using System;
using System.ComponentModel.DataAnnotations;
namespace backend.Models
{
    #region 帳號申請-申請功能
        public class ApplyReq
        {
            public string op_id {get;set;} /* 使用者帳號 */
            public string op_pswd {get;set;} /* 使用者密碼 */
            public string op_name {get;set;} /* 使用者名稱 */
            public string email {get;set;} /* 使用者信箱 */
            public int unit {get;set;} /* 單位 */
        }
    #endregion

    #region 帳號申請-審核清單
        public class ListReq
        {
            public int is_checked {get;set;} /* 狀態 */
        }
        public class ListRes
        {
            public string op_id {get;set;} /* 使用者帳號 */
            public string op_name {get;set;} /* 使用者名稱 */
            public string email {get;set;} /* 使用者信箱 */
            public string unit {get;set;} /* 單位 */
            public int is_checked {get;set;} /* 狀態 */
            public string role_name {get;set;} /*角色名稱*/
            public string reason {get;set;} /* 拒絕原因 */
            public DateTime create_datetime {get;set;} /* 申請時間 */
        }
    #endregion

    #region 帳號申請-確認審核
        public class CheckRep
        {
            public string op_id {get;set;} /* 使用者帳號 */
            public int role_id {get;set;} /* 角色編號 */
            public int is_checked {get;set;} /* 狀態 */
            public string create_id {get;set;} /* 審核人 */
            public string reason {get;set;} /* 拒絕原因 */
        }

        public class CheckRes
        {
            public string op_id {get;set;} /* 使用者帳號 */
            public string op_pswd {get;set;} /* 使用者密碼 */
            public string op_name {get;set;} /* 使用者名稱 */
            public string email {get;set;} /* 使用者信箱 */
            public string reason {get;set;} /* 拒絕原因 */
        }
    #endregion
}