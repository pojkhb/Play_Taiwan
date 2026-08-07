using System.ComponentModel.DataAnnotations;
namespace backend.Models
{
	public class CheckAccountRequest
	{
		public string op_id {get;set;} /* 使用者帳號 */
	}
	public class ResetPasswordRequest: CheckAccountRequest
	{
		public string op_pswd {get;set;} /* 使用者密碼 */
	}
}