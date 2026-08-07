using System.Collections.Generic;
using backend.dao;
using backend.Models;
using backend.utils;
using Microsoft.AspNetCore.Http;

namespace backend.Services
{
	public class SharedFunctionService
	{
		private readonly SharedFunctionDao _SharedFunctionDao;
		private readonly HttpContext _ipContext;

		public SharedFunctionService(SharedFunctionDao SharedFunctionDao, IHttpContextAccessor httpContextAccessor)
		{
			 _SharedFunctionDao = SharedFunctionDao;
			 _ipContext = httpContextAccessor.HttpContext;
		}

		#region 共用涵式-檢查value是否存在
		public bool Get_CheckIfExists(string colunm, string value, string dbtable)
		{
			return _SharedFunctionDao.Get_CheckIfExists(colunm, value, dbtable);
		}
		#endregion

		#region 共用涵式-檢查角色名稱是否已存在
		public bool Get_CheckRoleNameIfExists(int role_id, string role_name)
		{
			return _SharedFunctionDao.Get_CheckRoleNameIfExists(role_id, role_name);
		}
		#endregion

		#region 共用涵式-Log歷史紀錄
        public void Insert_LogRecord(string message, string? op_id = null)
        {
            tokenEnCode token_encode = new tokenEnCode(_ipContext);
            /* 讀取Token的PayLoad */
            var payLoad = token_encode.GetPayLoad();

			object parameters = new {
				/* 客戶端IP */
				client_ip = ip.getClientAndRemoteIp(_ipContext).Split(",")[0], 
				/* 使用者帳號 */
				op_id = op_id is null ? payLoad["op_id"].ToString() : op_id,
				/* 抓取傳輸類型，ex：GET、POST、DELETE、PUT */
				method = _ipContext.Request.Method, 
				/* 抓取API路徑 */
				path = _ipContext.Request.Path.Value, 
				/* 訊息內容 */
				message = message
			};
			_SharedFunctionDao.Insert_LogRecord(parameters);
        }
        #endregion

		#region 共用涵式-下拉選單-所有角色
		public List<RoleDropdownResponse> Get_DropdownRole()
		{
			return _SharedFunctionDao.Get_DropdownRole();
		}
		#endregion

		#region 共用涵式-下拉選單-所有功能權限
		public List<ModuleDropdownResponse> Get_DropdownModule()
		{
			return _SharedFunctionDao.Get_DropdownModule();
		}
		#endregion
	}
}