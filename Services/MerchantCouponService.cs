using System.Collections.Generic;
using backend.dao;
using backend.Models;
using backend.ViewModels;

namespace backend.Services
{
    public class MerchantCouponService
    {
        private readonly MerchantCouponDao _dao;

        public MerchantCouponService(MerchantCouponDao dao)
        {
            _dao = dao;
        }

        public List<CouponResponse> GetByStore(int storeId) => _dao.GetByStore(storeId);

        public CouponResponse GetById(int couponId)
        {
            CouponResponse coupon = _dao.GetById(couponId);
            if (coupon == null) throw new NotFoundException($"找不到 coupon_id={couponId} 的優惠券");
            return coupon;
        }

        public int Create(CouponCreateRequest req)
        {
            ValidateDiscountType(req.discount_type);
            return _dao.Create(req);
        }

        public void Update(int couponId, CouponUpdateRequest req)
        {
            ValidateDiscountType(req.discount_type);
            bool updated = _dao.Update(couponId, req);
            if (!updated) throw new NotFoundException($"找不到 coupon_id={couponId} 的優惠券");
        }

        public void UpdateStatus(int couponId, string status)
        {
            if (status != "active" && status != "inactive")
            {
                throw new System.Exception("status 只能是 active 或 inactive");
            }
            bool updated = _dao.UpdateStatus(couponId, status);
            if (!updated) throw new NotFoundException($"找不到 coupon_id={couponId} 的優惠券");
        }

        public void Delete(int couponId)
        {
            bool deleted = _dao.Delete(couponId);
            if (!deleted) throw new NotFoundException($"找不到 coupon_id={couponId} 的優惠券");
        }

        private static void ValidateDiscountType(string discountType)
        {
            if (discountType != "percent" && discountType != "amount")
            {
                throw new System.Exception("discount_type 只能是 percent 或 amount");
            }
        }
    }
}
