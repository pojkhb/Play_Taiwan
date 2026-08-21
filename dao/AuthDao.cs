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
            // 💡 刪除全域的 _MysqlConnect，改為每次動態建立以避免 Timeout
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
                // 參數對應傳入
                return conn.QueryFirstOrDefault<EpAccount>(sql, new { ep_name = ep_name });
            }
        }
        #endregion

        #region 探員註冊 (新增)
        /// <summary>
        /// 註冊新探員，寫入資料庫並自動產生 uuid 格式的 ep_id
        /// </summary>
        public async Task<bool> RegisterAsync(RegisterRequest req, string emailToken)
        {
            string epId = "EP_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();

            // 寫入 account_type, email_token, is_email_verified
            string sql = @"
                INSERT INTO ep_account (ep_id, ep_name, account_type, email, ep_pswd, email_token, is_email_verified) 
                VALUES (@epId, @ep_name, @account_type, @email, @passwordHash, @emailToken, 0)";

            try
            {
                using (var conn = new MySqlConnection(_appSettings.mydb))
                {
                    // 💡 為了避免 MySql.Data 非同步的死結 Bug，這裡使用同步的 Execute 與 Open
                    conn.Open();

                    int rows = conn.Execute(sql, new
                    {
                        epId = epId,
                        ep_name = req.Username,
                        account_type = req.AccountType == 2 ? 2 : 1, // 如果前端傳2就是商家，否則預設為遊客1
                        email = req.Email,
                        passwordHash = req.Password,
                        emailToken = emailToken
                    });
                    return rows > 0;
                }
            }
            catch (Exception ex)
            {
                // 抓出真正的錯誤
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
                conn.Execute(sql, new
                {
                    ep_id = ep_id,
                    ep_name = req.ep_name
                });
            }
        }
        #endregion
    }
}