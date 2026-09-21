using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.Services.Neo4j;
using backend.ViewModels;

namespace backend.Services
{
    public class MerchantNfcService
    {
        private readonly MerchantNfcDao _dao;
        private readonly PlaceVersionChainService _placeService;

        public MerchantNfcService(MerchantNfcDao dao, PlaceVersionChainService placeService)
        {
            _dao = dao;
            _placeService = placeService;
        }

        /// <summary>商家掃描 NFC 貼紙後綁定優惠券；同一張貼紙重複綁定回傳 409。</summary>
        public void Bind(string nfcUid, int couponId)
        {
            if (_dao.ExistsNfcUid(nfcUid))
            {
                throw new ConflictException($"NFC 貼紙 {nfcUid} 已經綁定過優惠券");
            }
            _dao.Bind(nfcUid, couponId);
        }

        /// <summary>
        /// 使用者掃描 NFC 貼紙：商家資訊來自 Neo4j（含版本鏈覆蓋後的最新內容），
        /// 優惠券資訊來自 MySQL；auId 有值時同時寫入領取紀錄（已領取過則不重複寫入）。
        /// </summary>
        public async Task<NfcScanResponse> ScanAsync(string nfcUid, int? auId)
        {
            (var coupon, string storeUid) = _dao.GetScanInfo(nfcUid);
            if (coupon == null)
            {
                throw new NotFoundException($"找不到 NFC 貼紙 {nfcUid} 對應的優惠券資料");
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

            return new NfcScanResponse
            {
                coupon = coupon,
                merchant_place = merchantPlace,
                is_newly_claimed = isNewlyClaimed
            };
        }

        /// <summary>核銷優惠券：更新 user_coupon 並遞增 nfc_coupon.used_count。</summary>
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
