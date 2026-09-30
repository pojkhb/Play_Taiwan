using backend.Services;
using backend.utils;
using backend.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>商家端 QR Code 綁定。商家身分由 JWT 的 s_id 取得。</summary>
    [Authorize(Roles = "Merchant")]
    [ApiController]
    [Route("api/merchant/qrcode")]
    public class MerchantQrCodeController : ControllerBase
    {
        private readonly MerchantQrCodeService _service;

        public MerchantQrCodeController(MerchantQrCodeService service)
        {
            _service = service;
        }

        /// <summary>綁定 QR Code 與優惠券。</summary>
        /// <remarks>
        /// 商家掃描到 QR Code 的 qr_uid 後呼叫；優惠券必須是自己店家的，
        /// 同一個 QR Code 重複綁定會回傳 409。
        /// </remarks>
        [HttpPost]
        [Route("bind")]
        public IActionResult Bind([FromBody] QrCodeBindRequest req)
        {
            _service.Bind(User.GetSId(), req.qr_uid, req.coupon_id);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "QR Code 綁定成功", Result = null });
        }
    }
}
