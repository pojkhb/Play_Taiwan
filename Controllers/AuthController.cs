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
    /// 登入與探員帳號管理 API。
    /// 對應頁面：登入、設定－探員帳號、遊客 Vlog（旅遊回憶影片）生成。
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
        /// 探員登入。
        /// </summary>
        /// <remarks>
        /// 使用探員名稱(或信箱)與通行密碼進行登入，成功後同時回傳 account_type，
        /// 供前端判斷登入後要導向探員頁面(1=Tourist)或商家後台(2=Merchant)。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "ep_name": "NoobTW",
        ///   "ep_pswd": "123456"
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
        ///     "ep_id": "EP_19370FAD",
        ///     "ep_name": "amy",
        ///     "account_type": 1,
        ///     "account_type_name": "Tourist"
        ///   }
        /// }
        /// ```
        /// </remarks>
        /// <param name="req">登入資料，包含探員名稱與通行密碼。</param>
        /// <returns>登入結果，成功時回傳探員資訊、帳號類型與 JWT Token。</returns>
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
        /// 探員登出。
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


        #region 取得探員帳號資訊


        /// <summary>
        /// 取得目前登入探員的帳號資訊。
        /// </summary>
        /// <remarks>
        /// 對應「設定－探員帳號」頁面，同時可用來判斷登入頁面要導向哪一種介面：
        /// account_type_name = "Tourist" 表示一般探員，"Merchant" 表示商家帳號。
        /// 需在 Header 帶登入取得的 JWT Token：Authorization: Bearer {token}。
        ///
        /// **Request 範例**：
        /// ```json
        /// GET /api/Auth/Profile
        /// ```
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": {
        ///     "token": null,
        ///     "ep_id": "EP_19370FAD",
        ///     "ep_name": "amy",
        ///     "account_type": 1,
        ///     "account_type_name": "Tourist"
        ///   }
        /// }
        /// ```
        /// </remarks>
        /// <returns>目前登入探員的代號、名稱與帳號類型。</returns>
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


        #region 編輯探員帳號名稱


        /// <summary>
        /// 更新目前登入探員的帳號名稱。
        /// </summary>
        /// <remarks>
        /// 對應「設定－探員帳號」頁面的名稱編輯功能。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "ep_name": "NoobTW"
        /// }
        /// ```
        /// </remarks>
        /// <param name="req">欲更新的探員帳號名稱。</param>
        /// <returns>帳號名稱更新結果。</returns>
        [Authorize]
        [HttpPost]
        [Route("Profile")]
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
        /// 註冊新探員或商家帳號（支援填入生日、性別），並於背景發送驗證信至指定信箱。
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


        #region 忘記密碼


        public class ForgotPasswordRequest { public string Email { get; set; } }


        /// <summary>
        /// 忘記密碼。
        /// </summary>
        /// <remarks>
        /// 自動產生隨機密碼並寄送至信箱。
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
                    message = "新密碼已寄送至您的信箱",
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


        public class ChangePasswordRequest
        {
            public string OldPassword { get; set; }
            public string NewPassword { get; set; }
        }


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


  #region 遊客 Vlog（旅遊回憶影片）

/// <summary>
/// 產生 AI 草稿旁白預覽。
/// </summary>
/// <remarks>
/// 對應「遊戲結束」時呼叫。只需要 story_id，玩家的拜訪地點紀錄、遊玩時長
/// 由後端自動從 ep_task_record 算出（結束時間 - 開始時間），並 JOIN md_story_node / md_place
/// 組出景點清單，交給外部 AI 服務產生行程整理、建議旁白稿、SEO 關鍵字與宣傳文案，
/// 供玩家確認/微調後再送 CreateFinal。
/// **必須帶 Token**（用來查詢是哪位探員的遊玩紀錄）。
///
/// **Request 範例**：
/// ```json
/// {
///   "story_id": "AI_3F2A9C1B"
/// }
/// ```
///
/// **Response 範例**：
/// ```json
/// {
///   "isSuccess": true,
///   "message": "旁白草稿產生成功",
///   "Result": {
///     "status": "success",
///     "draft_preview": {
///       "itinerary": [
///         { "spot_name": "赤崎樓", "location_codename": "神秘的紅磚建築", "visit_time": "2026-08-27 14:00", "db_description": "..." }
///       ],
///       "suggested_script": "今天的冒險從神秘的紅磚建築開始...",
///       "seo_keywords": ["臺南景點", "赤崎樓"],
///       "promo_copy": "跟著探員腳步，重返府城時光"
///     }
///   }
/// }
/// ```
/// </remarks>
/// <param name="req">玩家剛完成的劇本 ID。</param>
/// <returns>AI 產生的旁白草稿與行程整理內容。</returns>
[Authorize]
[HttpPost]
[Route("Vlog/Preview")]
public async Task<IActionResult> VlogPreview([FromBody] VisitorVlogPreviewRequest req)
{
    string epId = User.FindFirst("ep_id")?.Value ?? User.Identity?.Name;
    if (string.IsNullOrEmpty(epId))
    {
        return Unauthorized(new ResultViewModel<string> { isSuccess = false, message = "無法驗證身分，請重新登入" });
    }

    if (string.IsNullOrWhiteSpace(req.story_id))
    {
        return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供 story_id" });
    }

    try
    {
        var result = await _visitorVlogService.GetPreviewAsync(epId, req);
        return Ok(new ResultViewModel<VisitorVlogPreviewApiResponse>
        {
            isSuccess = true,
            message = "旁白草稿產生成功",
            Result = result
        });
    }
    catch (Exception e)
    {
        _logger.LogError(e, "產生遊客 Vlog 旁白草稿時發生錯誤");
        return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
    }
}

