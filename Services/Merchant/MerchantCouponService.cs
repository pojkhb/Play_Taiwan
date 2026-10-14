using System.Collections.Generic;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.Services.Neo4j;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>商家優惠券維護。sId 為登入商家（JWT）的 s_id，單筆操作都限定在自己店家的優惠券。</summary>
    public class MerchantCouponService
    {
        private readonly MerchantCouponDao _dao;
        private readonly PlaceLookupService _placeLookup;

        public MerchantCouponService(MerchantCouponDao dao, PlaceLookupService placeLookup)
        {
            _dao = dao;
            _placeLookup = placeLookup;
        }

        /// <summary>自己店家的優惠券，每張附上店家目前的營業時間（Neo4j 連不上時為空陣列）。</summary>
        public async Task<List<MerchantCouponResponse>> GetByStoreAsync(int sId)
        {
            List<MerchantCouponResponse> coupons = _dao.GetByStore(sId);
            if (coupons.Count == 0) return coupons;

            List<PlaceOperatingHourItem> hours = await GetStoreOperatingHoursAsync(sId);
            foreach (MerchantCouponResponse coupon in coupons)
                coupon.operating_hours = hours;
            return coupons;
        }

        public async Task<MerchantCouponResponse> GetByIdAsync(int sId, int couponId)
        {
            MerchantCouponResponse coupon = _dao.GetById(sId, couponId);
            if (coupon == null) throw new NotFoundException($"找不到 coupon_id={couponId} 的優惠券");
            coupon.operating_hours = await GetStoreOperatingHoursAsync(sId);
            return coupon;
        }

        private async Task<List<PlaceOperatingHourItem>> GetStoreOperatingHoursAsync(int sId)
        {
            string storeUid = _dao.GetStoreUid(sId);
            if (string.IsNullOrWhiteSpace(storeUid)) return new List<PlaceOperatingHourItem>();

            return (await _placeLookup.GetOperatingHoursAsync(new[] { storeUid })).GetValueOrDefault(storeUid)
                ?? new List<PlaceOperatingHourItem>();
        }

        public int Create(int sId, CouponCreateRequest req)
        {
            ValidateDiscountType(req.discount_type);
            return _dao.Create(sId, req);
        }

        public void Update(int sId, int couponId, CouponUpdateRequest req)
        {
            ValidateDiscountType(req.discount_type);
            bool updated = _dao.Update(sId, couponId, req);
            if (!updated) throw new NotFoundException($"找不到 coupon_id={couponId} 的優惠券");
        }

        public void UpdateStatus(int sId, int couponId, string status)
        {
            if (status != "active" && status != "inactive")
            {
                throw new BadRequestException("status 只能是 active 或 inactive");
            }
            bool updated = _dao.UpdateStatus(sId, couponId, status);
            if (!updated) throw new NotFoundException($"找不到 coupon_id={couponId} 的優惠券");
        }

        public void Delete(int sId, int couponId)
        {
            bool deleted = _dao.Delete(sId, couponId);
            if (!deleted) throw new NotFoundException($"找不到 coupon_id={couponId} 的優惠券");
        }

        private static void ValidateDiscountType(string discountType)
        {
            if (discountType != "percent" && discountType != "amount")
            {
                throw new BadRequestException("discount_type 只能是 percent 或 amount");
            }
        }
    }
}
