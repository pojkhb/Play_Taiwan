using System;
using System.Collections.Generic;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using backend.Services;
using backend.Models;
using backend.ViewModels;

namespace backend.Controllers.transportation
{
    [ApiController]
    [Route("api/[controller]")]
    // 角色權限設定 RoleModuleSetting
    public class RoleModuleSettingController : ControllerBase
    {
        private readonly ILogger<RoleModuleSettingController> _logger;
        private readonly RoleModuleSettingService _service;
        private readonly SharedFunctionService _shareservice;

        public RoleModuleSettingController(ILogger<RoleModuleSettingController> logger, RoleModuleSettingService service, SharedFunctionService shareservice)
        {
            _logger = logger;
            _service = service;
            _shareservice = shareservice;
        }

        #region 權限管理-列表資料
        [HttpGet]
        [Route("get")]
        //GET: api/RoleModuleSetting/get
        public IActionResult Get()
        {
            try
            {
                return Ok(new ResultViewModel<List<RoleModuleResponse>>
                {
                    isSuccess = true,
                    message = string.Empty,
                    Result = _service.Get_RoleModuleSetting(),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<RoleModuleResponse>>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null,
                });
            }
        }
        #endregion

        #region 權限管理-新增角色權限
        [HttpPost]
        [Route("post")]
        // POST: api/RoleModuleSetting/post
        public IActionResult Post([FromBody] RoleModuleRequest req)
        {
            try
            {
                string message = string.Empty;

                if (_shareservice.Get_CheckIfExists("role_name", req.role_name, "role") == true)
                {
                    message = "此角色已存在，新增失敗";
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
                    bool response = _service.Insert_RoleModuleSetting(req);
                    message = response ? "角色新增成功" : "角色新增失敗";
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
                /* Log歷史紀錄 */
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
        
        #region 權限管理-修改角色權限
        [HttpPut]
        [Route("put")]
        // PUT: api/RoleModuleSetting/put
        public IActionResult Put([FromBody] RoleModuleRequest req)
        {
            try
            {
                string message = string.Empty;

                if (_shareservice.Get_CheckIfExists("role_id", req.role_id.ToString(), "role") == false)
                {
                    message = "查無此角色，修改失敗";
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
                    bool response = _service.Update_RoleModuleSetting(req);
                    message = response ? "角色修改成功" : "角色修改失敗";
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
                /* Log歷史紀錄 */
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

        #region 權限管理-軟刪除角色權限(停用)
        [HttpDelete]
        // Delete: api/RoleModuleSetting/delete
		[Route("delete")]
        public IActionResult Delete(byte role_id)
        {
            try
            {
                string message = string.Empty;

                if (_shareservice.Get_CheckIfExists("role_id", role_id.ToString(), "role") == false)
                {
                    message = "查無此角色，停用失敗";
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
                    bool response = _service.Delete_RoleModuleSetting(role_id);
                    message = response ? "角色停用成功" : "角色停用失敗";
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
                /* Log歷史紀錄 */
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
