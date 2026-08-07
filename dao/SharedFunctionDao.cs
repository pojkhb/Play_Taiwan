using System.Linq;
using Dapper;

using backend.utils;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using MySql.Data.MySqlClient;
using System.Collections.Generic;
using backend.Models;

namespace backend.dao
{
    public class SharedFunctionDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        private readonly HttpContext _ipContext;
        public SharedFunctionDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 共用涵式-檢查value是否已存在
        public bool Get_CheckIfExists(string colunm, string value, string dbtable)
        {
            bool result = false;
            string sql = $@"SELECT {colunm} FROM {dbtable} WHERE {colunm} = @value";
            var parameters = new { value = value };
            var value_list = _MysqlConnect.Query<string>(sql, parameters);
            if (value_list.Count() > 0) result = true;
            return result;
        }
        #endregion

        #region 共用涵式-檢查角色名稱是否已存在
        public bool Get_CheckRoleNameIfExists(int role_id, string role_name)
        {
            bool result = false;
            string sql = $@"SELECT role_id, role_name FROM role WHERE role_id = @role_id AND role_name = @role_name";
            var parameters = new { role_id = role_id, role_name = role_name };
            var value_list = _MysqlConnect.Query<string>(sql, parameters);
            if (value_list.Count() > 0) result = true;
            return result;
        }
        #endregion

        #region 共用涵式-Log歷史紀錄
        public void Insert_LogRecord(object parameters)
        {
            dynamic param = parameters;

            string path = param.path;
            string message = param.message;
            int maxPathLength = 25;
            int maxMessageLength = 40;

            if (param.path.Length > maxPathLength) path = param.path.Substring(0, maxPathLength) + "...";
            if (param.message.Length > maxMessageLength) message = param.message.Substring(0, maxMessageLength) + "...";

            string sql = @"
                INSERT INTO log (log_time, client_ip, op_id, method, path, message)
                VALUES(NOW(), @client_ip, @op_id, @method, @path, @message)
            ";

            var new_parameters = new {
                client_ip = param.client_ip,
                op_id = param.op_id,
                method = param.method,
                path = path,
                message = message,
            };

            _MysqlConnect.Execute(sql, new_parameters);
        }
        #endregion

        #region 共用涵式-下拉選單-所有角色
        public List<RoleDropdownResponse> Get_DropdownRole()
        {
            string sql = $@"SELECT role_id, role_name, revoked FROM role";
            var data_list = _MysqlConnect.Query<RoleDropdownResponse>(sql);
            return data_list.ToList();
        }
        #endregion

        #region 共用涵式-下拉選單-所有功能權限
        public List<ModuleDropdownResponse> Get_DropdownModule()
        {
            string sql = $@"SELECT md_id, md_name FROM Module";
            var data_list = _MysqlConnect.Query<ModuleDropdownResponse>(sql);
            return data_list.ToList();
        }
        #endregion
    }
}