using System.Collections.Generic;
using System.Data;
using System.Linq;

using backend.Extensions;
using backend.Models;
using backend.utils;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using MySql.Data.MySqlClient;
using Dapper;

namespace backend.dao
{
    public class FrameFunctionDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        public FrameFunctionDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
        }

        #region 前端動態樣式
        public List<FrontendStyleResponse> Get_FrontendStyle()
        {
            string sql = @"SELECT * FROM Frontend_style";
            List<FrontendStyleResponse> data_list = _MysqlConnect.Query<FrontendStyleResponse>(sql).ToList();
            return data_list.ToList();
        }
        #endregion

        #region 跑馬燈
        public List<MarqueeResponse> Get_Marquee()
        {
            string sql = @"
                SELECT (acc_year - 1911) AS year, acc_month AS month FROM acc_history_rec ORDER BY acc_year DESC, acc_month DESC LIMIT 1
            ";
            List<MarqueeResponse> data_list = _MysqlConnect.Query<MarqueeResponse>(sql).ToList();
            return data_list.ToList();
        }
        #endregion  
    }
}