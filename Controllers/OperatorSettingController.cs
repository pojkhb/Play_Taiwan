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
    // 使用者資料維護 OperatorSetting
    public class OperatorSettingController : ControllerBase
    {
        private readonly ILogger<OperatorSettingController> _logger;
        private readonly OperatorSettingService _service;
        private readonly SharedFunctionService _shareservice;

        public OperatorSettingController(ILogger<OperatorSettingController> logger, OperatorSettingService service, SharedFunctionService shareservie)
        {
            _logger = logger;
            _service = service;
            _shareservice = shareservie;
        }

        #region 帳號管理-列表資料(報表下載)
        [HttpPost]
        [Route("Export")]
        // POST: api/OperatorSetting/Export
        public IActionResult OperatorSettingExport()
        {
            try
            {
                var opId = HttpContext.Items["op_id"]?.ToString();
                if (string.IsNullOrWhiteSpace(opId))
                    return StatusCode(401, "Unauthorized");

                var allowAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
               {
                "adminhsinbow",
                "admin"
               };
                if (!allowAccounts.Contains(opId))
                    return StatusCode(403, "Forbidden");

                var Res = _service.OperatorSettingExport();
                if (Res == null)
                {
                    return NotFound(new ResultViewModel<string>
                    {
                        isSuccess = false,
                        message = @$"查詢範圍內尚無資料！",
                        Result = null,
                    });
                }

                string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                string FileName = Res["Name"].ToString();
                string FileExtension = "xlsx";
                var base64 = Convert.ToBase64String(Res["FileContent"] as byte[]);

                return Ok(new ExportFileViewModel()
                {
                    FileName = FileName,
                    FileContent = $"data:{ContentType};base64,{base64}",
                    Extension = FileExtension,
                    FullFileName = $"{FileName}.{FileExtension}",
                });
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

        #region 個人管理-個人資料
        [HttpGet]
        [Route("getUserInfo")]
        // GET: api/OperatorSetting/getUserInfo
        public IActionResult GetUserInfo()
        {
            try
            {
                var op_id = HttpContext.Items["op_id"]?.ToString();
                return Ok(new ResultViewModel<OperatorResponse>
                {
                    isSuccess = true,
                    message = string.Empty,
                    Result = _service.GetUserInfo_OperatorSetting(op_id),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<OperatorResponse>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null,
                });
            }
        }
        #endregion

        #region 帳號管理-列表資料
        [HttpGet]
        [Route("get")]
        // GET: api/OperatorSetting/get
        public IActionResult Get()
        {
            try
            {
                return Ok(new ResultViewModel<List<OperatorResponse>>
                {
                    isSuccess = true,
                    message = string.Empty,
                    Result = _service.Get_OperatorSetting(),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<OperatorResponse>>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null,
                });
            }
        }
        #endregion

        #region 帳號管理-新增功能
        [HttpPost]
        [Route("post")]
        // POST: api/OperatorSetting/post
        public IActionResult Post([FromBody] OperatorRequest req)
        {
            try
            {
                string message = string.Empty;

                if (_shareservice.Get_CheckIfExists("op_id", req.op_id, "operator") == true)
                {
                    message = "此帳號已存在，新增失敗";
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
                    bool response = _service.Insert_OperatorSetting(req);
                    message = response ? $"帳號:{req.op_id} 新增成功" : $"帳號:{req.op_id} 新增失敗";
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

        #region 帳號管理-修改功能
        [HttpPut]
        [Route("put")]
        // PUT: api/OperatorSetting/put
        public IActionResult Put([FromBody] OperatorRequest req)
        {
            try
            {
                string message = string.Empty;

                if (_shareservice.Get_CheckIfExists("op_id", req.op_id, "operator") == false)
                {
                    message = "此帳號不存在，修改失敗";
                    /* Log歷史紀錄 */
                    _shareservice.Insert_LogRecord(message);
                    return BadRequest(new ResultViewModel<string>
                    {
                        isSuccess = false,
                        message = "此帳號不存在，修改失敗",
                        Result = null,
                    });
                }
                else
                {
                    bool response = _service.Update_OperatorSetting(req);
                    message = response ? $"帳號:{req.op_id} 修改成功" : $"帳號:{req.op_id} 修改失敗";
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

        #region 個人管理-修改密碼
        [HttpPost]
        [Route("changePassword")]
        // PUT: api/OperatorSetting/changePassword
        public IActionResult ChangePassword([FromBody] OperatorRequest req)
        {
            try
            {
                string message = _service.Update_OperatorPswd(req);
                /* Log歷史紀錄 */
                _shareservice.Insert_LogRecord(message);
                if (message == "密碼更新成功")
                    return Ok(new ResultViewModel<string>
                    {
                        isSuccess = false,
                        message = message,
                        Result = null,
                    });
                if (message == "帳號錯誤，請重新登入確認")
                    return Unauthorized(new ResultViewModel<string>
                    {
                        isSuccess = false,
                        message = message,
                        Result = null,
                    });
                return BadRequest(new ResultViewModel<string>
                {
                    isSuccess = false,
                    message = message,
                    Result = null,
                });
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

        #region 帳號管理-軟刪除功能
        [HttpDelete]
        // Delete: api/OperatorSetting/delete
        [Route("delete")]
        public IActionResult Delete(string op_id)
        {
            try
            {
                string message = string.Empty;

                if (_shareservice.Get_CheckIfExists("op_id", op_id, "operator") == false)
                {
                    message = "此帳號不存在，刪除失敗";
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
                    /* 軟刪除(刪除) */
                    bool response = _service.Delete_OperatorSetting(op_id);
                    message = response ? $"帳號:{op_id} 刪除成功" : $"帳號:{op_id} 刪除失敗";
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

        #region 帳號管理-啟用停用功能
        [HttpPut]
        // Put: api/OperatorSetting/useable
        [Route("useable")]
        public IActionResult Useable([FromBody] OperatorRequest req)
        {
            try
            {
                string message = string.Empty;

                if (_shareservice.Get_CheckIfExists("op_id", req.op_id, "operator") == false)
                {
                    message = "此帳號不存在，停用失敗";
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
                    bool response = _service.Useable_OperatorSetting(req);
                    message = response ? $"帳號:{req.op_id} 停用成功" : $"帳號:{req.op_id} 停用失敗";
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
