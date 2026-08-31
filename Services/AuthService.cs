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
            if (string.IsNullOrWhiteSpace(req.ep_name) ||
                string.IsNullOrWhiteSpace(req.ep_pswd))
            {
                throw new Exception("請輸入探員名稱(或信箱)與通行密碼");
            }

            EpAccount account = _dao.GetEpAccount(req.ep_name);

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

            string inputPasswordHash = hashTool.getSha256(
                req.ep_pswd,
                _appSettings.hash_key
            );

            if (inputPasswordHash != account.ep_pswd)
            {
                throw new Exception("帳號或密碼錯誤");
            }

            string token = GenerateJwtToken(account);

            return new LoginResponse
            {
                token = token,
                ep_id = account.ep_id,
                ep_name = account.ep_name,
                account_type = account.account_type,
                account_type_name = GetAccountTypeName(account.account_type)
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
                    <h2>你好，{req.Username}！探員登錄成功！</h2>
                    <p>感謝你註冊 Play Taiwan，你的專屬解謎旅程已經為你準備好。</p>
                    <p>為了確保帳號安全，請點擊下方按鈕驗證您的信箱：</p>
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
        public bool VerifyEmail(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) throw new Exception("缺少驗證碼");
            return _dao.VerifyEmail(token);
        }
        #endregion

        #region 忘記密碼 (產生隨機密碼並寄信)
        public async Task ForgotPasswordAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) throw new Exception("請提供 Email");

            var userEmail = _dao.GetEmailByAddress(email);
            if (string.IsNullOrEmpty(userEmail)) throw new Exception("找不到此 Email 註冊的帳號");

            string tempPassword = Guid.NewGuid().ToString("N").Substring(0, 8);
            sha256Hash hashTool = new sha256Hash();
            string newPasswordHash = hashTool.getSha256(tempPassword, _appSettings.hash_key);

            _dao.UpdatePassword(email, newPasswordHash);

            string subject = "Play Taiwan - 密碼重置通知";
            string htmlContent = $@"
                <div style='font-family: Arial; padding: 20px;'>
                    <h2>你的密碼已重置</h2>
                    <p>你收到這封信是因為你申請了重置密碼。</p>
                    <p>你的新登入密碼為：<strong style='font-size:18px; color:#d9534f;'>{tempPassword}</strong></p>
                    <p>請使用此密碼登入後，盡快前往「設定」修改為你熟悉的密碼。</p>
                </div>";

            _ = Task.Run(async () =>
            {
                try { await _emailService.SendEmailAsync(email, "探員", subject, htmlContent); }
                catch (Exception ex) { _logger.LogError($"密碼信寄送失敗: {ex.Message}"); }
            });
        }
        #endregion

        #region 修改密碼 (登入狀態下)
        public void ChangePassword(string oldPassword, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(oldPassword) || string.IsNullOrWhiteSpace(newPassword))
                throw new Exception("密碼不能為空");

            string ep_id = GetCurrentEpId();

            EpAccount account = _dao.GetAccountById(ep_id);

            if (account == null) throw new Exception("找不到當前登入的帳號資料，請重新登入");

            sha256Hash hashTool = new sha256Hash();
            string oldHash = hashTool.getSha256(oldPassword, _appSettings.hash_key);

            if (oldHash != account.ep_pswd) throw new Exception("舊密碼輸入錯誤");

            string newHash = hashTool.getSha256(newPassword, _appSettings.hash_key);
            _dao.UpdatePassword(account.email, newHash);
        }
        #endregion

        /// <summary>
        /// 將資料庫存的數字帳號類型，轉換成前端好判斷的文字。
        /// 1(或其他非2的值) = Tourist(一般探員)，2 = Merchant(商家)。
        /// Login 產生 JWT 跟 GetProfile 查詢都共用這一份規則，避免兩處各寫一套邏輯。
        /// </summary>
        private string GetAccountTypeName(int accountType)
        {
            return accountType == 2 ? "Merchant" : "Tourist";
        }

        /// <summary>
        /// 產生探員登入 JWT Token。
        /// </summary>
        private string GenerateJwtToken(EpAccount account)
        {
            string roleStr = GetAccountTypeName(account.account_type);

            SymmetricSecurityKey securityKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_appSettings.jwt_secret)
            );

            SigningCredentials credentials = new SigningCredentials(
                securityKey,
                SecurityAlgorithms.HmacSha256
            );

            Claim[] claims = new[]
            {
                new Claim("ep_id", account.ep_id),
                new Claim("ep_name", account.ep_name),
                new Claim(ClaimTypes.NameIdentifier, account.ep_id),
                new Claim(ClaimTypes.Name, account.ep_name),
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

        private string GetCurrentEpId()
        {
            var epIdClaim = _ipContext?.User?.FindFirst("ep_id") ?? _ipContext?.User?.FindFirst(ClaimTypes.NameIdentifier);
            if (epIdClaim == null) throw new Exception("無法取得當前探員身分，請重新登入");
            return epIdClaim.Value;
        }

        #region 取得探員帳號資訊
        public LoginResponse GetProfile()
        {
            string ep_id = GetCurrentEpId();
            LoginResponse profile = _dao.GetProfile(ep_id);
            if (profile == null) throw new Exception("找不到此探員帳號");
            profile.account_type_name = GetAccountTypeName(profile.account_type);
            return profile;
        }
        #endregion

        #region 編輯探員帳號名稱
        public void UpdateProfile(EpAccountUpdateRequest req)
        {
            string ep_id = GetCurrentEpId();
            _dao.UpdateProfile(ep_id, req);
        }
        #endregion
    }
}