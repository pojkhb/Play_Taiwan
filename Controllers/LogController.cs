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
    // 模組設定 Log
    public class LogController : ControllerBase
    {
        private readonly ILogger<LogController> _logger;
        private readonly LogService _service;
        private readonly SharedFunctionService _shareservice;

        public LogController(ILogger<LogController> logger, LogService service, SharedFunctionService shareservice)
        {
            _logger = logger;
            _service = service;
            _shareservice = shareservice;
        }

        [HttpGet]
        [Route("get")]
        // GET: api/Log/get
        public IActionResult Get()
        {
            try
            {
                return Ok(new ResultViewModel<List<LogResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.Get_Log(),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<LogResponse>>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null,
                });
            }
        }

        // [HttpDelete]
        // // Delete: api/Log/delete
		// [Route("delete")]
        // public IActionResult Delete(string md_id)
        // {
        //     try
        //     {
        //         string message = string.Empty;

        //         if (_shareservice.Get_CheckIfExists("md_id", md_id, "role_module") == false)
        //         {
        //             /* Log歷史紀錄 */
        //             message = "查無此權限，停用失敗";
        //             _shareservice.Insert_LogRecord(message);
        //             return BadRequest(new ResultViewModel<string>
        //             {
        //                 isSuccess = false,
        //                 message = message,
        //                 Result = null,
        //             });
        //         }
        //         else
        //         {
        //             /* 軟刪除(停用) */
        //             bool response = _service.Delete_Log(md_id);
        //             message = response ? "權限停用成功" : "權限停用失敗";
        //             /* Log歷史紀錄 */
        //             _shareservice.Insert_LogRecord(message);
        //             return Ok(new ResultViewModel<string>
        //             {
        //                 isSuccess = true,
        //                 message = message,
        //                 Result = null,
        //             });
        //         }
        //     }
        //     catch (Exception e)
        //     {
        //         /* Log歷史紀錄 */
        //         _shareservice.Insert_LogRecord(e.Message.ToString());
        //         return NotFound(new ResultViewModel<string>
        //         {
        //             isSuccess = false,
        //             message = e.Message.ToString(),
        //             Result = null,
        //         });
        //     }
        // }
    }
}
