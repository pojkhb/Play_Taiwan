using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 任務答題相關 API。
    /// 對應頁面：答題、答對、答錯、提示。
    /// </summary>

    [ApiController]
    [Route("api/[controller]")]
    // 任務答題 (對應畫面: 答題, 答對, 答錯, 提示)
    public class TaskController : ControllerBase
    {
        private readonly ILogger<TaskController> _logger;
        private readonly TaskService _service;

        public TaskController(ILogger<TaskController> logger, TaskService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 取得任務詳情

        /// <summary>
        /// 取得指定任務的詳細內容。
        /// </summary>
        /// <remarks>
        /// 對應「答題」頁面，顯示題目與選項內容。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Task/{task_id}
        /// </remarks>
        /// <param name="task_id">任務代號。</param>
        /// <returns>任務詳細內容，包含題目與選項。</returns>
        // API：取得任務詳情（GetTask）－回傳指定任務的題目與選項內容
        [HttpGet]
        [Route("{task_id}")]
        // GET: api/Task/{task_id}
        public IActionResult GetTask(string task_id)
        {
            try
            {
                return Ok(new ResultViewModel<TaskDetailResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetTaskDetail(task_id),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<TaskDetailResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 送出答案

        /// <summary>
        /// 送出任務答案並取得答題結果。
        /// </summary>
        /// <remarks>
        /// 對應「答對」與「答錯」頁面，判斷送出答案是否正確。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Task/Answer
        ///     {
        ///       "task_id": "T001",
        ///       "answer": "A"
        ///     }
        /// </remarks>
        /// <param name="req">答題請求資料，包含任務代號與選擇的答案。</param>
        /// <returns>答題結果，包含是否正確與後續資訊。</returns>
        // API：送出答案（Answer）－判斷送出的答案是否正確並回傳結果
        [HttpPost]
        [Route("Answer")]
        // POST: api/Task/Answer
        public IActionResult Answer([FromBody] TaskAnswerRequest req)
        {
            try
            {
                return Ok(new ResultViewModel<TaskAnswerResponse>
                {
                    isSuccess = true,
                    message = "送出成功",
                    Result = _service.SubmitAnswer(req),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<TaskAnswerResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 取得提示

        /// <summary>
        /// 取得指定任務的提示內容。
        /// </summary>
        /// <remarks>
        /// 對應「提示」頁面，顯示線索文字協助玩家答題。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Task/{task_id}/Hint
        /// </remarks>
        /// <param name="task_id">任務代號。</param>
        /// <returns>該任務的提示線索內容。</returns>
        // API：取得提示（GetHint）－回傳指定任務的線索提示內容
        [HttpGet]
        [Route("{task_id}/Hint")]
        // GET: api/Task/{task_id}/Hint
        public IActionResult GetHint(string task_id)
        {
            try
            {
                return Ok(new ResultViewModel<TaskHintResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetHint(task_id),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<TaskHintResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion
    }
}