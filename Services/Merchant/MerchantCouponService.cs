using System.Collections.Generic;
using backend.dao;
using backend.Models;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>商家優惠券維護。sId 為登入商家（JWT）的 s_id，單筆操作都限定在自己店家的優惠券。</summary>
    public class MerchantCouponService
    {
        private readonly MerchantCouponDao _dao;

        public MerchantCouponService(MerchantCouponDao dao)
        {
            _dao = dao;
        }

        public List<CouponResponse> GetByStore(int sId) => _dao.GetByStore(sId);

        public CouponResponse GetById(int sId, int couponId)
        {
            CouponResponse coupon = _dao.GetById(sId, couponId);
            if (coupon == null) throw new NotFoundException($"找不到 coupon_id={couponId} 的優惠券");
            return coupon;
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
