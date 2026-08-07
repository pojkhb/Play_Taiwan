using System.Collections.Generic;
using System.Linq;

using Dapper;

using backend.utils;
using backend.Models;

using Microsoft.Extensions.Options;

using MySql.Data.MySqlClient;

namespace backend.Middleware.jwt
{
    public class JWTDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        public JWTDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
        }

        #region 取得所有非停用的使用者帳號
        public List<JWTModel> GetUserList()
        {
            string sql = string.Empty;
            sql = @$"
                SELECT t1.op_id, t1.op_pswd, t1.op_name, t1.email, t1.op_unit AS unit, t1.dashboard_cfg, t1.role_id, t2.role_name, DATEDIFF(NOW(), t1.pswd_update_datetime) AS pswd_date
                FROM operator t1
                INNER JOIN role t2 ON t1.role_id = t2.role_id
                WHERE t1.revoked = 0 AND t1.useable = 1
            ";
            List<JWTModel> Result = _MysqlConnect.Query<JWTModel>(sql).ToList();
            return Result;
        }
        #endregion

        #region 取得使用者的權限模組
        public List<RoleModuleProcess> GetUserProcess(string op_id)
        {
            string sql = @$"
                SELECT t4.md_id AS md_id_token, t4.md_name 
                FROM operator t1
                INNER JOIN role t2 ON t1.role_id = t2.role_id
                INNER JOIN role_module t3 ON t2.role_id = t3.role_id
                INNER JOIN module t4 ON t3.md_id = t4.md_id
                WHERE t1.op_id = @op_id AND t1.revoked = 0 AND t3.revoked = 0 
            ";

            var parameters = new { op_id = op_id };
            List<RoleModuleProcess> Result = _MysqlConnect.Query<RoleModuleProcess>(sql, parameters).ToList();
            return Result;
        }
        #endregion
    }

    #region 確定角色是否存在
    public class RoleProcessDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        public RoleProcessDao(IOptions<AppSettings> appSettings)
        {
            this._appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
        }
        public bool GetRoleProcessList(string URLMethod,string Path,string role_id)
        {
            bool Result = false;
            string module_route_sql = @$"
                SELECT md_id
                FROM module_route
                WHERE route = @Path
                AND method = @URLMethod;
            ";
            var module_route_parameters = new { Path = Path, URLMethod = URLMethod };
            string md_id = _MysqlConnect.Query<string>(module_route_sql, module_route_parameters).FirstOrDefault();

            string role_sql = @$"
                SELECT rm.md_id
                FROM role r
                LEFT JOIN role_module rm
                ON r.role_id = rm.role_id
                WHERE r.role_id = @role_id AND r.revoked = 0;
            ";
            var role_parameters = new { role_id = role_id };
            List<string> md_id_list = _MysqlConnect.Query<string>(role_sql, role_parameters).ToList();

            if (!string.IsNullOrEmpty(md_id) && md_id_list.Count > 0)
            {
                Result = md_id == "All" ||
                        (md_id.Length == 3 && md_id_list.Contains(md_id)) ||
                        (md_id.Length == 1 && md_id_list.Any(item => item.StartsWith(md_id)));
            }

            return Result;
        }
    }
    #endregion
}