// 檔案路徑：System\Controllers\Auth\AuthController.cs
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
        /// 帳號登入（遊客、商家、管理員共用）。
        /// </summary>
        /// <remarks>
        /// 使用帳號名稱(或信箱)與密碼進行登入，成功後同時回傳 auth_type，
        /// 供前端判斷登入後要導向探員頁面(1=Tourist)、商家後台(2=Merchant)或管理後台(3=Admin)。
        /// 商家帳號會另外回傳 store_id（店家代號 store.s_id），Token 帶 Role=Merchant 與 s_id，可直接呼叫商家 API；
        /// 商家帳號還沒有店家資料時登入失敗，提示先完成商家註冊。
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
        ///     "account_type_name": "Tourist",
        ///     "store_id": null
        ///   }
        /// }
        /// ```
        /// 商家帳號登入時 auth_type = 2、account_type_name = "Merchant"，store_id 為店家代號（例如 2）。
        /// </remarks>
        /// <param name="req">登入資料，包含帳號名稱與密碼。</param>
        /// <returns>登入結果，成功時回傳帳號資訊、身分類型與 JWT Token。</returns>
        [AllowAnonymous]
        [HttpPost]
        [Route("Login")]
        [ProducesResponseType(typeof(ResultViewModel<LoginResponse>), 200)]
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
        /// <response code="200">登出成功，Result 為 null</response>
        [Authorize]
        [HttpPost]
        [Route("Logout")]
        [ProducesResponseType(typeof(ResultViewModel<string>), 200)]
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
        [ProducesResponseType(typeof(ResultViewModel<LoginResponse>), 200)]
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
        /// <response code="200">更新成功；Result 為 null</response>
        [Authorize]
        [HttpPost]
        [Route("Profile")]
        [ProducesResponseType(typeof(ResultViewModel<string>), 200)]
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
        /// <response code="200">註冊成功，驗證信會寄到信箱；Result 為 null</response>
        [AllowAnonymous]
        [HttpPost]
        [Route("Register")]
        [ProducesResponseType(typeof(ResultViewModel<string>), 200)]
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
        /// 【前端不用接】信箱驗證與啟用帳號。
        /// </summary>
        /// <remarks>
        /// **前端不用接**：這支是寫在驗證信裡的連結，使用者在信箱點擊後由瀏覽器直接開啟，會回傳 HTML 頁面。
        ///
        /// 供信箱內的驗證連結點擊使用。成功後會將帳號狀態改為已驗證並清空 Token；
        /// 若連結已超過 24 小時會回傳過期訊息。
        /// </remarks>
        /// <param name="token">信箱驗證專屬的 Token</param>
        /// <returns>回傳驗證結果畫面的 HTML 內容</returns>
        /// <response code="200">回傳 HTML 驗證結果頁面（不是 JSON）</response>
        [AllowAnonymous]
        [HttpGet]
        [Route("VerifyEmail")]
        public IActionResult VerifyEmail([FromQuery] string token)
        {
            try
            {
                _service.VerifyEmail(token);
                string html = BuildResultPage(
                    success: true,
                    title: "信箱驗證成功！",
                    message: "你的帳號已成功啟用，請返回 APP 或網頁進行登入。");
                return Content(html, "text/html", System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                string html = BuildResultPage(
                    success: false,
                    title: "驗證失敗",
                    message: ex.Message);
                return Content(html, "text/html", System.Text.Encoding.UTF8);
            }
        }

        /// <summary>結果頁（驗證信、重設密碼連結失效共用）：圖示 + 標題 + 說明</summary>
        private static string BuildResultPage(bool success, string title, string message)
        {
            string accentColor = success ? "#28a745" : "#dc3545";
            string icon = success
                ? "<svg width='64' height='64' viewBox='0 0 24 24' fill='none' xmlns='http://www.w3.org/2000/svg'><circle cx='12' cy='12' r='12' fill='#28a745'/><path d='M7 12.5L10.2 15.7L17 8.5' stroke='white' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'/></svg>"
                : "<svg width='64' height='64' viewBox='0 0 24 24' fill='none' xmlns='http://www.w3.org/2000/svg'><circle cx='12' cy='12' r='12' fill='#dc3545'/><path d='M8 8L16 16M16 8L8 16' stroke='white' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'/></svg>";

            return BuildPage(title, $@"
        <div class='icon'>{icon}</div>
        <h1>{title}</h1>
        <p>{message}</p>
        <div class='accent-bar' style='background-color: {accentColor};'></div>");
        }

        /// <summary>信件連結開啟的網頁外框（置中卡片 + 品牌字），content 放卡片內容</summary>
        private static string BuildPage(string title, string content)
        {
            return $@"
<!DOCTYPE html>
<html lang='zh-Hant'>
<head>
<meta charset='UTF-8' />
<meta name='viewport' content='width=device-width, initial-scale=1.0' />
<title>{title} - Play Taiwan</title>
<style>
    body {{
        margin: 0;
        min-height: 100vh;
        display: flex;
        align-items: center;
        justify-content: center;
        background: linear-gradient(135deg, #f0fdf4 0%, #e8f5e9 100%);
        font-family: 'Segoe UI', 'Microsoft JhengHei', Arial, sans-serif;
    }}
    .card {{
        background: #ffffff;
        border-radius: 16px;
        box-shadow: 0 10px 30px rgba(0, 0, 0, 0.08);
        padding: 48px 40px;
        max-width: 420px;
        width: 90%;
        text-align: center;
    }}
    .icon {{
        margin-bottom: 20px;
    }}
    h1 {{
        margin: 0 0 12px;
        font-size: 24px;
        color: #1a1a1a;
    }}
    p {{
        margin: 0;
        font-size: 15px;
        color: #555;
        line-height: 1.6;
    }}
    .brand {{
        margin-top: 32px;
        font-size: 13px;
        color: #aaa;
        letter-spacing: 0.5px;
    }}
    .accent-bar {{
        height: 4px;
        width: 48px;
        margin: 20px auto 0;
        border-radius: 2px;
    }}
    input {{
        display: block;
        box-sizing: border-box;
        width: 100%;
        margin-top: 16px;
        padding: 12px 14px;
        font-size: 15px;
        border: 1px solid #ccc;
        border-radius: 8px;
    }}
    button {{
        width: 100%;
        margin-top: 20px;
        padding: 12px;
        font-size: 15px;
        color: #ffffff;
        background-color: #d9534f;
        border: none;
        border-radius: 8px;
        cursor: pointer;
    }}
    button:disabled {{
        opacity: 0.6;
        cursor: default;
    }}
    .msg {{
        margin-top: 16px;
        min-height: 1.6em;
    }}
</style>
</head>
<body>
    <div class='card'>{content}
        <div class='brand'>PLAY TAIWAN</div>
    </div>
</body>
</html>";
        }

        #endregion

        #region 忘記密碼

        /// <summary>
        /// 忘記密碼。
        /// </summary>
        /// <remarks>
        /// 產生重設密碼連結並寄送至信箱，連結 30 分鐘內有效。
        /// 信中連結會開啟後端提供的重設密碼網頁（GET api/Auth/ResetPassword），使用者在網頁輸入新密碼即可完成重設，
        /// 前端只要接這支跟登入頁的「忘記密碼」。
        ///
        /// **Request 範例**：
        /// ```json
        /// { "Email": "test@example.com" }
        /// ```
        /// </remarks>
        /// <response code="200">重設密碼信已寄出；Result 為 null</response>
        [AllowAnonymous]
        [HttpPost]
        [Route("ForgotPassword")]
        [ProducesResponseType(typeof(ResultViewModel<string>), 200)]
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
        /// 【前端不用接】重設密碼網頁。
        /// </summary>
        /// <remarks>
        /// **前端不用接**：這支是寫在重設密碼信裡的連結，使用者在信箱點擊後由瀏覽器直接開啟，會回傳 HTML 頁面。
        ///
        /// 重設碼有效時顯示輸入新密碼的表單，送出後由網頁呼叫 POST api/Auth/ResetPassword；
        /// 重設碼無效或已過期時顯示失效頁面。只開啟網頁不會用掉重設碼（信箱掃描連結也不會讓它失效）。
        /// </remarks>
        /// <param name="token">重設密碼信裡的 Token</param>
        /// <response code="200">回傳 HTML 頁面（不是 JSON）</response>
        [AllowAnonymous]
        [HttpGet]
        [Route("ResetPassword")]
        public IActionResult ResetPasswordPage([FromQuery] string token)
        {
            string html = _service.IsResetTokenValid(token)
                ? BuildPage("重設密碼", ResetPasswordForm)
                : BuildResultPage(
                    success: false,
                    title: "連結已失效",
                    message: "重設連結無效或已過期，請回到 APP 重新申請忘記密碼。");
            return Content(html, "text/html", System.Text.Encoding.UTF8);
        }

        /// <summary>重設密碼表單；Token 由網頁從網址列讀取，不寫進 HTML</summary>
        private const string ResetPasswordForm = @"
        <h1>重設密碼</h1>
        <p>請輸入新的密碼，連結 30 分鐘內有效。</p>
        <form id='reset-form'>
            <input type='password' id='new-password' placeholder='新密碼' autocomplete='new-password' required />
            <input type='password' id='confirm-password' placeholder='再輸入一次新密碼' autocomplete='new-password' required />
            <button type='submit' id='submit'>確認重設</button>
        </form>
        <p id='msg' class='msg'></p>
        <script>
            document.getElementById('reset-form').addEventListener('submit', async function (e) {
                e.preventDefault();
                var form = e.target;
                var msg = document.getElementById('msg');
                var button = document.getElementById('submit');
                var password = document.getElementById('new-password').value;

                if (password !== document.getElementById('confirm-password').value) {
                    msg.style.color = '#dc3545';
                    msg.textContent = '兩次輸入的密碼不一樣';
                    return;
                }

                button.disabled = true;
                try {
                    var res = await fetch('/api/Auth/ResetPassword', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ Token: new URLSearchParams(location.search).get('token'), NewPassword: password })
                    });
                    var data = await res.json();
                    msg.style.color = data.isSuccess ? '#28a745' : '#dc3545';
                    msg.textContent = data.isSuccess ? '密碼重設成功！請回到 APP 用新密碼登入。' : data.message;
                    if (data.isSuccess) form.style.display = 'none';
                    else button.disabled = false;
                } catch (err) {
                    msg.style.color = '#dc3545';
                    msg.textContent = '連線失敗，請稍後再試';
                    button.disabled = false;
                }
            });
        </script>";

        /// <summary>
        /// 依重設連結 Token 完成密碼重設。
        /// </summary>
        /// <remarks>
        /// 重設密碼網頁（GET api/Auth/ResetPassword）送出時呼叫這支，Token 過期或不存在會回傳失敗。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "Token": "a1b2c3d4...",
        ///   "NewPassword": "newPassword123"
        /// }
        /// ```
        /// </remarks>
        /// <response code="200">密碼重設成功；Result 為 null</response>
        [AllowAnonymous]
        [HttpPost]
        [Route("ResetPassword")]
        [ProducesResponseType(typeof(ResultViewModel<string>), 200)]
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
        /// <response code="200">密碼修改成功；Result 為 null</response>
        [Authorize]
        [HttpPost]
        [Route("ChangePassword")]
        [ProducesResponseType(typeof(ResultViewModel<string>), 200)]
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