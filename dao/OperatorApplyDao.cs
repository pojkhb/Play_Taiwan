using System.Linq;
using Dapper;

using backend.Models;
using backend.utils;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using MySql.Data.MySqlClient;
using System.Collections.Generic;
using System;
using System.IO;

namespace backend.dao
{
    public class OperatorApplyDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        public OperatorApplyDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
        }

        #region 帳號申請-審核清單
        public List<ListRes> Get_OperatorApply(int is_checked)
        {
            string sql = @"
                SELECT t1.op_id, t1.op_name, t1.email, t3.op_unit_name AS unit, is_checked, t2.role_name, t1.reason, t1.create_datetime, t2.role_name
                FROM operator_apply t1
                LEFT JOIN role t2 ON t1.role_id = t2.role_id
                INNER JOIN operator_unit t3 ON t1.op_unit = t3.op_unit AND t3.ct_code = @ct_code
                WHERE t1.is_checked = @is_checked
            ";

            var parameters = new { 
                is_checked = is_checked,
                ct_code = _appSettings.ct_code
            };

            return _MysqlConnect.Query<ListRes>(sql, parameters).ToList();
        }
        #endregion

        #region 帳號申請-申請功能
            #region (取得審查人信箱)
            public List<string> Get_RoleCheckMail()
            {
                /* S04 => 帳號申請頁面 */
                string sql = @$"
                    SELECT DISTINCT t1.email FROM operator t1
                    JOIN role_module t2 ON t1.role_id = t2.role_id
                    WHERE t2.md_id = 'S04' AND t1.revoked = 0
                ";

                var data_list = _MysqlConnect.Query<string>(sql).ToList();
                return data_list;
            }
			#endregion

            #region 新增用戶帳號審查資訊
            public string Insert_OperatorApply(ApplyReq req)
            {
                string error_count = "Error count = 0 when adding to data table";
                sha256Hash sha256 = new sha256Hash();
                
                var parameters = new DynamicParameters();
                parameters.Add("op_id", req.op_id);
                parameters.Add("op_pswd", sha256.getSha256(req.op_pswd, this._appSettings.hash_key));
                parameters.Add("op_name", req.op_name);
                parameters.Add("email", req.email);
                parameters.Add("op_unit", req.unit);

                try
                {
                    // 檢查帳號是否已存在於正式表
                    string sqlExist = "SELECT EXISTS(SELECT 1 FROM operator WHERE op_id = @op_id)";
                    if (_MysqlConnect.QueryFirstOrDefault<bool>(sqlExist, parameters))
                        return "帳號已存在，請更換其他帳號，或前往登入";

                    // 取得申請狀態與單位屬性
                    string sqlStatus = @"
                        SELECT a.is_checked, u.op_unit_group 
                        FROM (SELECT @op_id as id) t
                        LEFT JOIN operator_apply a ON a.op_id = t.id
                        LEFT JOIN operator_unit u ON u.op_unit = @op_unit";
                    
                    var statusInfo = _MysqlConnect.QueryFirstOrDefault(sqlStatus, parameters);
                    int? is_checked = (int?)statusInfo?.is_checked;
                    string unit_check = (string)statusInfo?.op_unit_group;

                    // if (is_checked == 0) return "帳號已申請過，正在待審查";

                    // 判斷自動通過條件
                    Dictionary<string, int> unit_role = new Dictionary<string, int>
                    {
                        { "縣府", 6 }, { "公所", 7 }, { "警察局", 8 }
                    };
                    bool isAutoPass = !string.IsNullOrEmpty(unit_check) && unit_role.ContainsKey(unit_check);
                    parameters.Add("target_is_checked", isAutoPass ? 1 : 0);

                    // 更新或插入申請表
                    string sqlUpsertApply = is_checked == null ?
                        @"INSERT INTO operator_apply(op_id, op_pswd, op_name, email, op_unit, is_checked, create_datetime)
                        VALUES (@op_id, @op_pswd, @op_name, @email, @op_unit, @target_is_checked, NOW())" :
                        @"UPDATE operator_apply 
                        SET op_pswd = @op_pswd, op_name = @op_name, email = @email, 
                            op_unit = @op_unit, is_checked = @target_is_checked, create_datetime = NOW() 
                        WHERE op_id = @op_id";

                    _MysqlConnect.Execute(sqlUpsertApply, parameters);

                    // 如果自動通過，直接寫入正式表
                    if (isAutoPass)
                    {
                        parameters.Add("role_id", unit_role[unit_check]);
                        string sqlInsertOp = @"
                            INSERT INTO operator (op_id, op_pswd, op_name, email, revoked, dashboard_cfg, role_id, create_id, create_datetime, op_unit)
                            VALUES (@op_id, @op_pswd, @op_name, @email, 0, ';;;;', @role_id, '審核自動通過', NOW(), @op_unit)";
                        
                        int count = _MysqlConnect.Execute(sqlInsertOp, parameters);
                        if (count == 0) return "(Check)" + error_count;
                    }

                    return isAutoPass ? "申請已通過，請前往登入" : "已成功申請，請等待審核";
                }
                catch (MySqlException ex) { return $"SQL Error: {ex.Message}"; }
                catch (Exception ex) { return $"General Error: {ex.Message}"; }
            }
            #endregion
        #endregion

        #region 帳號申請-確認審核
            #region (取得該審核通過的用戶信箱)
            public CheckRes Get_RoleCheckMail(string op_id)
            {
                string sql = "SELECT op_id, op_pswd, op_name, email, reason FROM operator_apply WHERE op_id = @op_id;";
                var parameters = new { op_id = op_id };
                var data_list = _MysqlConnect.Query<CheckRes>(sql, parameters).FirstOrDefault();
                return data_list;
            }
			#endregion

            #region 審核通過 OR 未通過
            public string Update_OperatorApply(CheckRep req)
            {
                string sql = string.Empty;
                int count = 0;
                string error_count = "Error count = 0 when adding to data table";

                var parameters = new
                {
                    role_id = req.role_id,
                    is_checked = req.is_checked,
                    op_id = req.op_id,
                    create_id = req.create_id,
                    reason = req.reason,
                };
                
                /* 通過(給予權限) */
                string role_id = req.is_checked == 1 ? ",role_id = @role_id" : string.Empty;
                /* 未通過(拒絕原因) */
                string reason = req.is_checked == 2 ? ",reason = @reason" : ",reason = NULL";

                /* 確認用戶帳號申請資訊 */
                sql = $@"
                    UPDATE operator_apply SET is_checked = @is_checked {role_id} {reason}
                    WHERE op_id = @op_id
                ";
                _MysqlConnect.Execute(sql, parameters);

                /* 審核通過執行 */
                if(req.is_checked == 1)
                {
                    sql = $@"
                        INSERT INTO operator (op_id, op_pswd, op_name, email, revoked, dashboard_cfg, role_id, create_id, create_datetime, op_unit)
                        SELECT op_id, op_pswd, op_name, email, 0, ';;;;', @role_id, @create_id, NOW(), op_unit
                        FROM operator_apply
                        WHERE op_id = @op_id;
                    ";
                    count = (int)_MysqlConnect.Execute(sql, parameters);
                    /* 成功key會大於0 */
                    if( count == 0 ) return "(Check)" + error_count;
                }

                return "success";
            }
            #endregion
        #endregion
    }
}