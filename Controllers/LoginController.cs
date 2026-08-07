using System;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using backend.Services;
using backend.ViewModels;
using backend.Middleware.jwt;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]

    public class LoginController : ControllerBase
    {
        private readonly ILogger<LoginController> _logger;
        private readonly JWTUserService _service;
        private readonly SharedFunctionService _shareservice;

        public LoginController(ILogger<LoginController> logger, JWTUserService service, SharedFunctionService shareservie)
        {
            _logger = logger;
            _service = service;
            _shareservice = shareservie;
        }

        #region 登入
        // POST 登入帳號 api/Login/
        [HttpPost]
        public IActionResult login([FromBody] JWTRequest req)
        {
            try
            {
                object Result = _service.Authenticate(req);
                if (Result is string errorMessage)
                {
                    _shareservice.Insert_LogRecord(("登入失敗:" + errorMessage), req.op_id);
                    return BadRequest(new ResultViewModel<JWTResponse>
                    {
                        isSuccess = false,
                        message = errorMessage,
                        Status = "Error",
                    });
                }
                else if (Result is JWTResponse jwtResponse)
                {
                    _shareservice.Insert_LogRecord("登入成功", req.op_id);
                    return Ok(new ResultViewModel<JWTResponse>
                    {
                        isSuccess = true,
                        message = "登入成功",
                        Status = "Success",
                        Result = jwtResponse // 回傳 JWTResponse
                    });
                }
                else
                {
                    _shareservice.Insert_LogRecord("登入失敗:未知錯誤", req.op_id);
                    return BadRequest(new ResultViewModel<JWTResponse>
                    {
                        isSuccess = false,
                        message = "未知錯誤",
                        Status = "Error",
                    });
                }
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<JWTResponse>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Status = "Error"
                });
            }
        }
        #endregion
    }
}