// 檔案路徑：System\Controllers\MerchantVlogController.cs
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 商家端 Vlog 行銷影片相關 API。
    /// 對應頁面：生成（輸入資訊）→ 生成中 → 生成end → 完成。
    /// 流程：先呼叫 Preview 取得 AI 草稿旁白，使用者確認/微調後，呼叫 CreateFinal 送出正式合成，
    /// 前端再輪詢 Status 直到影片完成（完成時才寫入 ep_vlog）。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class MerchantVlogController : ControllerBase
    {
        private readonly ILogger<MerchantVlogController> _logger;
        private readonly MerchantVlogService _service;

        public MerchantVlogController(ILogger<MerchantVlogController> logger, MerchantVlogService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 1. 生成腳本預覽

        /// <summary>
        /// 依店家名稱、推薦資訊、敘事語氣，產生 AI 草稿旁白與行銷文案。
        /// </summary>
        [HttpPost]
        [Route("Preview")]
        public async Task<IActionResult> Preview([FromBody] MerchantVlogPreviewRequest request)
        {
            try
            {
                var result = await _service.GetPreviewAsync(request);
                return Ok(new ResultViewModel<MerchantVlogPreviewApiResponse>
                {
                    isSuccess = true,
                    message = "腳本預覽產生成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "產生商家 Vlog 腳本預覽時發生錯誤");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion

        #region 2. 正式合成影片

        /// <summary>
        /// 送出正式影片合成任務，回傳 task_id 供前端輪詢。**必須帶 Token**
        /// （因 ep_vlog.ep_id 為 not null，需在完成時綁定操作此功能的帳號）。
        /// </summary>
        [HttpPost]
        [Route("CreateFinal")]
        [Authorize]
        public async Task<IActionResult> CreateFinal([FromForm] MerchantVlogCreateFinalRequest request)
        {
            var epId = User.FindFirst("ep_id")?.Value ?? User.Identity?.Name;
            if (string.IsNullOrEmpty(epId))
            {
                return Unauthorized(new ResultViewModel<string> { isSuccess = false, message = "無法驗證身分，請重新登入" });
            }

            try
            {
                var result = await _service.CreateFinalVlogAsync(request);
                return Ok(new ResultViewModel<VlogCreateFinalApiResponse>
                {
                    isSuccess = true,
                    message = "影片合成任務已建立，請透過 Status API 輪詢進度",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "建立商家 Vlog 合成任務時發生錯誤");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion

        #region 3. 查詢任務狀態（輪詢）

        /// <summary>
        /// 依 task_id 查詢影片合成進度。**必須帶 Token**。查到 ready 時自動寫入 ep_vlog。
        /// </summary>
        [HttpGet]
        [Route("Status/{taskId}")]
        [Authorize]
        public async Task<IActionResult> Status(string taskId)
        {
            var epId = User.FindFirst("ep_id")?.Value ?? User.Identity?.Name;
            if (string.IsNullOrEmpty(epId))
            {
                return Unauthorized(new ResultViewModel<string> { isSuccess = false, message = "無法驗證身分，請重新登入" });
            }

            try
            {
                var result = await _service.CheckStatusAsync(taskId, epId);
                return Ok(new ResultViewModel<VlogTaskStatusApiResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"查詢商家 Vlog 任務 {taskId} 狀態時發生錯誤");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion
    }
}
