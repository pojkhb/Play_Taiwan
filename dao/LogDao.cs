using System;
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
    public class LogDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        public LogDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
        }

        #region log-列表資料
        public List<LogResponse> Get_Log(string? op_id = null, string? message = null)
        {
            string whereSql = string.Empty;

            if(op_id != null) whereSql += " AND op_id = @op_id";
            if(message != null) whereSql += " AND message LIKE CONCAT( @message, '%')";

            string sql = @"Select * FROM log";
            if(whereSql != string.Empty) sql += " WHERE 1=1 " + whereSql;

            var parameters = new {
                op_id = op_id,
                message = message
            };

            List<LogResponse> data_list = _MysqlConnect.Query<LogResponse>(sql, parameters).ToList();
            return data_list.OrderByDescending(x => x.log_id).ToList();
        }
        #endregion  

        #region log-更新
        public void Update_Log(string op_id, string message)
        {
            string sql = @"
                UPDATE log
                SET message = CONCAT('V', @message)
                WHERE op_id = @op_id AND message LIKE CONCAT(@message, '%');
            ";

            var parameters = new {
                op_id = op_id,
                message = message
            };
            _MysqlConnect.Execute(sql, parameters);
        }
        #endregion

        // #region 模組設定-軟刪除功能(停用)
        // public bool Delete_Log(string md_id)
        // {
        //     string sql = @"DELETE FROM Log WHERE md_id = @md_id;";
        //     var parameters = new { md_id = md_id };
        //     int count = (int)_MysqlConnect.Execute(sql, parameters);
        //     if(count == 0) return false;
        //     return true;
        // }
        // #endregion
    }
}