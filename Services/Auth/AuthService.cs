// 檔案路徑：System\Services\Auth\AuthService.cs
// 對應新資料表 `auth`，取代舊的 ep_account 相關邏輯
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.util;
using backend.utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace backend.Services
{
    public class AuthService
    {
        private readonly AuthDao _dao;
        private readonly HttpContext _ipContext;
        private readonly AppSettings _appSettings;
        private readonly EmailService _emailService;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            AuthDao dao,
            IHttpContextAccessor httpContextAccessor,
            IOptions<AppSettings> appSettings,
            EmailService emailService,
            ILogger<AuthService> logger)
        {
            _dao = dao;
            _ipContext = httpContextAccessor.HttpContext;
            _appSettings = appSettings.Value;
            _emailService = emailService;
            _logger = logger;
        }

        #region 登入
        /// <summary>
        /// 登入（遊客／商家／管理員共用同一個入口）。登入失敗一律丟 BadRequestException（回 400，訊息直接給使用者看）。
        /// 商家帳號必須已建立店家資料，Token 會一併帶 s_id Claim 與 Role=Merchant，可直接呼叫商家 API。
        /// </summary>
        public LoginResponse Login(LoginRequest req)
        {
            AuthAccount account = ValidateCredentials(req);

            int? sId = null;
            if (account.auth_type == MerchantAuthType)
            {
                sId = _dao.GetStoreIdByAuId(account.au_id)
                    ?? throw new BadRequestException("找不到此帳號的商家資料，請先完成商家註冊");
            }

            return IssueLogin(account, sId);
        }

        private const int TouristAuthType = 1;
        private const int MerchantAuthType = 2;

        /// <summary>
        /// 檢查密碼與帳號狀態，通過後回傳帳號資料。
        /// 名稱或信箱符合的帳號可能不只一個（過去沒擋重複的帳號名稱），用密碼對得上的那一個；
        /// 密碼對了才告訴對方帳號停用或還沒驗證，不讓沒有密碼的人查出帳號狀態。
        /// </summary>
        private AuthAccount ValidateCredentials(LoginRequest req)
        {
            string account = req?.auth_name?.Trim();
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(req.auth_pswd))
            {
                throw new BadRequestException("請輸入帳號名稱(或信箱)與密碼");
            }

            string passwordHash = HashPassword(req.auth_pswd);
            AuthAccount matched = _dao.GetLoginCandidates(account).FirstOrDefault(a => a.auth_pswd == passwordHash)
                ?? throw new BadRequestException("帳號或密碼錯誤");

            if (!matched.is_active)
            {
                throw new BadRequestException("此帳號已停用");
            }

            if (!matched.is_email_verified)
            {
                throw new BadRequestException("請先到信箱點驗證信裡的連結，驗證後才能登入；沒收到信的話，用同一個 Email 再註冊一次就會重寄");
            }

            return matched;
        }

        private string HashPassword(string password) => new sha256Hash().getSha256(password, _appSettings.hash_key);

        /// <summary>更新最後登入時間並簽發 Token。</summary>
        private LoginResponse IssueLogin(AuthAccount account, int? sId)
        {
            _dao.UpdateLastLogin(account.au_id);

            return new LoginResponse
            {
                token = GenerateJwtToken(account, sId),
                au_id = account.au_id,
                auth_name = account.auth_name,
                auth_type = account.auth_type,
                account_type_name = GetAccountTypeName(account.auth_type),
                store_id = sId
            };
        }
        #endregion

        #region 註冊
        /// <summary>
        /// 遊客註冊，寄出驗證信。欄位不合規則回 400（BadRequestException），名稱或 Email 被用掉回 409（ConflictException）。
        /// 商家請用 POST /api/Merchant/Register（這裡建的商家帳號沒有店家資料，永遠登不進去），管理員不開放自行註冊。
        /// 同一個 Email 已經註冊但還沒驗證信箱時，視為重新註冊：換成這次的資料並重寄驗證信（舊的驗證連結失效）。
        /// </summary>
        public async Task RegisterAsync(RegisterRequest req)
        {
            if (req == null) throw new BadRequestException("請填寫註冊資料");

            if (req.AccountType == MerchantAuthType)
                throw new BadRequestException("商家帳號請改用商家註冊（POST /api/Merchant/Register），才會一併建立店家資料");
            if (req.AccountType != 0 && req.AccountType != TouristAuthType)
                throw new BadRequestException("只開放註冊遊客帳號（AccountType = 1）");

            req.Username = AccountRules.Name(req.Username);
            req.Email = AccountRules.Email(req.Email);
            AccountRules.Password(req.Password);
            req.Birthday = AccountRules.Birthday(req.Birthday);
            req.Gender = AccountRules.Gender(req.Gender);

            AuthAccount existing = _dao.GetAccountByEmail(req.Email);
            if (existing != null && (existing.is_email_verified || existing.auth_type != TouristAuthType))
                throw new ConflictException("此 Email 已被註冊過，請直接登入；忘記密碼可以用「忘記密碼」重設");

            if (_dao.NameTaken(req.Username, existing?.au_id))
                throw new ConflictException("此帳號名稱已被使用，請換一個");

            string passwordHash = HashPassword(req.Password);
            string emailToken = Guid.NewGuid().ToString("N");

            if (existing == null)
                await _dao.RegisterAsync(req, passwordHash, emailToken);
            else if (!await _dao.RestartPendingRegistrationAsync(existing.au_id, req, passwordHash, emailToken))
                throw new ConflictException("此 Email 已被註冊過，請直接登入；忘記密碼可以用「忘記密碼」重設");   // 剛好在這時候完成驗證

            // 用使用者連進來的網址（本機或對外 domain）組連結
            string verifyUrl = PublicUrl.Of(_ipContext.Request, $"/api/Auth/VerifyEmail?token={emailToken}");
            string subject = "歡迎加入 Play Taiwan！你的探險即將開始";
            string htmlContent = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                    <h2>你好，{WebUtility.HtmlEncode(req.Username)}！帳號註冊成功！</h2>
                    <p>感謝你註冊 Play Taiwan，你的專屬解謎旅程已經為你準備好。</p>
                    <p>為了確保帳號安全，請點擊下方按鈕驗證您的信箱（連結 24 小時內有效）：</p>
                    <p><a href='{verifyUrl}' style='display:inline-block; padding:10px 20px; background-color:#28a745; color:white; text-decoration:none; border-radius:5px;'>點我驗證並啟用帳號</a></p>
                    <p style='font-size: 13px; color: #888; word-break: break-all;'>按鈕沒有反應的話，請複製下面的網址到瀏覽器開啟：<br/><a href='{verifyUrl}' style='color: #28a745;'>{verifyUrl}</a></p>
                    <br/>
                    <p>祝 探索愉快，</p>
                    <p><strong>Play Taiwan 營運團隊</strong></p>
                </div>";
            string textContent =
$@"你好，{req.Username}！帳號註冊成功！

感謝你註冊 Play Taiwan，你的專屬解謎旅程已經為你準備好。
為了確保帳號安全，請開啟下面的網址驗證您的信箱（連結 24 小時內有效）：

{verifyUrl}

祝 探索愉快，
Play Taiwan 營運團隊";

            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendEmailAsync(req.Email, req.Username, subject, htmlContent, textContent);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[{req.Email}] 歡迎信寄送失敗: {ex.Message}");
                }
            });
        }
        #endregion

        #region 信箱驗證
        public void VerifyEmail(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) throw new Exception("缺少驗證碼");

            int rows = _dao.VerifyEmail(token);
            if (rows > 0) return;

            if (_dao.IsEmailTokenExpired(token))
            {
                throw new Exception("驗證連結已過期，請重新註冊或聯繫客服重寄驗證信");
            }

            throw new Exception("驗證失敗：無效的連結或信箱已驗證過");
        }
        #endregion

        #region 忘記密碼 (寄送重設連結)
        public async Task ForgotPasswordAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) throw new Exception("請提供 Email");

            AuthAccount account = _dao.GetAccountByEmail(email);
            if (account == null) throw new Exception("找不到此 Email 註冊的帳號");

            string resetToken = Guid.NewGuid().ToString("N");

            _dao.SetPasswordResetToken(account.au_id, resetToken, validMinutes: 30);

            // 連到後端提供的重設密碼網頁（App 沒有網頁可以接這個連結）
            string resetUrl = PublicUrl.Of(_ipContext.Request, $"/api/Auth/ResetPassword?token={resetToken}");
            string subject = "Play Taiwan - 密碼重設通知";
            string htmlContent = $@"
                <div style='font-family: Arial; padding: 20px;'>
                    <h2>密碼重設請求</h2>
                    <p>你收到這封信是因為你申請了重置密碼，連結 30 分鐘內有效。</p>
                    <p><a href='{resetUrl}' style='display:inline-block; padding:10px 20px; background-color:#d9534f; color:white; text-decoration:none; border-radius:5px;'>點我重設密碼</a></p>
                    <p style='font-size: 13px; color: #888; word-break: break-all;'>按鈕沒有反應的話，請複製下面的網址到瀏覽器開啟：<br/><a href='{resetUrl}' style='color: #d9534f;'>{resetUrl}</a></p>
                    <p>若非本人操作，請忽略此信件，你的密碼不會被更動。</p>
                </div>";
            string textContent =
