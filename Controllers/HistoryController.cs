using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 過往紀錄相關 API。
    /// 對應頁面：過往、過往－卷、過往－明信片vlog。
    /// </summary>

    [ApiController]
    [Route("api/[controller]")]
    // 過往 (對應畫面: 過往, 過往-卷, 過往-明信片vlog)
    public class HistoryController : ControllerBase
    {
        private readonly ILogger<HistoryController> _logger;
        private readonly HistoryService _service;

        public HistoryController(ILogger<HistoryController> logger, HistoryService service)
        {
            _logger = logger;
            _service = service;
        }

        // 從目前登入的 JWT Token 取得探員代號(ep_id)，供查詢過往紀錄時判斷是哪位探員
        private string GetCurrentEpId()
        {
            var epIdClaim = User.FindFirst("ep_id") ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (epIdClaim == null) throw new Exception("無法取得當前探員身分，請重新登入");
            return epIdClaim.Value;
        }

        #region 取得所有過往劇本

        /// <summary>
        /// 取得目前探員的所有過往劇本清單。
        /// </summary>
        /// <remarks>
        /// 對應「過往」頁面的收藏館列表，顯示已完成的劇本卷。
        /// 需在 Header 帶登入取得的 JWT Token：Authorization: Bearer {token}。
        ///
        /// Request 範例：
        ///
        ///     GET /api/History
        ///
        /// Response 範例：
        ///
        ///     {
        ///       "isSuccess": true,
        ///       "message": "查詢成功",
        ///       "Result": [
        ///         {
        ///           "story_id": "story_tainan_001",
        ///           "title": "府城儒生失落卷",
        ///           "synopsis": "尋著百年軌跡，找回失落記憶……",
        ///           "completed_date": "2026-08-09T00:00:00",
        ///           "region": "台南永康區",
        ///           "route_summary": null,
        ///           "vlog_id": "VLOG-001",
        ///           "postcard_review_url": null
        ///         }
        ///       ]
        ///     }
        /// </remarks>
        /// <returns>目前探員已完成的過往劇本清單，story_id 對應 md_story.story_id。</returns>
        // API：取得所有過往劇本（GetHistoryList）－回傳目前探員已完成的劇本清單
        [Authorize]
        [HttpGet]
        [Route("")]
        // GET: api/History
        public IActionResult GetHistoryList()
        {
            try
            {
                string epId = GetCurrentEpId();
                return Ok(new ResultViewModel<List<HistoryStoryItem>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetHistoryList(epId),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<HistoryStoryItem>> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 取得過往劇本詳情(卷)

        /// <summary>
        /// 取得單一過往劇本的詳細內容(卷)。
        /// </summary>
        /// <remarks>
        /// 對應「過往－卷」頁面，顯示完成日期、探索總覽等劇本詳情。
        /// 需在 Header 帶登入取得的 JWT Token：Authorization: Bearer {token}。
        ///
        /// Request 範例：
        ///
        ///     GET /api/History/story_tainan_001
        ///
        /// Response 範例：
        ///
        ///     {
        ///       "isSuccess": true,
        ///       "message": "查詢成功",
        ///       "Result": {
        ///         "story_id": "story_tainan_001",
        ///         "title": "府城儒生失落卷",
        ///         "synopsis": "尋著百年軌跡，找回失落記憶……",
        ///         "completed_date": "2026-08-09T00:00:00",
        ///         "region": "台南永康區",
        ///         "route_summary": null,
        ///         "vlog_id": "VLOG-001",
        ///         "postcard_review_url": null
        ///       }
        ///     }'
        /// [❌ 尚未完成]
        /// </remarks>
        /// <param name="story_id">劇本代號，對應 md_story.story_id，例如 story_tainan_001（此劇本代號會延續傳到 /api/Postcard/Story/{story_id}）。</param>
        /// <returns>指定劇本的詳細內容，若探員尚未完成此劇本則回傳 404。</returns>
        // API：取得過往劇本詳情（GetHistoryDetail）－回傳指定劇本的完成日期與探索總覽
        [Authorize]
        [HttpGet]
        [Route("{story_id}")]
        // GET: api/History/{story_id}
        public IActionResult GetHistoryDetail(string story_id)
        {
            try
            {
                string epId = GetCurrentEpId();
                return Ok(new ResultViewModel<HistoryStoryItem>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetHistoryDetail(story_id, epId),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<HistoryStoryItem> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion
    }
}