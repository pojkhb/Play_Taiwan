using backend.Services;
using backend.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>商家端 NFC 貼紙綁定。</summary>
    [ApiController]
    [Route("api/merchant/nfc")]
    public class MerchantNfcController : ControllerBase
    {
        private readonly MerchantNfcService _service;

        public MerchantNfcController(MerchantNfcService service)
        {
            _service = service;
        }

        /// <summary>綁定 NFC 貼紙與優惠券。</summary>
        /// <remarks>商家掃描到 NFC 貼紙 UID 後呼叫；同一張貼紙重複綁定會回傳 409。</remarks>
        [HttpPost]
        [Route("bind")]
        public IActionResult Bind([FromBody] NfcBindRequest req)
        {
            _service.Bind(req.nfc_uid, req.coupon_id);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "NFC 貼紙綁定成功", Result = null });
        }
    }
}
