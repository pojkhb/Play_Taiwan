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
    // 模組設定 ModuleSetting
    public class ModuleSettingController : ControllerBase
    {
        private readonly ILogger<ModuleSettingController> _logger;
        private readonly ModuleSettingService _service;
        private readonly SharedFunctionService _shareservice;

        public ModuleSettingController(ILogger<ModuleSettingController> logger, ModuleSettingService service, SharedFunctionService shareservice)
        {
            _logger = logger;
            _service = service;
            _shareservice = shareservice;
        }

        #region 模組設定-列表資料
        [HttpGet]
        [Route("get")]
        // GET: api/ModuleSetting/get
        public IActionResult Get()
        {
            try
            {
                return Ok(new ResultViewModel<List<ModuleResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.Get_ModuleSetting(),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<ModuleResponse>>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null,
                });
            }
        }
        #endregion

        #region 模組設定-軟刪除功能(停用)
        [HttpDelete]
        // Delete: api/ModuleSetting/delete
		[Route("delete")]
        public IActionResult Delete(string md_id)
        {
            try
            {
                string message = string.Empty;

                if (_shareservice.Get_CheckIfExists("md_id", md_id, "module") == false)
                {
                    message = "查無此模組，停用失敗";
                    /* Log歷史紀錄 */
                    _shareservice.Insert_LogRecord(message);
                    return BadRequest(new ResultViewModel<string>
                    {
                        isSuccess = false,
                        message = message,
                        Result = null,
                    });
                }
                else
                {
                    /* 軟刪除(停用) */
                    bool response = _service.Delete_ModuleSetting(md_id);
                    message = response ? "模組停用成功" : "模組停用失敗";
                    /* Log歷史紀錄 */
                    _shareservice.Insert_LogRecord(message);
                    return Ok(new ResultViewModel<string>
                    {
                        isSuccess = true,
                        message = message,
                        Result = null,
                    });
                }
            }
            catch (Exception e)
            {
                _shareservice.Insert_LogRecord(e.Message.ToString());
                return NotFound(new ResultViewModel<string>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null,
                });
            }
        }
        #endregion
    }
}
