// 檔案路徑：System\Controllers\Vlog\VisitorVlogController.cs
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.utils;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 遊客 VLOG（旅遊回憶影片）。
    /// 流程：遊戲結束 → Preview（story_id）拿到旁白草稿 → 玩家確認旁白 → CreateFinal（story_id + 旁白），
    /// 後端自動抓這次遊玩拍的照片與景點資料打包送出 → 輪詢 Status/{story_id}?task_id= 直到 status=3。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class VisitorVlogController : ControllerBase
    {
        private readonly ILogger<VisitorVlogController> _logger;
        private readonly VisitorVlogService _service;

        public VisitorVlogController(ILogger<VisitorVlogController> logger, VisitorVlogService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 1. 旁白草稿

        /// <summary>
        /// 遊戲結束後，依 story_id 產生 VLOG 旁白草稿。
        /// </summary>
        /// <remarks>
        /// 後端會從資料庫組出玩家走過的景點、遊玩時長、任務數，交給 AI 產生旁白。
        ///
        /// **Request 範例**：
        /// ```json
        /// { "story_id": 1 }
        /// ```
        ///
        /// 回傳的 `script` 給玩家確認或修改，改完送到 CreateFinal。
        /// `photo_count` 為 0 時無法合成影片，前端可以先提示玩家。
        /// </remarks>
        [HttpPost]
        [Route("Preview")]
        [ProducesResponseType(typeof(ResultViewModel<VisitorVlogPreviewResponse>), 200)]
        public async Task<IActionResult> Preview([FromBody] VisitorVlogPreviewRequest request)
        {
            try
            {
                var result = await _service.PreviewAsync(User.GetAuId(), request.story_id);
                return Ok(new ResultViewModel<VisitorVlogPreviewResponse>
                {
                    isSuccess = true,
                    message = "旁白草稿產生成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "產生遊客 VLOG 草稿失敗（story_id={StoryId}）", request?.story_id);
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion

        #region 2. 送出影片合成

        /// <summary>
        /// 送出確認後的旁白，後端打包照片與景點資料送去合成影片。
        /// </summary>
        /// <remarks>
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "story_id": 1,
        ///   "final_script": "確認後的旁白…",
        ///   "promo_copy": "（選填）修改後的宣傳文案",
        ///   "seo_keywords": ["台中散步", "宮原眼科"]
        /// }
        /// ```
        /// 回傳的 `task_id` 要保存好，輪詢 Status 時帶回來。
        /// </remarks>
        [HttpPost]
        [Route("CreateFinal")]
        [ProducesResponseType(typeof(ResultViewModel<VisitorVlogTaskResponse>), 200)]
        public async Task<IActionResult> CreateFinal([FromBody] VisitorVlogCreateFinalRequest request)
        {
            try
            {
                var result = await _service.CreateFinalAsync(User.GetAuId(), request);
                return Ok(new ResultViewModel<VisitorVlogTaskResponse>
                {
                    isSuccess = true,
                    message = $"影片合成任務已建立（{result.photo_count} 張照片），請用 Status 查詢進度",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "送出遊客 VLOG 合成失敗（story_id={StoryId}）", request?.story_id);
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion

        #region 3. 查詢進度 / 結果

        /// <summary>
        /// 查詢這個劇本的 VLOG（影片、旁白、宣傳文案）。
        /// </summary>
        /// <remarks>
        /// status：1=待處理、2=處理中、3=已完成（`video_url` 有值）、4=失敗（看 `error_message`）。
        ///
        /// 處理中時要帶 CreateFinal 回傳的 `task_id` 才會向 AI 更新進度，建議每 5–10 秒輪詢一次；
        /// 不帶 `task_id` 只回傳資料庫目前的紀錄（例如從歷史紀錄頁查看已完成的影片）。
        ///
        ///     GET /api/VisitorVlog/Status/1?task_id=xxxx
        /// </remarks>
        [HttpGet]
        [Route("Status/{story_id:int}")]
        [ProducesResponseType(typeof(ResultViewModel<VisitorVlogStatusResponse>), 200)]
        public async Task<IActionResult> Status(int story_id, [FromQuery] string task_id)
        {
            try
            {
                var result = await _service.GetStatusAsync(User.GetAuId(), story_id, task_id);
                return Ok(new ResultViewModel<VisitorVlogStatusResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢遊客 VLOG 狀態失敗（story_id={StoryId}）", story_id);
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion
    }
}
