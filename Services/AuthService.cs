using System;
using backend.dao;
using backend.Models;
using backend.utils;
using Microsoft.AspNetCore.Http;

namespace backend.Services
{
    public class AuthService
    {
        private readonly AuthDao _dao;
        private readonly HttpContext _ipContext;

        public AuthService(AuthDao dao, IHttpContextAccessor httpContextAccessor)
        {
            _dao = dao;
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 登入-探員代號+通行密碼
        public LoginResponse Login(LoginRequest req)
        {
            // TODO: 資料庫建置完成後，改為呼叫 _dao.Get_EpAccount 驗證帳密並產生 JWT
            return _dao.Login(req);
        }
        #endregion

        #region 登出
        public void Logout()
        {
            // TODO: 若有 refresh token / session 表，於此處撤銷
        }
        #endregion

        #region 取得探員帳號資訊
        public LoginResponse GetProfile()
        {
            return _dao.GetProfile();
        }
        #endregion

        #region 編輯探員帳號名稱
        public void UpdateProfile(EpAccountUpdateRequest req)
        {
            _dao.UpdateProfile(req);
        }
        #endregion
    }
}