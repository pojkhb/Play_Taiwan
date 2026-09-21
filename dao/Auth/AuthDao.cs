// 檔案路徑：System\dao\Auth\AuthDao.cs
// 對應新資料表 `auth`，取代舊的 ep_account 相關查詢
using System;
using System.Threading.Tasks;
using Dapper;
using backend.utils;
using backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class AuthDao
    {
        private readonly AppSettings _appSettings;
        private readonly HttpContext _ipContext;

        public AuthDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 依名稱或信箱查詢帳號 (登入時使用)
        public AuthAccount GetAuthAccount(string authName)
        {
            string sql = @"
                SELECT
                    au_id,
                    auth_name,
                    auth_type,
                    auth_email,
                    auth_pswd,
                    is_active,
                    is_email_verified
                FROM auth
                WHERE auth_name = @authName OR auth_email = @authName
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.QueryFirstOrDefault<AuthAccount>(sql, new { authName });
            }
        }
        #endregion

        #region 註冊 (新增帳號)
        public async Task<bool> RegisterAsync(RegisterRequest req, string emailToken)
        {
            string sql = @"
                INSERT INTO auth
                (auth_name, auth_type, auth_email, auth_pswd, au_email_token, au_email_expires,
                 is_email_verified, au_birthday, au_gender)
                VALUES
                (@auth_name, @auth_type, @auth_email, @auth_pswd, @emailToken, @emailExpires,
                 0, @birthday, @gender);
            ";

            try
            {
                using (var conn = new MySqlConnection(_appSettings.mydb))
                {
                    conn.Open();
                    int rows = await conn.ExecuteAsync(sql, new
                    {
                        auth_name = req.Username,
                        auth_type = req.ResolvedAccountType,
                        auth_email = req.Email,
                        auth_pswd = req.Password,
                        emailToken,
                        emailExpires = DateTime.UtcNow.AddHours(24),
                        birthday = req.Birthday,
                        gender = req.Gender
                    });
                    return rows > 0;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"MySQL寫入發生例外: {ex.Message}");
            }
        }
        #endregion

        #region 信箱驗證
        /// <summary>
        /// 驗證信箱 Token；同時檢查是否過期，過期或不存在都會回傳 0 筆影響。
        /// </summary>
        public int VerifyEmail(string token)
        {
            string sql = @"
                UPDATE auth
                SET is_email_verified = 1, au_email_token = NULL, au_email_expires = NULL
                WHERE au_email_token = @token
                  AND au_email_expires > NOW();
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.Execute(sql, new { token });
            }
        }

        /// <summary>
        /// 專門用來判斷「Token 是否存在但已過期」，方便回傳更明確的錯誤訊息。
        /// </summary>
        public bool IsEmailTokenExpired(string token)
        {
            string sql = @"
                SELECT COUNT(1) FROM auth
                WHERE au_email_token = @token AND au_email_expires <= NOW();
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.ExecuteScalar<int>(sql, new { token }) > 0;
            }
        }
        #endregion

        #region 取得帳號資訊 (個人資料頁)
        public LoginResponse GetProfile(int auId)
        {
            string sql = @"
                SELECT au_id, auth_name, auth_type
                FROM auth
                WHERE au_id = @auId
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.QueryFirstOrDefault<LoginResponse>(sql, new { auId });
            }
        }
        #endregion

        #region 更新帳號名稱
        public void UpdateProfile(int auId, AuthAccountUpdateRequest req)
        {
            string sql = "UPDATE auth SET auth_name = @auth_name WHERE au_id = @auId";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                conn.Execute(sql, new { auId, auth_name = req.auth_name });
            }
        }
        #endregion

        #region 依信箱查詢帳號是否存在 (忘記密碼用)
        public AuthAccount GetAccountByEmail(string email)
        {
            string sql = @"
                SELECT au_id, auth_name, auth_email
                FROM auth
                WHERE auth_email = @email
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.QueryFirstOrDefault<AuthAccount>(sql, new { email });
            }
        }
        #endregion

        #region 寫入忘記密碼重設 Token
        public void SetPasswordResetToken(int auId, string resetToken, DateTime expires)
        {
            string sql = @"
                UPDATE auth
                SET pwd_reset_token = @resetToken, pwd_reset_expires = @expires
                WHERE au_id = @auId;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                conn.Execute(sql, new { auId, resetToken, expires });
            }
        }
        #endregion

        #region 依重設 Token 查詢帳號 (確認未過期)
        public AuthAccount GetAccountByResetToken(string token)
        {
            string sql = @"
                SELECT au_id, auth_email
                FROM auth
                WHERE pwd_reset_token = @token
                  AND pwd_reset_expires > NOW()
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.QueryFirstOrDefault<AuthAccount>(sql, new { token });
            }
        }
        #endregion

        #region 依重設 Token 更新密碼並清空 Token
        public void ResetPasswordByToken(string token, string newPasswordHash)
        {
            string sql = @"
                UPDATE auth
                SET auth_pswd = @newPasswordHash, pwd_reset_token = NULL, pwd_reset_expires = NULL
                WHERE pwd_reset_token = @token;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                conn.Execute(sql, new { token, newPasswordHash });
            }
        }
        #endregion

        #region 更新密碼 (依 au_id，登入狀態下修改密碼用)
        public void UpdatePassword(int auId, string newPasswordHash)
        {
            string sql = "UPDATE auth SET auth_pswd = @newPasswordHash WHERE au_id = @auId";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                conn.Execute(sql, new { auId, newPasswordHash });
            }
        }
        #endregion

        #region 依 ID 查詢完整帳號資訊 (修改密碼用)
        public AuthAccount GetAccountById(int auId)
        {
            string sql = @"
                SELECT
                    au_id,
                    auth_name,
                    auth_type,
                    auth_email,
                    auth_pswd,
                    is_active,
                    is_email_verified
                FROM auth
                WHERE au_id = @auId
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.QueryFirstOrDefault<AuthAccount>(sql, new { auId });
            }
        }
        #endregion

        #region 更新最後登入時間
        public void UpdateLastLogin(int auId)
        {
            string sql = "UPDATE auth SET last_login_at = NOW() WHERE au_id = @auId";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                conn.Execute(sql, new { auId });
            }
        }
        #endregion
    }
}