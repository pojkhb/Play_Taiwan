using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 任務線索提示 API。
    /// 對應頁面：提示／答題。依玩家答錯次數提供兩階段提示。
    /// </summary>

    [ApiController]
    [Route("api/[controller]")]
    // 線索提示
    public class TaskHintController : ControllerBase
    {
        private readonly ILogger<TaskHintController> _logger;
        private readonly TaskHintService _service;

        public TaskHintController(ILogger<TaskHintController> logger, TaskHintService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 取得任務提示

        /// <summary>
        /// 依玩家目前答錯次數，取得對應階段的線索提示。
        /// </summary>
        /// <remarks>
        /// Request 範例：
        ///
        ///     POST /api/TaskHint/GetHint
        ///     { "taskId": "task_confucius_001", "wrongCount": 2 }
        /// </remarks>
        /// <param name="request">任務識別碼與目前答錯次數。</param>
        /// <returns>提示內容，若尚未達到提示門檻則 Available 為 false。</returns>
        // API：取得任務提示（GetHint）－依答錯次數回傳對應階段提示
        [HttpPost]
        [Route("GetHint")]
        // POST: api/TaskHint/GetHint
        public async System.Threading.Tasks.Task<IActionResult> GetHint([FromBody] HintRequest request)
        {
            try
            {
                var result = await _service.GetHintAsync(request.TaskId, request.WrongCount);
                return Ok(new ResultViewModel<HintResponse> { isSuccess = true, message = "查詢成功", Result = result });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<HintResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion
    }
}