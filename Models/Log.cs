using System;
using System.ComponentModel.DataAnnotations;
namespace backend.Models
{
	public class LogResponse
	{
		public int log_id {get;set;} /* 流水號 */
		public DateTime log_time {get;set;} /* 建立時間 */
		public string client_ip {get;set;} /* 客戶端IP */
		public string op_id {get;set;} /* 使用者帳號 */
		public string method {get;set;} /* 傳輸類型 */
		public string path {get;set;} /* API路徑 */
		public string message {get;set;} /* 訊息內容 */
	}
}