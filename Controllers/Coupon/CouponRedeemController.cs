using backend.Services;
using backend.utils;
using backend.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>使用者端優惠券核銷。</summary>
    [Authorize]
    [ApiController]
    [Route("api/coupons")]
    public class CouponRedeemController : ControllerBase
    {
        private readonly MerchantQrCodeService _service;

        public CouponRedeemController(MerchantQrCodeService service)
        {
            _service = service;
        }

        /// <summary>核銷優惠券。</summary>
        /// <remarks>
        /// 核銷登入者（JWT 的 au_id）自己領取的優惠券：更新 user_coupon.is_used/used_at，
        /// 並遞增 qrcode_coupon.used_count。不需要 request body。
        /// </remarks>
        [HttpPost]
        [Route("{couponId:int}/redeem")]
        public IActionResult Redeem(int couponId)
        {
            _service.Redeem(couponId, User.GetAuId());
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "優惠券核銷成功", Result = null });
        }
    }
}
