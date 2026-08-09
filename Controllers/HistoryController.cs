using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;

namespace backend.Controllers
{
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