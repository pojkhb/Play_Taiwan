using System.Collections.Generic;

using backend.Models;
using backend.dao;

namespace backend.Services
{
	public class ForgotPasswordService
	{
		private readonly ForgotPasswordDao _forgotPasswordDao;

		public ForgotPasswordService(ForgotPasswordDao forgotPasswordDao)
		{
			 _forgotPasswordDao = forgotPasswordDao;
		}

		#region 忘記密碼-檢查帳號
		public bool CheckAccount(string op_id)
		{
			return _forgotPasswordDao.CheckAccount(op_id);
		}
		#endregion  

		#region 忘記密碼-重設密碼
		public string ResetPassword(ResetPasswordRequest req)
		{
			return _forgotPasswordDao.ResetPassword(req);
		}
		#endregion 
	}
}