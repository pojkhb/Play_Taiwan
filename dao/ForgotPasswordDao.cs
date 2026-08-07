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
    public class ForgotPasswordDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        public ForgotPasswordDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
        }

        #region 忘記密碼-檢查帳號
        public bool CheckAccount(string op_id)
        {
            string sql = $@"
            SELECT 
                op_id
            FROM operator
            WHERE op_id = @op_id AND revoked = 0
            ";
            
            var parameters = new { op_id = op_id };
            return _MysqlConnect.Query<string>(sql, parameters).Any();
        }
        #endregion

        #region 忘記密碼-重設密碼
        public string ResetPassword(ResetPasswordRequest req)
        {
            sha256Hash sha256 = new sha256Hash();

            var parameters = new {
                op_id = req.op_id,
                op_pswd = sha256.getSha256(req.op_pswd, this._appSettings.hash_key),
            };

            var check_op_id_sql = @"
                SELECT * FROM operator WHERE op_id = @op_id
            ";
            int op_id_count = _MysqlConnect.Query<object>(check_op_id_sql, parameters).Count();
            if( op_id_count == 0 ) return "帳號錯誤，請重新確認帳號";

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
    }
}