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

        #region 依探員名稱或信箱查詢帳號 (登入時使用)
        public EpAccount GetEpAccount(string ep_name)
        {
            string sql = @"
                SELECT
                    ep_id,
                    ep_name,
                    account_type,
                    email,
                    ep_pswd,
                    is_active,
                    is_email_verified
                FROM ep_account
                WHERE ep_name = @ep_name OR email = @ep_name
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.QueryFirstOrDefault<EpAccount>(sql, new { ep_name = ep_name });
            }
        }
        #endregion

        #region 探員註冊 (新增)
        public async Task<bool> RegisterAsync(RegisterRequest req, string emailToken)
        {
            string epId = "EP_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
            string sql = @"
                INSERT INTO ep_account (ep_id, ep_name, account_type, email, ep_pswd, email_token, is_email_verified) 
                VALUES (@epId, @ep_name, @account_type, @email, @passwordHash, @emailToken, 0)";

            try
            {
                using (var conn = new MySqlConnection(_appSettings.mydb))
                {
                    conn.Open();
                    int rows = conn.Execute(sql, new
                    {
                        epId = epId,
                        ep_name = req.Username,
                        account_type = req.AccountType == 2 ? 2 : 1,
                        email = req.Email,
                        passwordHash = req.Password,
                        emailToken = emailToken
                    });
                    return rows > 0;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"MySQL同步寫入發生例外: {ex.Message}");
            }
        }
        #endregion

        #region 驗證信箱
        public bool VerifyEmail(string token)
        {
            string sql = @"
                UPDATE ep_account 
                SET is_email_verified = 1, email_token = NULL 
                WHERE email_token = @token;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                int rowsAffected = conn.Execute(sql, new { token = token });
                return rowsAffected > 0;
            }
        }
        #endregion

        #region 取得探員帳號資訊
        public LoginResponse GetProfile(string ep_id)
        {
            string sql = @"
                SELECT
                    ep_id,
                    ep_name
                FROM ep_account
                WHERE ep_id = @ep_id
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                var account = conn.QueryFirstOrDefault<EpAccount>(sql, new { ep_id = ep_id });
                if (account == null) return null;
                return new LoginResponse
                {
                    token = null,
                    ep_id = account.ep_id,
                    ep_name = account.ep_name
                };
            }
        }
        #endregion

        #region 編輯探員帳號名稱
        public void UpdateProfile(string ep_id, EpAccountUpdateRequest req)
        {
            string sql = @"
                UPDATE ep_account 
                SET ep_name = @ep_name 
                WHERE ep_id = @ep_id
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                conn.Execute(sql, new { ep_id = ep_id, ep_name = req.ep_name });
            }
        }
        #endregion

        #region 透過 Email 查詢信箱是否存在 (忘記密碼用)
        public string GetEmailByAddress(string email)
        {
            string sql = "SELECT email FROM ep_account WHERE email = @email LIMIT 1";
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.QueryFirstOrDefault<string>(sql, new { email = email });
            }
        }
        #endregion

        #region 更新密碼
        public void UpdatePassword(string email, string newPasswordHash)
        {
            string sql = "UPDATE ep_account SET ep_pswd = @newPasswordHash WHERE email = @email";
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                conn.Execute(sql, new { email = email, newPasswordHash = newPasswordHash });
            }
        }
        #endregion
        #region 依 ID 查詢完整帳號資訊 (修改密碼用)
        public EpAccount GetAccountById(string ep_id)
        {
            string sql = @"
                SELECT
                    ep_id,
                    ep_name,
                    account_type,
                    email,
                    ep_pswd,
                    is_active,
                    is_email_verified
                FROM ep_account
                WHERE ep_id = @ep_id
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.QueryFirstOrDefault<EpAccount>(sql, new { ep_id = ep_id });
            }
        }
        #endregion
    }
}