/// <summary>
/// 送出正式合成任務。
/// </summary>
/// <remarks>
/// 玩家確認/微調旁白後呼叫。不用上傳照片，後端會依 story_id 自動從 md_task_media
/// 抓取這個劇本所有節點對應的素材、即時打包成 zip 再轉發給外部服務。
/// 送出後只會拿到 task_id，真正的影片要透過 Vlog/Status 輪詢才會知道何時完成。
/// **必須帶 Token**（完成時需綁定操作此功能的帳號寫入 ep_vlog）。
///
/// **Request 範例**：
/// ```json
/// {
///   "final_script": "今天的冒險從神秘的紅磚建築開始...",
///   "story_id": "AI_3F2A9C1B"
/// }
/// ```
///
/// **Response 範例**：
/// ```json
/// {
///   "isSuccess": true,
///   "message": "影片合成任務已建立，請透過 Status API 輪詢進度",
///   "Result": {
///     "status": "processing",
///     "task_id": "TASK_7F2A9C1B",
///     "message": "任務已排入佇列",
///     "check_url": "/api/check_status/TASK_7F2A9C1B"
///   }
/// }
/// ```
/// </remarks>
/// <param name="req">最終旁白文字、關聯的劇本 ID。</param>
/// <returns>合成任務的 task_id，供前端輪詢 Status 使用。</returns>
[Authorize]
[HttpPost]
[Route("Vlog/CreateFinal")]
public async Task<IActionResult> VlogCreateFinal([FromBody] VisitorVlogCreateFinalRequest req)
{
    string epId = User.FindFirst("ep_id")?.Value ?? User.Identity?.Name;
    if (string.IsNullOrEmpty(epId))
    {
        return Unauthorized(new ResultViewModel<string> { isSuccess = false, message = "無法驗證身分，請重新登入" });
    }

    if (string.IsNullOrWhiteSpace(req.final_script) || string.IsNullOrWhiteSpace(req.story_id))
    {
        return BadRequest(new ResultViewModel<string>
        {
            isSuccess = false,
            message = "請提供 final_script 與 story_id"
        });
    }

    try
    {
        var result = await _visitorVlogService.CreateFinalVlogAsync(epId, req);
        return Ok(new ResultViewModel<VlogCreateFinalApiResponse>
        {
            isSuccess = true,
            message = "影片合成任務已建立，請透過 Status API 輪詢進度",
            Result = result
        });
    }
    catch (Exception e)
    {
        _logger.LogError(e, "建立遊客 Vlog 合成任務時發生錯誤");
        return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
    }
}

/// <summary>
/// 查詢合成任務進度（前端輪詢用）。
/// </summary>
/// <remarks>
/// 依 CreateFinal 回傳的 task_id 查詢外部服務目前進度。當外部回應帶有 download_url 時，
/// 視為完成，系統會自動把該筆 Vlog 寫入 ep_vlog。**必須帶 Token**。
///
/// 因為 ep_vlog 需要關聯 story_id，而 task_id 本身不會存進 ep_vlog，
/// 所以呼叫這支時要把當初呼叫 CreateFinal 用的 story_id 一併以 query string 帶回，
/// 系統才知道這個任務完成後要寫進哪一筆劇本的紀錄。
///
/// **Request 範例**：
/// ```
/// GET /api/Auth/Vlog/Status/TASK_7F2A9C1B?story_id=AI_3F2A9C1B
/// ```
///
/// **Response 範例（進行中）**：
/// ```json
/// {
///   "isSuccess": true,
///   "message": "查詢成功",
///   "Result": {
///     "status": "processing",
///     "task_id": "TASK_7F2A9C1B",
///     "filename": null,
///     "download_url": null
///   }
/// }
/// ```
///
/// **Response 範例（已完成）**：
/// ```json
/// {
///   "isSuccess": true,
///   "message": "查詢成功",
///   "Result": {
///     "status": "completed",
///     "task_id": "TASK_7F2A9C1B",
///     "filename": "vlog_TASK_7F2A9C1B.mp4",
///     "download_url": "https://vlog.angelalala.com/download/TASK_7F2A9C1B.mp4"
///   }
/// }
/// ```
/// </remarks>
/// <param name="taskId">CreateFinal 回傳的合成任務 ID。</param>
/// <param name="story_id">當初呼叫 CreateFinal 用的劇本 ID，完成時用來寫入 ep_vlog.story_id。</param>
/// <returns>任務目前狀態；完成時包含可下載的影片網址。</returns>
[Authorize]
[HttpGet]
[Route("Vlog/Status/{taskId}")]
public async Task<IActionResult> VlogStatus(string taskId, [FromQuery] string story_id)
{
    string epId = User.FindFirst("ep_id")?.Value ?? User.Identity?.Name;
    if (string.IsNullOrEmpty(epId))
    {
        return Unauthorized(new ResultViewModel<string> { isSuccess = false, message = "無法驗證身分，請重新登入" });
    }

    try
    {
        var result = await _visitorVlogService.CheckStatusAsync(taskId, epId, story_id);
        return Ok(new ResultViewModel<VlogTaskStatusApiResponse>
        {
            isSuccess = true,
            message = "查詢成功",
            Result = result
        });
    }
    catch (Exception e)
    {
        _logger.LogError(e, $"查詢遊客 Vlog 任務 {taskId} 狀態時發生錯誤");
        return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
    }
}

#endregion
    }
}