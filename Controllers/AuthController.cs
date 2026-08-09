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
    // 登入 / 帳號管理 (對應畫面: 登入, 設定-探員帳號)
    public class AuthController : ControllerBase
    {
        private readonly ILogger<AuthController> _logger;
        private readonly AuthService _service;

        public AuthController(ILogger<AuthController> logger, AuthService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 登入
        [HttpPost]
        [Route("Login")]
        // POST: api/Auth/Login
        public IActionResult Login([FromBody] LoginRequest req)
        {
            try
            {
                return Ok(new ResultViewModel<LoginResponse>
                {
                    isSuccess = true,
                    message = "登入成功",
                    Result = _service.Login(req),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<LoginResponse>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null,
                });
            }
        }
        #endregion

        #region 登出
        [HttpPost]
        [Route("Logout")]
        // POST: api/Auth/Logout
        public IActionResult Logout()
        {
            try
            {
                _service.Logout();
                return Ok(new ResultViewModel<string> { isSuccess = true, message = "登出成功", Result = null });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<string> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion

        #region 取得探員帳號資訊
        [HttpGet]
        [Route("Profile")]
        // GET: api/Auth/Profile
        public IActionResult Profile()
        {
            try
            {
                return Ok(new ResultViewModel<LoginResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetProfile(),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<LoginResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion

        #region 編輯探員帳號名稱
        [HttpPut]
        [Route("Profile")]
        // PUT: api/Auth/Profile
        public IActionResult UpdateProfile([FromBody] EpAccountUpdateRequest req)
        {
            try
            {
                _service.UpdateProfile(req);
                return Ok(new ResultViewModel<string> { isSuccess = true, message = "更新成功", Result = null });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<string> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion
    }
}