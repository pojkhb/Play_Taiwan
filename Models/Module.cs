using System;
using System.ComponentModel.DataAnnotations;
namespace backend.Models
{
	public class ModuleResponse
	{
		public string md_id  { get; set; } /* 模組代碼 */
		public string md_name  { get; set; } /* 模組名稱 */
	}
}