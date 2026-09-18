// 檔案路徑：backend\Services\AuthService.cs
// 對應新資料表 `auth`，取代舊的 ep_account 相關邏輯
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
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
        public LoginResponse Login(LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.auth_name) || string.IsNullOrWhiteSpace(req.auth_pswd))
            {
                throw new Exception("請輸入帳號名稱(或信箱)與密碼");
            }

            AuthAccount account = _dao.GetAuthAccount(req.auth_name);

            if (account == null)
            {
                throw new Exception("帳號或密碼錯誤");
            }

            if (!account.is_active)
            {
                throw new Exception("此帳號已停用");
            }

            if (!account.is_email_verified)
            {
                throw new Exception("請先至信箱收取驗證信，驗證後才能登入");
            }

            sha256Hash hashTool = new sha256Hash();
            string inputPasswordHash = hashTool.getSha256(req.auth_pswd, _appSettings.hash_key);

            if (inputPasswordHash != account.auth_pswd)
            {
                throw new Exception("帳號或密碼錯誤");
            }

            _dao.UpdateLastLogin(account.au_id);

            string token = GenerateJwtToken(account);

            return new LoginResponse
            {
                token = token,
                au_id = account.au_id,
                auth_name = account.auth_name,
                auth_type = account.auth_type,
                account_type_name = GetAccountTypeName(account.auth_type)
            };
        }
        #endregion

        #region 註冊
        public async Task RegisterAsync(RegisterRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            {
                throw new Exception("資料不完整，請提供 Email 與密碼");
            }

            sha256Hash hashTool = new sha256Hash();
            string passwordHash = hashTool.getSha256(req.Password, _appSettings.hash_key);
            req.Password = passwordHash;

            string emailToken = Guid.NewGuid().ToString("N");

            bool isSuccess = await _dao.RegisterAsync(req, emailToken);
            if (!isSuccess)
            {
                throw new Exception("註冊失敗，該 Email 可能已被註冊過。");
            }

            string verifyUrl = $"http://localhost:5501/api/Auth/VerifyEmail?token={emailToken}";
            string subject = "歡迎加入 Play Taiwan！你的探險即將開始";
            string htmlContent = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                    <h2>你好，{req.Username}！帳號註冊成功！</h2>
                    <p>感謝你註冊 Play Taiwan，你的專屬解謎旅程已經為你準備好。</p>
                    <p>為了確保帳號安全，請點擊下方按鈕驗證您的信箱（連結 24 小時內有效）：</p>
                    <p><a href='{verifyUrl}' style='display:inline-block; padding:10px 20px; background-color:#28a745; color:white; text-decoration:none; border-radius:5px;'>點我驗證並啟用帳號</a></p>
                    <br/>
                    <p>祝 探索愉快，</p>
                    <p><strong>Play Taiwan 營運團隊</strong></p>
                </div>";

            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendEmailAsync(req.Email, req.Username, subject, htmlContent);
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
            DateTime expires = DateTime.UtcNow.AddMinutes(30);

            _dao.SetPasswordResetToken(account.au_id, resetToken, expires);

            string resetUrl = $"http://localhost:5501/reset-password?token={resetToken}";
            string subject = "Play Taiwan - 密碼重設通知";
            string htmlContent = $@"
                <div style='font-family: Arial; padding: 20px;'>
                    <h2>密碼重設請求</h2>
                    <p>你收到這封信是因為你申請了重置密碼，連結 30 分鐘內有效。</p>
                    <p><a href='{resetUrl}' style='display:inline-block; padding:10px 20px; background-color:#d9534f; color:white; text-decoration:none; border-radius:5px;'>點我重設密碼</a></p>
                    <p>若非本人操作，請忽略此信件，你的密碼不會被更動。</p>
                </div>";

            _ = Task.Run(async () =>
            {
                try { await _emailService.SendEmailAsync(email, account.auth_name, subject, htmlContent); }
                catch (Exception ex) { _logger.LogError($"密碼重設信寄送失敗: {ex.Message}"); }
            });
        }
        #endregion

        #region 依重設 Token 完成密碼重設
        public void ResetPassword(ResetPasswordRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Token) || string.IsNullOrWhiteSpace(req.NewPassword))
            {
                throw new Exception("請提供 Token 與新密碼");
            }

            AuthAccount account = _dao.GetAccountByResetToken(req.Token);
            if (account == null)
            {
                throw new Exception("重設連結無效或已過期，請重新申請忘記密碼");
            }

            sha256Hash hashTool = new sha256Hash();
            string newHash = hashTool.getSha256(req.NewPassword, _appSettings.hash_key);

            _dao.ResetPasswordByToken(req.Token, newHash);
        }
        #endregion

        #region 修改密碼 (登入狀態下)
        public void ChangePassword(string oldPassword, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(oldPassword) || string.IsNullOrWhiteSpace(newPassword))
                throw new Exception("密碼不能為空");

            int auId = GetCurrentAuId();
            AuthAccount account = _dao.GetAccountById(auId);
            if (account == null) throw new Exception("找不到當前登入的帳號資料，請重新登入");

            sha256Hash hashTool = new sha256Hash();
            string oldHash = hashTool.getSha256(oldPassword, _appSettings.hash_key);

            if (oldHash != account.auth_pswd) throw new Exception("舊密碼輸入錯誤");

            string newHash = hashTool.getSha256(newPassword, _appSettings.hash_key);
            _dao.UpdatePassword(auId, newHash);
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
        /// 產生登入 JWT Token。
        /// </summary>
        private string GenerateJwtToken(AuthAccount account)
        {
            string roleStr = GetAccountTypeName(account.auth_type);

            SymmetricSecurityKey securityKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_appSettings.jwt_secret)
            );

            SigningCredentials credentials = new SigningCredentials(
                securityKey,
                SecurityAlgorithms.HmacSha256
            );

            Claim[] claims = new[]
            {
                new Claim("au_id", account.au_id.ToString()),
                new Claim("auth_name", account.auth_name),
                new Claim(ClaimTypes.NameIdentifier, account.au_id.ToString()),
                new Claim(ClaimTypes.Name, account.auth_name),
                new Claim(ClaimTypes.Role, roleStr)
            };

            JwtSecurityToken token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_appSettings.expires),
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
            return profile;
        }
        #endregion

        #region 編輯帳號名稱
        public void UpdateProfile(AuthAccountUpdateRequest req)
        {
            int auId = GetCurrentAuId();
            _dao.UpdateProfile(auId, req);
        }
        #endregion
    }
}