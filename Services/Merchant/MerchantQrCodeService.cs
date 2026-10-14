using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.Services.Neo4j;
using backend.ViewModels;

namespace backend.Services
{
    public class MerchantQrCodeService
    {
        private readonly MerchantQrCodeDao _dao;
        private readonly PlaceVersionChainService _placeService;
        private readonly PlaceLookupService _placeLookup;

        public MerchantQrCodeService(MerchantQrCodeDao dao, PlaceVersionChainService placeService, PlaceLookupService placeLookup)
        {
            _dao = dao;
            _placeService = placeService;
            _placeLookup = placeLookup;
        }

        /// <summary>
        /// 商家掃描 QR Code 後綁定優惠券：優惠券必須屬於此商家（sId 來自 JWT），
        /// 同一個 QR Code 重複綁定、或優惠券已經綁過別的 QR Code 都回傳 409（一張優惠券只能綁一個 qr_uid），
        /// 檢查與寫入在 DAO 的同一個 Transaction 內，同時送來的綁定也擋得住。
        /// </summary>
        public void Bind(int sId, string qrUid, int couponId)
        {
            if (string.IsNullOrWhiteSpace(qrUid))
            {
                throw new BadRequestException("請提供 qr_uid");
            }
            _dao.Bind(sId, qrUid, couponId);
        }

        /// <summary>
        /// 使用者掃描 QR Code：商家資訊與店家照片來自 Neo4j（含版本鏈覆蓋後的最新內容），
        /// 優惠券資訊來自 MySQL；auId 有值時同時寫入領取紀錄（已領取過則不重複寫入）。
        /// </summary>
        public async Task<QrCodeScanResponse> ScanAsync(string qrUid, int? auId)
        {
            (var coupon, string storeUid) = _dao.GetScanInfo(qrUid);
            if (coupon == null)
            {
                throw new NotFoundException($"找不到 QR Code {qrUid} 對應的優惠券資料");
            }

            PlaceCurrentInfo merchantPlace = string.IsNullOrWhiteSpace(storeUid)
                ? null
                : await _placeService.GetCurrentVersionAsync(storeUid);

            // 店家照片用商家的景點 ID 向 Neo4j 拿：商家在店家資料填了照片網址就用那張，不然用景點照片（跟地圖、劇本同一張）
            string imageUrl = string.IsNullOrWhiteSpace(storeUid)
                ? null
                : (await _placeLookup.GetStoreImagesAsync(new[] { storeUid })).GetValueOrDefault(storeUid);

            bool isNewlyClaimed = false;
            if (auId.HasValue)
            {
                bool alreadyClaimed = _dao.HasClaimed(auId.Value, coupon.coupon_id);
                if (!alreadyClaimed)
                {
                    _dao.ClaimCoupon(auId.Value, coupon.coupon_id);
                    isNewlyClaimed = true;
                }
            }

            return new QrCodeScanResponse
            {
                coupon = coupon,
                merchant_place = merchantPlace,
                image_url = imageUrl,
                operating_hours = merchantPlace?.operating_hours ?? new List<PlaceOperatingHourItem>(),
                is_newly_claimed = isNewlyClaimed
            };
        }

        /// <summary>
        /// 使用者領過的全部優惠券（含已核銷、已下架、已過期，用 is_used／is_usable 區分），新領的排前面。
        /// 店家照片用商家的景點 ID 一次向 Neo4j 拿，跟掃 QR Code 是同一張（商家填的照片網址優先）；Neo4j 連不上時改用 MySQL 的照片，都沒有時為 null。
        /// 營業時間也一次向 Neo4j 拿，Neo4j 連不上時為空陣列。
        /// </summary>
        public async Task<List<MyCouponResponse>> GetMyCouponsAsync(int auId)
        {
            List<MerchantQrCodeDao.UserCouponRow> rows = _dao.GetUserCoupons(auId);
            Dictionary<string, string> images = await _placeLookup.GetStoreImagesAsync(rows.Select(r => r.store_uid));
            Dictionary<string, List<PlaceOperatingHourItem>> hours = await _placeLookup.GetOperatingHoursAsync(rows.Select(r => r.store_uid));

            return rows.Select(r => new MyCouponResponse
            {
                coupon = new CouponResponse
                {
                    coupon_id = r.coupon_id,
                    s_id = r.s_id,
                    coupon_code = r.coupon_code,
                    coupon_name = r.coupon_name,
                    discount_commodity = r.discount_commodity,
                    discount_type = r.discount_type,
                    discount_value = r.discount_value,
                    valid_from = r.valid_from,
                    valid_to = r.valid_to,
                    status = r.status
                },
                store_name = r.store_name,
                full_address = r.full_address,
                image_url = r.store_uid != null ? images.GetValueOrDefault(r.store_uid) : null,
                operating_hours = (r.store_uid != null ? hours.GetValueOrDefault(r.store_uid) : null) ?? new List<PlaceOperatingHourItem>(),
                obtained_at = r.obtained_at,
                is_used = r.is_used,
                used_at = r.used_at,
                is_usable = r.is_usable
            }).ToList();
        }

        /// <summary>核銷優惠券：更新 user_coupon 並遞增 qrcode_coupon.used_count。</summary>
        public void Redeem(int couponId, int auId)
        {
            bool redeemed = _dao.Redeem(couponId, auId);
            if (!redeemed)
            {
                throw new ConflictException("此優惠券尚未領取，或已經核銷過，無法重複核銷");
            }
        }
    }
}
