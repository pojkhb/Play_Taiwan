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
    public class ModuleSettingDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        public ModuleSettingDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
        }

        #region 模組設定-檢查模組是否存在
        public bool Get_CheckedModule(string md_id)
        {
            bool result = false;
            string query_sql = @"SELECT md_id FROM module WHERE md_id = @md_id";
            var parameters = new { md_id = md_id };
            var op_id_list = _MysqlConnect.Query<string>(query_sql, parameters);
            if (op_id_list.Count() > 0) result = true;
            return result;
        }
        #endregion

        #region 模組設定-列表資料
        public List<ModuleResponse> Get_ModuleSetting()
        {
            string sql = @"Select * FROM module";
            List<ModuleResponse> data_list = _MysqlConnect.Query<ModuleResponse>(sql).ToList();
            return data_list.OrderBy(x => x.md_id).ToList();
        }
        #endregion  

        #region 模組設定-軟刪除功能(停用)
        public bool Delete_ModuleSetting(string md_id)
        {
            string sql = @"DELETE FROM module WHERE md_id = @md_id;";
            var parameters = new { md_id = md_id };
            int key = (int)_MysqlConnect.Execute(sql, parameters);
            if(key == 0) return false;
            return true;
        }
        #endregion
    }
}