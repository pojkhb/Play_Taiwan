using System.Threading.Tasks;
using backend.Services;
using backend.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>使用者端 NFC 掃描。</summary>
    [ApiController]
    [Route("api/nfc")]
    public class NfcController : ControllerBase
    {
        private readonly MerchantNfcService _service;
        private readonly ICurrentActorProvider _actorProvider;

        public NfcController(MerchantNfcService service, ICurrentActorProvider actorProvider)
        {
            _service = service;
            _actorProvider = actorProvider;
        }

        /// <summary>掃描 NFC 貼紙查詢商家與優惠券資訊。</summary>
        /// <remarks>
        /// 商家資訊來自 Neo4j（含版本鏈覆蓋後的最新內容），優惠券資訊來自 MySQL；
        /// 帶入 auId 時，若尚未領取過此優惠券會同時寫入 user_coupon 領取紀錄。
        /// </remarks>
        [HttpGet]
        [Route("scan/{nfcUid}")]
        public async Task<IActionResult> Scan(string nfcUid, [FromQuery] int? auId)
        {
            int? currentAuId = _actorProvider.GetAuId(auId);
            NfcScanResponse result = await _service.ScanAsync(nfcUid, currentAuId);
            return Ok(new ResultViewModel<NfcScanResponse> { isSuccess = true, message = "查詢成功", Result = result });
        }
    }
}
