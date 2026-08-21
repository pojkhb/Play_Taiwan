using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;
using System.Threading.Tasks;

namespace backend.Controllers
{
    /// <summary>
    /// 登入與探員帳號管理 API。
    /// 對應頁面：登入、設定－探員帳號。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
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

        /// <summary>
        /// 探員登入。
        /// </summary>
        /// <remarks>
        /// 使用探員名稱(或信箱)與通行密碼進行登入。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Auth/Login
        ///     {
        ///       "ep_name": "NoobTW",
        ///       "ep_pswd": "123456"
        ///     }
        /// </remarks>
        /// <param name="req">登入資料，包含探員名稱與通行密碼。</param>
        /// <returns>登入結果，成功時回傳探員資訊與 JWT Token。</returns>
        // API：探員登入（Login）－驗證探員代號與密碼，成功後回傳 JWT Token
        [AllowAnonymous]
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

        /// <summary>
        /// 探員登出。
        /// </summary>
        /// <remarks>
        /// 清除目前登入狀態；未來若有 Refresh Token 或登入工作階段資料表，
        /// 可在此 API 一併撤銷 Token。
        /// </remarks>
        /// <returns>登出執行結果。</returns>
        // API：探員登出（Logout）－清除目前登入狀態
        [Authorize]
        [HttpPost]
        [Route("Logout")]
        // POST: api/Auth/Logout
        public IActionResult Logout()
        {
            try
            {
                _service.Logout();

                return Ok(new ResultViewModel<string>
                {
                    isSuccess = true,
                    message = "登出成功",
                    Result = null
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<string>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null
                });
            }
        }

        #endregion

        #region 取得探員帳號資訊

        /// <summary>
        /// 取得目前登入探員的帳號資訊。
        /// </summary>
        /// <remarks>
        /// 對應「設定－探員帳號」頁面。
        /// </remarks>
        /// <returns>目前登入探員的代號與帳號名稱。</returns>
        // API：查詢探員帳號資訊（Profile）－回傳目前登入探員的代號與名稱
        [Authorize]
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
                return NotFound(new ResultViewModel<LoginResponse>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null
                });
            }
        }

        #endregion

        #region 編輯探員帳號名稱

        /// <summary>
        /// 更新目前登入探員的帳號名稱。
        /// </summary>
        /// <remarks>
        /// 對應「設定－探員帳號」頁面的名稱編輯功能。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Auth/Profile
        ///     {
        ///       "ep_name": "NoobTW"
        ///     }
        /// </remarks>
        /// <param name="req">欲更新的探員帳號名稱。</param>
        /// <returns>帳號名稱更新結果。</returns>
        // API：更新探員帳號名稱（UpdateProfile）－修改目前登入探員的顯示名稱
        [Authorize]
        [HttpPost]
        [Route("Profile")]
        // POST: api/Auth/Profile
        public IActionResult UpdateProfile([FromBody] EpAccountUpdateRequest req)
        {
            try
            {
                _service.UpdateProfile(req);

                return Ok(new ResultViewModel<string>
                {
                    isSuccess = true,
                    message = "更新成功",
                    Result = null
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<string>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null
                });
            }
        }

        #endregion

        #region 註冊

        /// <summary>
        /// 探員註冊。
        /// </summary>
        /// <remarks>
        /// 註冊新探員或商家帳號，並於背景發送驗證信至指定信箱。
        /// </remarks>
        [AllowAnonymous]
        [HttpPost]
        [Route("Register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest req)
        {
            try
            {
                await _service.RegisterAsync(req);
                
                return Ok(new ResultViewModel<string>
                {
                    isSuccess = true,
                    message = "註冊成功！驗證信已發送至您的信箱，請先完成驗證再登入。",
                    Result = null
                });
            }
            catch (Exception e)
            {
                return BadRequest(new ResultViewModel<string>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null
                });
            }
        }

        #endregion

        #region 信箱驗證

        /// <summary>
        /// 信箱驗證與啟用帳號。
        /// </summary>
        /// <remarks>
        /// 供信箱內的驗證連結點擊使用。成功後會將帳號狀態改為已驗證並清空 Token。
        /// </remarks>
        /// <param name="token">信箱驗證專屬的 Token</param>
        /// <returns>回傳驗證結果畫面的 HTML 內容</returns>
        [AllowAnonymous]
        [HttpGet]
        [Route("VerifyEmail")]
        public IActionResult VerifyEmail([FromQuery] string token)
        {
            try
            {
                bool result = _service.VerifyEmail(token);
                if (result)
                {
                    return Content("<h1>信箱驗證成功！請返回 APP 或網頁進行登入。</h1>", "text/html", System.Text.Encoding.UTF8);
                }
                return BadRequest("驗證失敗：無效的連結或信箱已驗證過。");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        #endregion
    }
}