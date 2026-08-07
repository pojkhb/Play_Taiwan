using System.Collections.Generic;

using backend.Models;
using backend.dao;

namespace backend.Services
{
	public class LogService
	{
		private readonly LogDao _LogDao;

		public LogService(LogDao LogDao)
		{
			 _LogDao = LogDao;
		}

		#region 取得條件篩選功能模塊主檔列表
		public List<LogResponse> Get_Log()
		{
			return _LogDao.Get_Log();
		}
		#endregion
	}
}