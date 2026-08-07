using System.ComponentModel.DataAnnotations;
namespace backend.Models
{
	public class OperatorRequest
	{
		public string op_id {get;set;} /* 使用者帳號 */
		public string op_pswd {get;set;} /* 使用者密碼 */
		public string op_name {get;set;} /* 使用者名稱 */
		public string email {get;set;} /* 電子信箱 */
		public int unit {get;set;} /* 單位 */
		public string dashboard_cfg {get;set;} /* 儀表板區塊 */
		public byte role_id {get;set;} /* 角色代碼 */
		public string create_id {get;set;} /* 建立者名稱 */
		public bool useable {get;set;} /* 是否啟用 */
		public string? old_op_pswd {get;set;} /* 舊密碼 */
	}
	public class OperatorResponse
	{
		public string op_id {get;set;} /* 使用者帳號 */
		public string op_name {get;set;} /* 使用者密碼 */
		public string role_id {get;set;} /* 角色代碼 */
		public string role_name {get;set;} /* 角色名稱 */
		public string email {get;set;} /* 使用者電子信箱 */
		public string unit {get;set;} /* 單位代碼 */
		public string unit_name {get;set;} /* 單位名稱 */
		public string dashboard_cfg {get;set;} /* 儀表板權模塊代碼 */
		public int revoked {get;set;} /* 使用者是否軟刪除 */
		public bool useable {get;set;} /* 是否啟用 */
	}
	public class OperatorExportRes
    {
         public string OpId { get; set; } /* 帳號 */
		 public string OpName { get; set; } /* 使用者名稱 */
		 public string Email { get; set; } /* 信箱 */
		public string UnitName { get; set; } /* 所屬單位 */
		public string RoleName { get; set; } /* 角色 */
		public string Status { get; set; } /* 狀態 */
    }
}