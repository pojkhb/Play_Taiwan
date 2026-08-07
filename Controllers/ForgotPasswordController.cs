using System;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using backend.Services;
using backend.ViewModels;
using backend.Models;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]

    public class ForgotPasswordController : ControllerBase
    {
        private readonly ILogger<ForgotPasswordController> _logger;
        private readonly ForgotPasswordService _service;
        private readonly SharedFunctionService _shareservice;

        public ForgotPasswordController(ILogger<ForgotPasswordController> logger, ForgotPasswordService service, SharedFunctionService shareservie)
        {
            _logger = logger;
            _service = service;
            _shareservice = shareservie;
        }

        #region 忘記密碼-檢查帳號
        // POST 忘記密碼-檢查帳號 api/ForgotPassword/checkAccount
        [HttpPost]
        [Route("checkAccount")]
        public IActionResult CheckAccount([FromBody] CheckAccountRequest req)
        {
            try
            {
                bool hsaAccount = _service.CheckAccount(req.op_id);
                if (hsaAccount)
                {
                    _shareservice.Insert_LogRecord("忘記密碼功能:查詢帳號成功", req.op_id);
                    return Ok(new ResultViewModel<string>
                    {
                        isSuccess = true,
                        message = "檢查帳號成功",
                        Status = "Success",
                        Result = "可重設密碼"
                    });
                }
                else
                {
                    _shareservice.Insert_LogRecord("忘記密碼功能:查詢帳號失敗", req.op_id);
                    return BadRequest(new ResultViewModel<string>
                    {
                        isSuccess = false,
                        message = "查無帳號",
                        Status = "Error",
                    });
                }
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<string>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Status = "Error"
                });
            }
        }
        #endregion

        #region 忘記密碼-重設密碼
        // POST 忘記密碼-重設密碼 api/ForgotPassword/resetPassword
        [HttpPost]
        [Route("resetPassword")]
        public IActionResult ResetPassword([FromBody] ResetPasswordRequest req)
        {
            try
            {
                string Result = _service.ResetPassword(req);
                if (Result == "密碼更新成功")
                {
                    _shareservice.Insert_LogRecord("忘記密碼功能:重設密碼成功", req.op_id);
                    return Ok(new ResultViewModel<string>
                    {
                        isSuccess = true,
                        message = "重設密碼成功",
                        Status = "Success",
                        Result = Result
                    });
                }
                else
                {
                    _shareservice.Insert_LogRecord("忘記密碼功能:重設密碼失敗", req.op_id);
                    return BadRequest(new ResultViewModel<string>
                    {
                        isSuccess = false,
                        message = Result,
                        Status = "Error",
                    });
                }
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<string>
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