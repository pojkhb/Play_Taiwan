using System;

namespace backend.Models
{
    /// <summary>
    /// 對應 `auth` 資料表的完整欄位。
    /// </summary>
    public class AuthAccount
    {
        public int au_id { get; set; }
        public string auth_name { get; set; }
        /// <summary>1=遊客、2=商家、3=管理員</summary>
        public int auth_type { get; set; }
        public string auth_email { get; set; }
        public string auth_pswd { get; set; }
        public bool is_active { get; set; }
        public bool is_email_verified { get; set; }
        public string au_email_token { get; set; }
        public DateTime? au_email_expires { get; set; }
        public DateTime? last_login_at { get; set; }
        public string pwd_reset_token { get; set; }
        public DateTime? pwd_reset_expires { get; set; }
        public DateTime? au_birthday { get; set; }
        public string au_gender { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
    }

    /// <summary>登入請求。用 auth_name 欄位承載「名稱或信箱」，DAO 端用 OR 查詢兩者。</summary>
    public class LoginRequest
    {
        public string auth_name { get; set; }
        public string auth_pswd { get; set; }
    }

    /// <summary>登入／查詢個人資料共用的回應格式。</summary>
    public class LoginResponse
    {
        public string token { get; set; }
        public int au_id { get; set; }
        public string auth_name { get; set; }
        public int auth_type { get; set; }
        public string account_type_name { get; set; }
    }

    /// <summary>註冊請求。年齡欄位已移除，新表沒有 age，只存生日。</summary>
    public class RegisterRequest
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public DateTime? Birthday { get; set; }
        public string Gender { get; set; }
        /// <summary>前端送來的期望身分；1=遊客、2=商家。管理員(3)不開放自行註冊。</summary>
        public int AccountType { get; set; }

        /// <summary>經過驗證/校正後，實際要寫入 auth_type 的值。</summary>
        public int ResolvedAccountType => AccountType == 2 ? 2 : 1;
    }

    /// <summary>更新個人資料請求（目前只開放改名稱）。</summary>
    public class AuthAccountUpdateRequest
    {
        public string auth_name { get; set; }
    }

    public class ForgotPasswordRequest
    {
        public string Email { get; set; }
    }

    /// <summary>
    /// 忘記密碼重設請求。對應新表的 pwd_reset_token / pwd_reset_expires，
    /// 走「寄送重設連結」流程，不再直接把新密碼寄給使用者。
    /// </summary>
    public class ResetPasswordRequest
    {
        public string Token { get; set; }
        public string NewPassword { get; set; }
    }

    public class ChangePasswordRequest
    {
        public string OldPassword { get; set; }
        public string NewPassword { get; set; }
    }
}