using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;

namespace backend.Controllers
{
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