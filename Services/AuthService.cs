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
        private readonly EmailService _emailService; // 注入寄信服務
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

            // 💡 擋下尚未驗證信箱的帳號
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
                ep_name = account.ep_name
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

            // 1. 統一密碼加密方式 (配合 Login 的 sha256Hash)
            sha256Hash hashTool = new sha256Hash();
            string passwordHash = hashTool.getSha256(req.Password, _appSettings.hash_key);
            
            // 將加密後的密碼放回 Request，交給 DAO 寫入
            req.Password = passwordHash;

            // 2. 產生一組信箱驗證專用的 Token
            string emailToken = Guid.NewGuid().ToString("N");

            // 3. 呼叫 DAO 寫入資料庫
            bool isSuccess = await _dao.RegisterAsync(req, emailToken);
            if (!isSuccess)
            {
                throw new Exception("註冊失敗，該 Email 可能已被註冊過。");
            }

            // 4. 準備並發送驗證信 (背景處理)
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

        /// <summary>
        /// 產生探員登入 JWT Token。
        /// </summary>
        private string GenerateJwtToken(EpAccount account)
        {
            // 將數字 account_type 轉換為可識別的身分字串
            string roleStr = account.account_type == 2 ? "Merchant" : "Tourist";

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
                new Claim(ClaimTypes.Role, roleStr) // 將身分寫入 JWT
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

        // === 以下為共用的私有方法，用來自動從 JWT 拿玩家代號 ===
        private string GetCurrentEpId()
        {
            // 利用 HttpContext 解析當前攜帶 Token 的玩家 ep_id
            var epIdClaim = _ipContext?.User?.FindFirst("ep_id") ?? _ipContext?.User?.FindFirst(ClaimTypes.NameIdentifier);
            if (epIdClaim == null) throw new Exception("無法取得當前探員身分，請重新登入");
            return epIdClaim.Value;
        }

        #region 取得探員帳號資訊
        public LoginResponse GetProfile()
        {
            string ep_id = GetCurrentEpId();
            return _dao.GetProfile(ep_id);
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