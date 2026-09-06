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
    /// 提供前端取得探員已完成的劇本清單、觀看過往劇本詳細探索總覽等功能。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class HistoryController : ControllerBase
    {
        private readonly ILogger<HistoryController> _logger;
        private readonly HistoryService _service;

        public HistoryController(ILogger<HistoryController> logger, HistoryService service)
        {
            _logger = logger;
            _service = service;
        }

        // 從目前登入的 JWT Token 取得探員代號(ep_id)
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
        /// 
        /// **Request 範例**：
        /// 
        ///     GET /api/History
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": [
        ///     {
        ///       "story_id": "story_tainan_001",
        ///       "title": "府城儒生失落卷",
        ///       "synopsis": "尋著百年軌跡，找回失落記憶……",
        ///       "completed_date": "2026-08-09T00:00:00",
        ///       "region": "台南永康區",
        ///       "vlog_id": "VLOG-001",
        ///       "spots": null
        ///     }
        ///   ]
        /// }
        /// ```
        /// </remarks>
        [Authorize]
        [HttpGet]
        [Route("")]
        public IActionResult GetHistoryList()
        {
            try
            {
                string epId = GetCurrentEpId();
                return Ok(new ResultViewModel<List<HistoryStoryItem>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetHistoryList(epId)
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "取得過往劇本清單失敗");
                return StatusCode(500, new ResultViewModel<List<HistoryStoryItem>> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion

        #region 取得過往劇本詳情(卷)
        /// <summary>
        /// 取得單一過往劇本的詳細內容 (包含所有經歷過的景點清單)。
        /// </summary>
        /// <remarks>
        /// 對應「過往－卷」頁面，顯示完成日期、故事大綱以及探索總覽(Spots)。
        /// 
        /// **Request 範例**：
        /// 
        ///     GET /api/History/story_tainan_001
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": {
        ///     "story_id": "story_tainan_001",
        ///     "title": "府城儒生失落卷",
        ///     "synopsis": "尋著百年軌跡，找回失落記憶……",
        ///     "completed_date": "2026-08-09T00:00:00",
        ///     "region": "台南永康區",
        ///     "vlog_id": "VLOG-001",
        ///     "spots": [
        ///       "臺南孔廟",
        ///       "赤崁樓",
        ///       "神農街"
        ///     ]
        ///   }
        /// }
        /// ```
        /// </remarks>
        /// <param name="story_id">劇本代號，對應 md_story.story_id</param>
        [Authorize]
        [HttpGet]
        [Route("{story_id}")]
        public IActionResult GetHistoryDetail(string story_id)
        {
            try
            {
                string epId = GetCurrentEpId();
                return Ok(new ResultViewModel<HistoryStoryItem>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetHistoryDetail(story_id, epId)
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"取得過往劇本詳情失敗: {story_id}");
                return StatusCode(500, new ResultViewModel<HistoryStoryItem> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion
    }
}