using backend.Services;
using backend.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>使用者端優惠券核銷。</summary>
    [ApiController]
    [Route("api/coupons")]
    public class CouponRedeemController : ControllerBase
    {
        private readonly MerchantNfcService _service;
        private readonly ICurrentActorProvider _actorProvider;

        public CouponRedeemController(MerchantNfcService service, ICurrentActorProvider actorProvider)
        {
            _service = service;
            _actorProvider = actorProvider;
        }

        /// <summary>核銷優惠券。</summary>
        /// <remarks>更新 user_coupon.is_used/used_at，並遞增 nfc_coupon.used_count。</remarks>
        [HttpPost]
        [Route("{couponId:int}/redeem")]
        public IActionResult Redeem(int couponId, [FromBody] CouponRedeemRequest req)
        {
            int? auId = _actorProvider.GetAuId(req?.au_id);
            if (!auId.HasValue)
            {
                return BadRequest(new ResultViewModel<object> { isSuccess = false, message = "請提供 au_id", Result = null });
            }

            _service.Redeem(couponId, auId.Value);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "優惠券核銷成功", Result = null });
        }
    }
}
