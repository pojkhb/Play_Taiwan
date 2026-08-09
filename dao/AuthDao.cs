using System;
using Dapper;
using backend.utils;
using backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class AuthDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        private readonly HttpContext _ipContext;

        public AuthDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 登入
        public LoginResponse Login(LoginRequest req)
        {
            // TODO: 資料表 ep_account 尚未建置，欄位保留如下:
            // ep_id (探員代號 PK), ep_pswd (雜湊密碼), ep_name (帳號名稱), created_at, updated_at
            // string sql = @"SELECT ep_id, ep_name, ep_pswd FROM ep_account WHERE ep_id = @ep_id";
            // var data = _MysqlConnect.QueryFirstOrDefault<LoginResponse>(sql, new { ep_id = req.ep_id });

            return new LoginResponse
            {
                token = "mock-jwt-token",
                ep_id = req.ep_id,
                ep_name = "EP001"
            };
        }
        #endregion

        #region 取得探員帳號資訊
        public LoginResponse GetProfile()
        {
            // TODO: 依 Token 內 ep_id 查詢 ep_account 資料表
            return new LoginResponse
            {
                token = null,
                ep_id = "EP001",
                ep_name = "EP001"
            };
        }
        #endregion

        #region 編輯探員帳號名稱
        public void UpdateProfile(EpAccountUpdateRequest req)
        {
            // TODO: string sql = @"UPDATE ep_account SET ep_name = @ep_name WHERE ep_id = @ep_id";
        }
        #endregion
    }
}