$@"密碼重設請求

你收到這封信是因為你申請了重置密碼，請開啟下面的網址重設密碼（30 分鐘內有效）：

{resetUrl}

若非本人操作，請忽略此信件，你的密碼不會被更動。";

            _ = Task.Run(async () =>
            {
                try { await _emailService.SendEmailAsync(email, account.auth_name, subject, htmlContent, textContent); }
                catch (Exception ex) { _logger.LogError($"密碼重設信寄送失敗: {ex.Message}"); }
            });
        }
        #endregion

        #region 依重設 Token 完成密碼重設
        /// <summary>重設碼存在且未過期（重設密碼網頁決定要顯示表單還是失效頁）</summary>
        public bool IsResetTokenValid(string token)
        {
            return !string.IsNullOrWhiteSpace(token) && _dao.GetAccountByResetToken(token) != null;
        }

        public void ResetPassword(ResetPasswordRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Token) || string.IsNullOrWhiteSpace(req.NewPassword))
            {
                throw new Exception("請提供 Token 與新密碼");
            }

            AccountRules.Password(req.NewPassword);

            AuthAccount account = _dao.GetAccountByResetToken(req.Token);
            if (account == null)
            {
                throw new Exception("重設連結無效或已過期，請重新申請忘記密碼");
            }

            _dao.ResetPasswordByToken(req.Token, HashPassword(req.NewPassword));
        }
        #endregion

        #region 修改密碼 (登入狀態下)
        public void ChangePassword(string oldPassword, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(oldPassword) || string.IsNullOrWhiteSpace(newPassword))
                throw new Exception("密碼不能為空");

            AccountRules.Password(newPassword);

            int auId = GetCurrentAuId();
            AuthAccount account = _dao.GetAccountById(auId);
            if (account == null) throw new Exception("找不到當前登入的帳號資料，請重新登入");

            if (HashPassword(oldPassword) != account.auth_pswd) throw new Exception("舊密碼輸入錯誤");

            _dao.UpdatePassword(auId, HashPassword(newPassword));
        }
        #endregion

        /// <summary>
        /// 將資料庫存的數字身分，轉換成前端好判斷的文字。
        /// 1=遊客(Tourist)、2=商家(Merchant)、其他(含3)=管理員(Admin)。
        /// Login 產生 JWT 跟 GetProfile 查詢都共用這一份規則，避免兩處各寫一套邏輯。
        /// </summary>
        private string GetAccountTypeName(int authType)
        {
            return authType switch
            {
                2 => "Merchant",
                3 => "Admin",
                _ => "Tourist"
            };
        }

        /// <summary>
        /// 產生登入 JWT Token。商家帳號有店家資料時另外帶 s_id Claim（見 CurrentUser.GetSId）。
        /// </summary>
        private string GenerateJwtToken(AuthAccount account, int? sId)
        {
            string roleStr = GetAccountTypeName(account.auth_type);

            SymmetricSecurityKey securityKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_appSettings.jwt_secret)
            );

            SigningCredentials credentials = new SigningCredentials(
                securityKey,
                SecurityAlgorithms.HmacSha256
            );

            var claims = new List<Claim>
            {
                new Claim("au_id", account.au_id.ToString()),
                new Claim("auth_name", account.auth_name),
                new Claim(ClaimTypes.NameIdentifier, account.au_id.ToString()),
                new Claim(ClaimTypes.Name, account.auth_name),
                new Claim(ClaimTypes.Role, roleStr)
            };

            if (sId.HasValue)
            {
                claims.Add(new Claim("s_id", sId.Value.ToString()));
            }

            JwtSecurityToken token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddDays(_appSettings.expires),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        #region 登出
        public void Logout()
        {
            // TODO: 若有 refresh token / session 表，於此處撤銷
        }
        #endregion

        private int GetCurrentAuId()
        {
            var auIdClaim = _ipContext?.User?.FindFirst("au_id") ?? _ipContext?.User?.FindFirst(ClaimTypes.NameIdentifier);
            if (auIdClaim == null || !int.TryParse(auIdClaim.Value, out int auId))
            {
                throw new Exception("無法取得當前登入身分，請重新登入");
            }
            return auId;
        }

        #region 取得帳號資訊
        public LoginResponse GetProfile()
        {
            int auId = GetCurrentAuId();
            LoginResponse profile = _dao.GetProfile(auId);
            if (profile == null) throw new Exception("找不到此帳號");
            profile.account_type_name = GetAccountTypeName(profile.auth_type);
            profile.store_id = profile.auth_type == MerchantAuthType ? _dao.GetStoreIdByAuId(auId) : null;
            return profile;
        }
        #endregion

        #region 編輯帳號名稱
        /// <summary>改帳號名稱：規則跟註冊一樣，不能跟其他帳號的名稱或信箱重複（回 409）</summary>
        public void UpdateProfile(AuthAccountUpdateRequest req)
        {
            int auId = GetCurrentAuId();
            string name = AccountRules.Name(req?.auth_name);

            if (_dao.NameTaken(name, auId)) throw new ConflictException("此帳號名稱已被使用，請換一個");

            _dao.UpdateProfile(auId, new AuthAccountUpdateRequest { auth_name = name });
        }
        #endregion
    }
}