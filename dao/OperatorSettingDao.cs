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
    public class OperatorSettingDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        public OperatorSettingDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
        }

        #region 個人管理-個人資料
        public OperatorResponse GetUserInfo_OperatorSetting(string op_id)
        {
            string sql = $@"
            SELECT 
                t1.op_id, t1.op_name, t1.email, t2.role_id, t2.role_name, t3.op_unit AS unit, t3.op_unit_name AS unit_name
            FROM operator t1
            INNER JOIN role t2 ON t1.role_id = t2.role_id
            INNER JOIN operator_unit t3 ON t1.op_unit = t3.op_unit AND t3.ct_code = @ct_code
            WHERE t1.op_id = @op_id
            ";
            
            var parameters = new {
                ct_code = _appSettings.ct_code,
                op_id = op_id,
            };

            return _MysqlConnect.Query<OperatorResponse>(sql, parameters).FirstOrDefault();
        }
        #endregion

        #region 帳號管理-列表資料
        public List<OperatorResponse> Get_OperatorSetting()
        {
            string sql = $@"
            SELECT 
                t1.op_id, t1.op_name, t2.role_id, t2.role_name, t1.email, t3.op_unit AS unit, t3.op_unit_name AS unit_name, t1.dashboard_cfg, t1.revoked, t1.useable
            FROM operator t1
            INNER JOIN role t2 ON t1.role_id = t2.role_id
            INNER JOIN operator_unit t3 ON t1.op_unit = t3.op_unit AND t3.ct_code = @ct_code
            WHERE t1.revoked = 0
            ";
            
            var parameters = new { ct_code = _appSettings.ct_code };

            return _MysqlConnect.Query<OperatorResponse>(sql, parameters).ToList();
        }
        #endregion

        #region 帳號管理-新增功能
        public bool Insert_OperatorSetting(OperatorRequest req)
        {
            string operator_sql = @"
                INSERT INTO operator(op_id,op_pswd,pswd_update_datetime,op_name,email,op_unit,dashboard_cfg,role_id,create_id,create_datetime)
                VALUES(@op_id,@op_pswd,DATE_SUB(NOW(), INTERVAL 1 DAY),@op_name,@email,@op_unit,@dashboard_cfg,@role_id,@create_id,now())
            ";

            string operator_pswd_rec_sql = @"
                INSERT INTO operator_pswd_rec(op_id,op_pswd)
                VALUES(@op_id,@op_pswd)
            ";
            
            sha256Hash sha256 = new sha256Hash();

            var parameters = new {
                op_id = req.op_id,
                /* Sha256加密 */
                op_pswd = sha256.getSha256(req.op_pswd, this._appSettings.hash_key),
                op_name = req.op_name,
                email = req.email,
                op_unit = req.unit,
                dashboard_cfg = req.dashboard_cfg,
                role_id = req.role_id,
                create_id = req.create_id
            };

            int count = (int)_MysqlConnect.Execute(operator_sql, parameters);
            _MysqlConnect.Execute(operator_pswd_rec_sql, parameters);
            /* 成功key會大於0 */
            if( count == 0 ) return false;
            return true;
        }
        #endregion

        #region 帳號管理-修改功能
        public bool Update_OperatorSetting(OperatorRequest req)
        {
            string sql = @"
                UPDATE operator
                SET op_name = @op_name, email = @email, op_unit = @op_unit, role_id = @role_id
                WHERE op_id = @op_id;
            ";
            
            sha256Hash sha256 = new sha256Hash();

            var parameters = new {
                op_id = req.op_id,
                op_name = req.op_name,
                email = req.email,
                op_unit = req.unit,
                role_id = req.role_id
            };

            int count = (int)_MysqlConnect.Execute(sql, parameters);
            /* 成功key會大於0 */
            if( count == 0 ) return false;
            return true;
            
        }
        #endregion

        #region 個人管理-修改密碼
        public string Update_OperatorPswd(OperatorRequest req)
        {
            sha256Hash sha256 = new sha256Hash();

            var parameters = new {
                op_id = req.op_id,
                op_pswd = sha256.getSha256(req.op_pswd, this._appSettings.hash_key),
                old_op_pswd = sha256.getSha256(req.old_op_pswd, this._appSettings.hash_key),
            };

            var check_op_id_sql = @"
                SELECT * FROM operator WHERE op_id = @op_id
            ";
            int op_id_count = _MysqlConnect.Query<object>(check_op_id_sql, parameters).Count();
            if( op_id_count == 0 ) return "帳號錯誤，請重新登入確認";

            var check_op_pswd_sql = @"
                SELECT * FROM operator WHERE op_id = @op_id AND op_pswd = @old_op_pswd
            ";
            int op_pswd_count = _MysqlConnect.Query<object>(check_op_pswd_sql, parameters).Count();
            if( op_pswd_count == 0 ) return "舊密碼錯誤";

            var check_op_pswd_rec_sql = @"
                SELECT pr.* FROM (
                    SELECT * FROM operator_pswd_rec
                    WHERE op_id = @op_id
                    ORDER BY id DESC
                    LIMIT 3
                ) AS pr
                WHERE pr.op_id = @op_id AND pr.op_pswd = @op_pswd;
            ";
            int op_pswd_rec = _MysqlConnect.Query<object>(check_op_pswd_rec_sql, parameters).Count();
            if( op_pswd_rec > 0 ) return "新密碼與不可與前三次相同";

            var insert_op_pswd_rec_sql = @"
                INSERT INTO operator_pswd_rec(op_id,op_pswd)
                VALUES(@op_id,@op_pswd);
            ";
            _MysqlConnect.Execute(insert_op_pswd_rec_sql, parameters);
            
            var delete_op_pswd_rec_sql = @"
                DELETE pr1 FROM operator_pswd_rec pr1
                INNER JOIN (
                    SELECT id FROM operator_pswd_rec
                    WHERE op_id = @op_id
                    ORDER BY id DESC
                    LIMIT 10 OFFSET 3
                ) AS pr2
                ON pr1.id = pr2.id;
            ";
            _MysqlConnect.Execute(delete_op_pswd_rec_sql, parameters);

            string update_op_pswd_sql = @"
                UPDATE operator
                SET op_pswd = @op_pswd, pswd_update_datetime = now()
                WHERE op_id = @op_id;
            ";
            int update_op_pswd_count = (int)_MysqlConnect.Execute(update_op_pswd_sql, parameters);
            if( update_op_pswd_count == 0 ) return "發生錯誤，密碼更新失敗";
            return "密碼更新成功";
        }
        #endregion

        #region 帳號管理-軟刪除功能
        public bool Delete_OperatorSetting(string op_id)
        {
            string sql = @"
                UPDATE operator
                SET revoked = 1
                WHERE op_id = @op_id;
            ";

            var parameters = new { op_id = op_id };

            int count = (int)_MysqlConnect.Execute(sql, parameters);
            /* 成功key會大於0 */
            if( count == 0 ) return false;
            return true;
        }
        #endregion

        #region 帳號管理-啟用停用功能
        public bool Useable_OperatorSetting(OperatorRequest req)
        {
            string sql = @"
                UPDATE operator
                SET useable = @useable
                WHERE op_id = @op_id;
            ";

            var parameters = new {
                op_id = req.op_id,
                useable = req.useable,
            };

            int count = (int)_MysqlConnect.Execute(sql, parameters);
            /* 成功key會大於0 */
            if( count == 0 ) return false;
            return true;
        }
        #endregion
    }
}