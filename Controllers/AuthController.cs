// 檔案路徑：System\Controllers\AuthController.cs
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
    /// 帳號登入與個人資料管理 API。
    /// 對應頁面：登入、設定－帳號、遊客 Vlog（旅遊回憶影片）生成。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ILogger<AuthController> _logger;
        private readonly AuthService _service;
        private readonly VisitorVlogService _visitorVlogService;

        public AuthController(
            ILogger<AuthController> logger,
            AuthService service,
            VisitorVlogService visitorVlogService)
        {
            _logger = logger;
            _service = service;
            _visitorVlogService = visitorVlogService;
        }

        #region 登入

        /// <summary>
        /// 帳號登入。
        /// </summary>
        /// <remarks>
        /// 使用帳號名稱(或信箱)與密碼進行登入，成功後同時回傳 auth_type，
        /// 供前端判斷登入後要導向探員頁面(1=Tourist)、商家後台(2=Merchant)或管理後台(3=Admin)。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "auth_name": "NoobTW",
        ///   "auth_pswd": "123456"
        /// }
        /// ```
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "登入成功",
        ///   "Result": {
        ///     "token": "eyJhbGciOi...",
        ///     "au_id": 101,
        ///     "auth_name": "amy",
        ///     "auth_type": 1,
        ///     "account_type_name": "Tourist"
        ///   }
        /// }
        /// ```
        /// </remarks>
        /// <param name="req">登入資料，包含帳號名稱與密碼。</param>
        /// <returns>登入結果，成功時回傳帳號資訊、身分類型與 JWT Token。</returns>
        [AllowAnonymous]
        [HttpPost]
        [Route("Login")]
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
        /// 帳號登出。
        /// </summary>
        /// <remarks>
        /// 清除目前登入狀態；未來若有 Refresh Token 或登入工作階段資料表，
        /// 可在此 API 一併撤銷 Token。
        /// </remarks>
        /// <returns>登出執行結果。</returns>
        [Authorize]
        [HttpPost]
        [Route("Logout")]
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

        #region 取得帳號資訊

        /// <summary>
        /// 取得目前登入帳號的資訊。
        /// </summary>
        /// <remarks>
        /// 對應「設定－帳號」頁面，同時可用來判斷要導向哪一種介面：
        /// account_type_name = "Tourist" / "Merchant" / "Admin"。
        /// 需在 Header 帶登入取得的 JWT Token：Authorization: Bearer {token}。
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": {
        ///     "token": null,
        ///     "au_id": 101,
        ///     "auth_name": "amy",
        ///     "auth_type": 1,
        ///     "account_type_name": "Tourist"
        ///   }
        /// }
        /// ```
        /// </remarks>
        /// <returns>目前登入帳號的代號、名稱與身分類型。</returns>
        [Authorize]
        [HttpGet]
        [Route("Profile")]
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

        #region 編輯帳號名稱

        /// <summary>
        /// 更新目前登入帳號的名稱。
        /// </summary>
        /// <remarks>
        /// 對應「設定－帳號」頁面的名稱編輯功能。
        ///
        /// **Request 範例**：
        /// ```json
        /// { "auth_name": "NoobTW" }
        /// ```
        /// </remarks>
        /// <param name="req">欲更新的帳號名稱。</param>
        /// <returns>帳號名稱更新結果。</returns>
        [Authorize]
        [HttpPost]
        [Route("Profile")]
        public IActionResult UpdateProfile([FromBody] AuthAccountUpdateRequest req)
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
        /// 帳號註冊。
        /// </summary>
        /// <remarks>
        /// 註冊新的遊客或商家帳號（支援填入生日、性別），並於背景發送驗證信至指定信箱。
        /// 管理員身分不開放自行註冊。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "Username": "NoobTW",
        ///   "Email": "test@example.com",
        ///   "Password": "password123",
        ///   "Birthday": "2005-01-24",
        ///   "Gender": "Male",
        ///   "AccountType": 1
        /// }
        /// ```
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
        /// 供信箱內的驗證連結點擊使用。成功後會將帳號狀態改為已驗證並清空 Token；
        /// 若連結已超過 24 小時會回傳過期訊息。
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
                _service.VerifyEmail(token);
                return Content("<h1>信箱驗證成功！請返回 APP 或網頁進行登入。</h1>", "text/html", System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        #endregion

        #region 忘記密碼

        /// <summary>
        /// 忘記密碼。
        /// </summary>
        /// <remarks>
        /// 產生重設密碼連結並寄送至信箱，連結 30 分鐘內有效。
        /// 使用者需點擊信中連結，再呼叫 ResetPassword 端點完成密碼重設。
        ///
        /// **Request 範例**：
        /// ```json
        /// { "Email": "test@example.com" }
        /// ```
        /// </remarks>
        [AllowAnonymous]
        [HttpPost]
        [Route("ForgotPassword")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
        {
            try
            {
                await _service.ForgotPasswordAsync(req.Email);
                return Ok(new ResultViewModel<string>
                {
                    isSuccess = true,
                    message = "密碼重設連結已寄送至您的信箱",
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

        #region 重設密碼

        /// <summary>
        /// 依重設連結 Token 完成密碼重設。
        /// </summary>
        /// <remarks>
        /// 對應 ForgotPassword 寄出的信件連結，Token 過期或不存在會回傳失敗。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "Token": "a1b2c3d4...",
        ///   "NewPassword": "newPassword123"
        /// }
        /// ```
        /// </remarks>
        [AllowAnonymous]
        [HttpPost]
        [Route("ResetPassword")]
        public IActionResult ResetPassword([FromBody] ResetPasswordRequest req)
        {
            try
            {
                _service.ResetPassword(req);
                return Ok(new ResultViewModel<string>
                {
                    isSuccess = true,
                    message = "密碼重設成功，請使用新密碼登入",
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

        #region 修改密碼

        /// <summary>
        /// 修改密碼。
        /// </summary>
        /// <remarks>
        /// 需在登入狀態下，並提供舊密碼驗證。
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("ChangePassword")]
        public IActionResult ChangePassword([FromBody] ChangePasswordRequest req)
        {
            try
            {
                _service.ChangePassword(req.OldPassword, req.NewPassword);
                return Ok(new ResultViewModel<string>
                {
                    isSuccess = true,
                    message = "密碼修改成功",
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

       
    }
}