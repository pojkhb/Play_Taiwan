using System.Linq;

using Dapper;

using backend.Models;
using backend.utils;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using MySql.Data.MySqlClient;
using System.Collections.Generic;

namespace backend.dao
{
    public class RoleModuleSettingDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        public RoleModuleSettingDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
        }

        #region 角色權限設定-列表資料
        public List<RoleModuleResponse> Get_RoleModuleSetting()
        {
            string sql = @"
            SELECT t1.role_id, t1.role_name, t2.md_id, t3.md_name, t4.act_id, t5.act_name, t2.create_id, t2.create_datetime FROM role t1
            INNER JOIN role_module t2
            ON t1.role_id = t2.role_id
            INNER JOIN module t3
            ON t2.md_id = t3.md_id
			LEFT JOIN role_module_detail t4
			ON t4.role_id = t1.role_id AND t4.md_id = t2.md_id
			LEFT JOIN module_detail t5
			ON t5.act_id = t4.act_id
            WHERE t1.revoked = 0";
            return _MysqlConnect.Query<RoleModuleResponse>(sql).ToList();
        }
        #endregion

        // #region 角色權限設定-檢查角色是否已存在
        // public bool Get_CheckedRole(string value, string type)
        // {
        //     string dynamic_field = type == "id" ? "role_id" : "role_name";

        //     bool result = false;
        //     string query_sql = $@"SELECT {dynamic_field} FROM role WHERE {dynamic_field} = @value";
        //     var parameters = new { value = value };
        //     var opIdList = _MysqlConnect.Query<string>(query_sql, parameters);

        //     if (opIdList.Count() > 0) result = true;
        //     return result;
        // }
        // #endregion

        #region 角色權限設定-新增功能
        public bool Insert_RoleModuleSetting(RoleModuleRequest req)
        {
            string sql = string.Empty;
            List<object> parameters_list = new List<object>();
            /* 新增角色 */
            sql = @"INSERT INTO role(role_name,create_id,create_datetime)
            VALUES(@role_name,@create_id,NOW());
            SELECT LAST_INSERT_ID() AS role_id;";
            /* role_id 回傳新增role時的流水號 */
            byte role_id = _MysqlConnect.Query<byte>(sql, req).FirstOrDefault();

            /* 新增角色權限模組 */
            int count = 0;
            foreach(var md_id in req.md_id){
                sql = @"INSERT INTO role_module(role_id,md_id,permission,create_id,create_datetime)
                VALUES(@role_id,@md_id,@permission,@create_id,NOW())";

                parameters_list.Add(new {
                    role_id = role_id,
                    md_id = md_id,
                    permission = req.permission,
                    create_id = req.create_id
                });
            }
            count = (int)_MysqlConnect.Execute(sql, parameters_list);

            if( count == 0 ) return false;

            /* 新增角色權限細節模組 */
            if(req.act_id.Count() > 0){
                List<object> parameters_detail_list = new List<object>();
                foreach(var act_id in req.act_id){
                    sql = @"INSERT INTO role_module_detail(role_id,md_id,act_id)
                    VALUES(@role_id,@md_id,@act_id)";

                    parameters_detail_list.Add(new {
                        role_id = role_id,
                        md_id = act_id.Split("-")[0],
                        act_id = act_id.Split("-")[1],
                    });
                }
                count = (int)_MysqlConnect.Execute(sql, parameters_detail_list);
                if( count == 0 ) return false;
            }

            return true;
        }
        #endregion

        #region 角色權限設定-修改功能
        public bool Update_RoleModuleSetting(RoleModuleRequest req)
        {
            string sql_step1 = string.Empty;
            /* 修改角色名稱 */
            sql_step1 = @"UPDATE role SET role_name = @role_name WHERE role_id = @role_id;";
            /* 刪除既有角色權限 */
            sql_step1 += @"DELETE FROM role_module WHERE role_id = @role_id;";
            /* 刪除既有角色細節權限 */
            sql_step1 += @"DELETE FROM role_module_detail WHERE role_id = @role_id;";

            var parameters = new {role_id = req.role_id, role_name = req.role_name};
            _MysqlConnect.Execute(sql_step1, parameters);

            /* 重新新增角色權限 */
            string sql_step2 = string.Empty;
            List<object> parameters_list = new List<object>();
            foreach(var md_id in req.md_id){
                sql_step2 = @"INSERT INTO role_module(role_id,md_id,permission,create_id)
                VALUES(@role_id,@md_id,@permission,@create_id);";

                parameters_list.Add(new {
                    role_id = req.role_id,
                    md_id = md_id,
                    permission = req.permission,
                    create_id = req.create_id
                });
            }
            int count = (int)_MysqlConnect.Execute(sql_step2, parameters_list);

            if( count == 0 ) return false;

            /* 新增角色權限細節模組 */
            if(req.act_id.Count() > 0){
                string sql_step3 = string.Empty;
                List<object> parameters_detail_list = new List<object>();
                foreach(var act_id in req.act_id){
                    sql_step3 = @"INSERT INTO role_module_detail(role_id,md_id,act_id)
                    VALUES(@role_id,@md_id,@act_id)";

                    parameters_detail_list.Add(new {
                        role_id = req.role_id,
                        md_id = act_id.Split("-")[0],
                        act_id = act_id.Split("-")[1],
                    });
                }
                count = (int)_MysqlConnect.Execute(sql_step3, parameters_detail_list);
                if( count == 0 ) return false;
            }

            return true;
        }
        #endregion

        #region 角色權限設定-軟刪除功能(停用)
        public bool Delete_RoleModuleSetting(byte role_id)
        {
            string sql = @"UPDATE role SET revoked = 1 WHERE role_id = @role_id;";
            sql += @"UPDATE role_module SET revoked = 1 WHERE role_id = @role_id;";
            var parameters = new { role_id = role_id };

            int count = (int)_MysqlConnect.Execute(sql, parameters);
            if( count == 0 ) return false;
            return true;
        }
        #endregion
    }
}