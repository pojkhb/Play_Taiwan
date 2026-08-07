using System.Collections.Generic;

using backend.Models;
using backend.dao;

namespace backend.Services
{
	public class PermissionSettingService
	{
		private readonly PermissionSettingDao _PermissionSettingDao;

		public PermissionSettingService(PermissionSettingDao PermissionSettingDao)
		{
			 _PermissionSettingDao = PermissionSettingDao;
		}
		// #region 模組設定-檢查模組是否存在
		// public bool Get_CheckedPermission(string md_id)
		// {
		// 	return _PermissionSettingDao.Get_CheckedPermission(md_id);
		// }
		// #endregion

		#region 取得條件篩選功能模塊主檔列表
		public List<PermissionResponse> Get_PermissionSetting()
		{
			return _PermissionSettingDao.Get_PermissionSetting();
		}
		#endregion  
		
		#region 篩除功能模塊主檔資料
		public bool Delete_PermissionSetting(string md_id)
		{
			return _PermissionSettingDao.Delete_PermissionSetting(md_id);
		}
		#endregion
	}
}