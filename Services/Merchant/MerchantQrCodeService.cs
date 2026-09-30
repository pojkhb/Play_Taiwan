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

        public MerchantQrCodeService(MerchantQrCodeDao dao, PlaceVersionChainService placeService)
        {
            _dao = dao;
            _placeService = placeService;
        }

        /// <summary>
        /// 商家掃描 QR Code 後綁定優惠券：優惠券必須屬於此商家（sId 來自 JWT），
        /// 同一個 QR Code 重複綁定回傳 409。
        /// </summary>
        public void Bind(int sId, string qrUid, int couponId)
        {
            if (string.IsNullOrWhiteSpace(qrUid))
            {
                throw new BadRequestException("請提供 qr_uid");
            }
            if (!_dao.CouponBelongsToStore(couponId, sId))
            {
                throw new NotFoundException($"找不到 coupon_id={couponId} 的優惠券");
            }
            if (_dao.ExistsQrUid(qrUid))
            {
                throw new ConflictException($"QR Code {qrUid} 已經綁定過優惠券");
            }
            _dao.Bind(qrUid, couponId);
        }

        /// <summary>
        /// 使用者掃描 QR Code：商家資訊來自 Neo4j（含版本鏈覆蓋後的最新內容），
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
                is_newly_claimed = isNewlyClaimed
            };
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
