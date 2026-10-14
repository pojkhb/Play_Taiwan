using System.Threading.Tasks;
using backend.Services;
using backend.util;
using backend.utils;
using backend.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>使用者端 QR Code 掃描。</summary>
    [ApiController]
    [Route("api/qrcode")]
    public class QrCodeController : ControllerBase
    {
        private readonly MerchantQrCodeService _service;

        public QrCodeController(MerchantQrCodeService service)
        {
            _service = service;
        }

        /// <summary>掃描 QR Code 查詢商家與優惠券資訊。</summary>
        /// <remarks>
        /// 商家資訊來自 Neo4j（含版本鏈覆蓋後的最新內容），優惠券資訊來自 MySQL。
        /// 不需登入也能查看；帶遊客的 JWT 時，若尚未領取過此優惠券會同時寫入 user_coupon 領取紀錄。
        ///
        /// - image_url：店家照片，商家在店家資料填了照片網址（PUT api/Merchant/Profile 的 image_url）就用那張，
        ///   不然用商家的景點 ID 向 Neo4j 拿景點照片（「店家實景」）；沒有照片時為 null
        /// - merchant_place.images：這個景點在 Neo4j 的全部照片
        /// - operating_hours：店家營業時間，跟 merchant_place.operating_hours 同一份（依星期與開始時間排序），沒有時為空陣列
        /// </remarks>
        [AllowAnonymous]
        [HttpGet]
        [Route("scan/{qrUid}")]
        public async Task<IActionResult> Scan(string qrUid)
        {
            int? auId = User.IsInRole("Tourist") ? User.TryGetAuId() : null;
            QrCodeScanResponse result = await _service.ScanAsync(qrUid, auId);
            result.image_url = PublicUrl.Of(Request, result.image_url);
            return Ok(new ResultViewModel<QrCodeScanResponse> { isSuccess = true, message = "查詢成功", Result = result });
        }
    }
}
