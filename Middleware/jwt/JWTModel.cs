using System.ComponentModel.DataAnnotations;

namespace backend.Middleware.jwt
{
    public class JWTRequest
    {
        [Required]
        public string op_id{get;set;} //帳號

        [Required]
        public string op_pswd{get;set;} //密碼
    }
    
    public class JWTResponse
    { 
        public JWTResponse(string op_id, string op_name, string email, string unit, string dashboard_cfg, int role_id, string role_name, int pswd_date, string[] md_id_arr ,string token)
        {
            this.Account = op_id;
            this.Name = op_name;
            this.Email = email;
            this.Unit = unit;
            this.Dashboard = dashboard_cfg;
            this.RoleID = role_id;
            this.RoleName = role_name;
            this.PswdDate = pswd_date;
            this.Process = md_id_arr;
            this.Token = token;
        }
        public string Account {get;set;} //使用者帳號
        public string Name {get;set;} //使用者名稱
        public string Email {get;set;} //電子信箱
        public string Unit {get;set;} //單位
        public string Dashboard {get;set;} //儀表板區塊設定代碼
        public int RoleID {get;set;} //角色流水號
        public string RoleName {get;set;} //角色名稱
        public int PswdDate {get;set;} //密碼上次變更時間
        public string[] Process {get;set;} //權限代碼
        public string Token {get;set;} //整包token
    }
    
    public class JWTModel
    {
        public string op_id {get;set;} //帳號
        public string op_name {get;set;} //使用者名稱
        public string op_pswd {get;set;} //密碼
        public string email {get;set;} //電子信箱
        public string unit {get;set;} //單位
        public string dashboard_cfg {get;set;} //儀表板權模塊代碼
        public byte role_id {get;set;} //角色代碼
        public string role_name {get;set;} //角色名稱
        public int pswd_date {get;set;} //密碼上次變更時間
        public string[] md_id_arr {get;set;} //模組代碼
    }
}