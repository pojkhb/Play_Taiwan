using System;
using System.Collections.Generic;
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

        #region 取得所有過往劇本

        /// <summary>
        /// 取得目前探員的所有過往劇本清單。
        /// </summary>
        /// <remarks>
        /// 對應「過往」頁面的收藏館列表，顯示已完成的劇本卷。
        ///
        /// Request 範例：
        ///
        ///     GET /api/History
        /// </remarks>
        /// <returns>過往劇本清單。</returns>
        // API：取得所有過往劇本（GetHistoryList）－回傳目前探員已完成的劇本清單
        [HttpGet]
        [Route("")]
        // GET: api/History
        public IActionResult GetHistoryList()
        {
            try
            {
                return Ok(new ResultViewModel<List<HistoryStoryItem>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetHistoryList(),
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
        ///
        /// Request 範例：
        ///
        ///     GET /api/History/{story_id}
        /// </remarks>
        /// <param name="story_id">劇本代號。</param>
        /// <returns>指定劇本的詳細內容。</returns>
        // API：取得過往劇本詳情（GetHistoryDetail）－回傳指定劇本的完成日期與探索總覽
        [HttpGet]
        [Route("{story_id}")]
        // GET: api/History/{story_id}
        public IActionResult GetHistoryDetail(string story_id)
        {
            try
            {
                return Ok(new ResultViewModel<HistoryStoryItem>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetHistoryDetail(story_id),
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