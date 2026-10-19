// 檔案路徑：System\dao\Auth\AuthDao.cs
// 對應新資料表 `auth`，取代舊的 ep_account 相關查詢
using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>
        /// 登入時名稱或信箱符合的所有帳號，由 Service 挑密碼對得上的那一個。
        /// 帳號名稱過去沒有擋重複（也可能跟別人的信箱一樣），所以不能只取一筆：
        /// 信箱完全符合的排最前面，其次已驗證、啟用中、較新的帳號。
        /// </summary>
        public List<AuthAccount> GetLoginCandidates(string account)
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
                WHERE auth_name = @account OR auth_email = @account
                ORDER BY (auth_email = @account) DESC, is_email_verified DESC, is_active DESC, au_id DESC;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.Query<AuthAccount>(sql, new { account }).ToList();
            }
        }

        /// <summary>
        /// 帳號名稱已經被其他帳號用掉（不分大小寫）。也不能跟別人的信箱一樣，登入時才不會混淆。
        /// exceptAuId：修改自己的名稱、重新註冊未驗證的帳號時，排除自己。
        /// </summary>
        public bool NameTaken(string name, int? exceptAuId = null)
        {
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.ExecuteScalar<int>(@"
                    SELECT COUNT(1) FROM auth
                    WHERE (auth_name = @name OR auth_email = @name) AND (@exceptAuId IS NULL OR au_id <> @exceptAuId);",
                    new { name, exceptAuId }) > 0;
            }
        }

        /// <summary>Email 已經被其他帳號用掉（auth_email 有唯一索引，不分大小寫）</summary>
        public bool EmailTaken(string email, int? exceptAuId = null)
        {
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM auth WHERE auth_email = @email AND (@exceptAuId IS NULL OR au_id <> @exceptAuId);",
                    new { email, exceptAuId }) > 0;
            }
        }

        /// <summary>商家帳號對應的店家 s_id（store.au_id 有唯一索引，一個帳號只有一間店）；沒有店家資料時回傳 null。</summary>
        public int? GetStoreIdByAuId(int auId)
        {
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.ExecuteScalar<int?>("SELECT s_id FROM store WHERE au_id = @auId LIMIT 1;", new { auId });
            }
        }
        #endregion

        #region 註冊 (新增帳號)
        /// <summary>
        /// 新增遊客帳號（auth_type = 1，信箱驗證前不能登入）。欄位已由 Service 檢查、整理過，密碼是雜湊後的值。
        /// 同時送出兩次註冊、Email 撞到唯一索引時回 409。
        /// </summary>
        public async Task RegisterAsync(RegisterRequest req, string passwordHash, string emailToken)
        {
            // 驗證期限由資料庫用 NOW() 計算，驗證時也是跟 NOW() 比，時區才會一致
            string sql = @"
                INSERT INTO auth
                (auth_name, auth_type, auth_email, auth_pswd, au_email_token, au_email_expires,
                 is_email_verified, au_birthday, au_gender)
                VALUES
                (@auth_name, 1, @auth_email, @auth_pswd, @emailToken, NOW() + INTERVAL 24 HOUR,
                 0, @birthday, @gender);
            ";

            try
            {
                using (var conn = new MySqlConnection(_appSettings.mydb))
                {
                    conn.Open();
                    await conn.ExecuteAsync(sql, new
                    {
                        auth_name = req.Username,
                        auth_email = req.Email,
                        auth_pswd = passwordHash,
                        emailToken,
                        birthday = req.Birthday,
                        gender = req.Gender
                    });
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                throw new ConflictException("此 Email 已被註冊過");
            }
        }

        /// <summary>
        /// 還沒驗證信箱的帳號用同一個 Email 重新註冊：換成這次填的名稱、密碼、生日、性別，發新的驗證碼（舊的連結失效）。
        /// 驗證信沒收到或連結過期時，使用者照驗證失敗頁的提示重新註冊就能拿到新的驗證信。
        /// 只改還沒驗證的帳號，驗證過的不會被覆蓋。
        /// </summary>
        public async Task<bool> RestartPendingRegistrationAsync(int auId, RegisterRequest req, string passwordHash, string emailToken)
        {
            string sql = @"
                UPDATE auth
                SET auth_name = @auth_name, auth_pswd = @auth_pswd,
                    au_email_token = @emailToken, au_email_expires = NOW() + INTERVAL 24 HOUR,
                    au_birthday = @birthday, au_gender = @gender
                WHERE au_id = @auId AND is_email_verified = 0;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return await conn.ExecuteAsync(sql, new
                {
                    auId,
                    auth_name = req.Username,
                    auth_pswd = passwordHash,
                    emailToken,
                    birthday = req.Birthday,
                    gender = req.Gender
                }) > 0;
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
                SELECT au_id, auth_name, auth_type, auth_email, is_active, is_email_verified
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
        /// <summary>期限由資料庫用 NOW() 計算，查詢時也是跟 NOW() 比，時區才會一致</summary>
        public void SetPasswordResetToken(int auId, string resetToken, int validMinutes)
        {
            string sql = @"
                UPDATE auth
                SET pwd_reset_token = @resetToken, pwd_reset_expires = NOW() + INTERVAL @validMinutes MINUTE
                WHERE au_id = @auId;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                conn.Execute(sql, new { auId, resetToken, validMinutes });
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