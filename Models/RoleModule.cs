using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace backend.Models
{
	#region token權限驗證
	public class RoleModuleProcess
	{
		public string md_id_token {get;set;}
		public string md_name {get;set;}
	}
	#endregion

	#region 權限管理request
	public class RoleModuleRequest
	{
		public byte role_id {get;set;} /* 角色代碼 */ 
		public string role_name {get;set;}　/* 角色名稱 */
		public string[] md_id {get;set;} /* 功能模塊代碼 */
		public string[] act_id {get;set;} /* 功能模塊細節代碼 */
		public byte permission {get;set;} /* 權限1:read,2:write,3:all */
		public string create_id {get;set;} /* 建立者帳號 */
		public sbyte revoked {get;set;} /* 是否停用 */
	}
	#endregion

	#region 權限管理View response
	public class RoleModuleResponse
	{
		public byte role_id {get;set;} /* 角色代碼 */
		public string role_name {get;set;} /* 角色名稱 */
		// public bool revoked {get;set;} /* 角色是否停用 */
		// public byte permission {get;set;} /* 功能模塊權限:權限1:read,2:write,3:all */
		public string md_id;
		public string md_name;
		public string act_id;
		public string act_name;
		public List<ModuleDropdownResponse> md_list{get;set;}
		public List<ModuleDetailDropdownResponse> act_list{get;set;}
		public string create_id {get;set;} /* 建立者帳號 */
	}
	#endregion
}