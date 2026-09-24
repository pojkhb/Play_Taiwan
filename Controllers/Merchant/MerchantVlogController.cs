// 檔案路徑：System\Controllers\Merchant\MerchantVlogController.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.utils;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 商家 VLOG（Reels 行銷影片）。
    /// 流程：Tones 取語氣選項 → Preview 輸入店家資訊與照片、拿到 AI 旁白草稿 / 推薦配文 / TAG
    /// → 商家確認旁白後 CreateFinal 送出合成 → 輪詢 Status/{mm_id} 直到 status=3 拿到影片。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MerchantVlogController : ControllerBase
    {
        private readonly ILogger<MerchantVlogController> _logger;
        private readonly MerchantVlogService _service;

        public MerchantVlogController(ILogger<MerchantVlogController> logger, MerchantVlogService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 0. 敘事語氣選項

        /// <summary>
        /// 取得可選的敘事語氣（例如幽默詼諧、質感專業、溫情走心）。
        /// </summary>
        [HttpGet]
        [Route("Tones")]
        [ProducesResponseType(typeof(ResultViewModel<List<NarrativeToneItem>>), 200)]
        public async Task<IActionResult> Tones()
        {
            try
            {
                return Ok(new ResultViewModel<List<NarrativeToneItem>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = await _service.GetTonesAsync()
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢敘事語氣失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion

        #region 1. 產生草稿

        /// <summary>
        /// 輸入店家資訊與照片，產生 AI 旁白草稿、推薦配文、TAG。
        /// </summary>
        /// <remarks>
        /// multipart/form-data。對應「生成（輸入資訊）」頁面。
        ///
        /// - `store_name`、`address` 不填會帶商家資訊表的資料
        /// - `images` 可重複多次（多張照片），順序就是影片中的順序；新專案至少一張
        /// - 想換語氣重新產生：帶同一個 `mm_id` 再呼叫一次，不帶 `images` 就沿用已上傳的照片
        ///
        /// 回傳的 `script` 給商家確認或修改，改完送到 CreateFinal。
        /// </remarks>
        [HttpPost]
        [Route("Preview")]
        [RequestSizeLimit(200 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 200 * 1024 * 1024)]
        [ProducesResponseType(typeof(ResultViewModel<MerchantVlogPreviewResponse>), 200)]
        public async Task<IActionResult> Preview([FromForm] MerchantVlogPreviewRequest request)
        {
            try
            {
                var result = await _service.PreviewAsync(User.GetAuId(), request, $"{Request.Scheme}://{Request.Host}");
                return Ok(new ResultViewModel<MerchantVlogPreviewResponse>
                {
                    isSuccess = true,
                    message = "草稿產生成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "產生商家 VLOG 草稿失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion

        #region 2. 送出影片合成

        /// <summary>
        /// 商家確認旁白後，把專案照片打包送去合成影片。
        /// </summary>
        /// <remarks>
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "mm_id": 12,
        ///   "final_script": "確認後的旁白…",
        ///   "caption": "（選填）修改後的推薦配文",
        ///   "hashtags": ["#玉井美食", "#芒果冰"]
        /// }
        /// ```
        /// 送出後用 Status/{mm_id} 輪詢進度。
        /// </remarks>
        [HttpPost]
        [Route("CreateFinal")]
        [ProducesResponseType(typeof(ResultViewModel<MerchantVlogTaskResponse>), 200)]
        public async Task<IActionResult> CreateFinal([FromBody] MerchantVlogCreateFinalRequest request)
        {
            try
            {
                var result = await _service.CreateFinalAsync(User.GetAuId(), request);
                return Ok(new ResultViewModel<MerchantVlogTaskResponse>
                {
                    isSuccess = true,
                    message = "影片合成任務已建立，請用 Status 查詢進度",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "送出商家 VLOG 合成失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion

        #region 3. 查詢進度 / 結果

        /// <summary>
        /// 查詢商家 VLOG 專案：影片、推薦配文、TAG。
        /// </summary>
        /// <remarks>
        /// status：1=草稿、2=處理中、3=已完成（`video_url` 有值）、4=失敗（看 `error_message`）。
        /// 處理中時每次呼叫都會向 AI 服務查一次進度，建議每 5–10 秒輪詢一次。
        /// </remarks>
        [HttpGet]
        [Route("Status/{mm_id:int}")]
        [ProducesResponseType(typeof(ResultViewModel<MerchantVlogStatusResponse>), 200)]
        public async Task<IActionResult> Status(int mm_id)
        {
            try
            {
                var result = await _service.GetStatusAsync(User.GetAuId(), mm_id);
                return Ok(new ResultViewModel<MerchantVlogStatusResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢商家 VLOG {MmId} 狀態失敗", mm_id);
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion
    }
}
