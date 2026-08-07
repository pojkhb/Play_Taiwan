using System.Collections.Generic;

using backend.Models;
using backend.dao;

namespace backend.Services
{
	public class ModuleSettingService
	{
		private readonly ModuleSettingDao _moduleSettingDao;

		public ModuleSettingService(ModuleSettingDao moduleSettingDao)
		{
			 _moduleSettingDao = moduleSettingDao;
		}

		#region 模組設定-列表資料
		public List<ModuleResponse> Get_ModuleSetting()
		{
			return _moduleSettingDao.Get_ModuleSetting();
		}
		#endregion  
		
		#region 模組設定-軟刪除功能(停用)
		public bool Delete_ModuleSetting(string md_id)
		{
			return _moduleSettingDao.Delete_ModuleSetting(md_id);
		}
		#endregion
	}
}