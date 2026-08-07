using System.Collections.Generic;
using System.Linq;

using backend.Models;
using backend.dao;

namespace backend.Services
{
	public class RoleModuleSettingService
	{
		private readonly RoleModuleSettingDao _roleModuleSettingDao;

		public RoleModuleSettingService(RoleModuleSettingDao roleModuleSettingDao)
		{
			 _roleModuleSettingDao = roleModuleSettingDao;
		}
		
		// #region 
		// public bool Get_CheckedRole(string value, string type)
		// {
		// 	return _roleModuleSettingDao.Get_CheckedRole(value, type);
		// }
		// #endregion

		#region 角色權限設定-列表功能
		public List<RoleModuleResponse> Get_RoleModuleSetting()
		{
			List<RoleModuleResponse> dbresult = _roleModuleSettingDao.Get_RoleModuleSetting();
			var group = dbresult.GroupBy(x => new { x.role_id, x.role_name, x.create_id });
			
			List<RoleModuleResponse> result = new List<RoleModuleResponse>();
			foreach(var item in group)
			{
				List<ModuleDropdownResponse> md_list = new List<ModuleDropdownResponse>();
				List<ModuleDetailDropdownResponse> act_list = new List<ModuleDetailDropdownResponse>();
					
				var data_list = item.GroupBy(x => new { x.md_id, x.md_name });
				foreach (var data in data_list)
				{
					ModuleDropdownResponse md = new ModuleDropdownResponse();
					md.md_id = data.Key.md_id;
					md.md_name = data.Key.md_name;
					md_list.Add(md);

					foreach (var act_data in data)
					{
						if (act_data.act_id != null)
						{
							ModuleDetailDropdownResponse act = new ModuleDetailDropdownResponse();
							act.md_id = data.Key.md_id;
							act.md_name = data.Key.md_name;
							act.act_id = act_data.act_id;
							act.act_name = act_data.act_name;
							act_list.Add(act);
						}
					}
				}
				result.Add(new RoleModuleResponse {
					role_id = item.Key.role_id,
					role_name = item.Key.role_name,
					md_list = md_list,
					act_list = act_list,
					create_id = item.Key.create_id
				});
			}
			
			return result;
		}
		#endregion  

		#region 角色權限設定-新增功能
		public bool Insert_RoleModuleSetting(RoleModuleRequest req)
		{
			return _roleModuleSettingDao.Insert_RoleModuleSetting(req);;
		}
		#endregion

		#region 角色權限設定-修改功能
		public bool Update_RoleModuleSetting(RoleModuleRequest req)
		{
			return _roleModuleSettingDao.Update_RoleModuleSetting(req);
		}
		#endregion

		#region 角色權限設定-刪除功能(停用)
		public bool Delete_RoleModuleSetting(byte role_id)
		{
			return _roleModuleSettingDao.Delete_RoleModuleSetting(role_id);
		}
		#endregion
	}
}