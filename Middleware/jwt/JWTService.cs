using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using backend.utils;
using backend.Models;

using Newtonsoft.Json;
using backend.dao;

namespace backend.Middleware.jwt
{
    public interface IUserService 
    {
        //JWTResponse Authenticate(JWTRequest req);
        //JWTModel GetById(string account);
    }
    public class JWTUserService
    {
        private readonly AppSettings _appSettings;
        private readonly JWTDao _dao;
        private readonly LogDao _logDao;
        private List<JWTModel> _users;

        public JWTUserService(IOptions<AppSettings> appSettings, JWTDao jWTDao, LogDao logDao)
        {
            this._appSettings = appSettings.Value;
            this._dao = jWTDao;
            this._logDao = logDao;
            this._users = _dao.GetUserList();
        }

        #region 取得資訊產生token
        public object Authenticate(JWTRequest req)
        {
            /* 判斷15min內有沒有登入失敗3次*/
            var log_data = _logDao.Get_Log(req.op_id, "登入失敗:帳號或密碼錯誤");
            int failed_time = log_data.Count;
            if (failed_time >= 5){
                var timeDifference = log_data.First().log_time.AddMinutes(15) - DateTime.Now;
                if (timeDifference.TotalMinutes < 0){
                    _logDao.Update_Log(req.op_id, "登入失敗:帳號或密碼錯誤");
                    failed_time = 0;
                }else if (timeDifference.TotalMinutes < 1){
                    return String.Format("錯誤次數過多，請於{0}秒後再試",(int)timeDifference.TotalSeconds);
                }else{
                    return String.Format("錯誤次數過多，請於{0}分鐘後再試",(int)timeDifference.TotalMinutes);
                }
            }

            sha256Hash sha256 = new sha256Hash();

            var user_data = _users.SingleOrDefault
            (m => m.op_id == req.op_id && m.op_pswd == sha256.getSha256(req.op_pswd, this._appSettings.hash_key));
            /* 判斷輸入的帳密是否正確 */
            if (user_data == null) 
            {
                return failed_time == 4 ? "帳號或密碼錯誤，錯誤次數過多，請稍後再試" : String.Format("帳號或密碼錯誤，還剩{0}次機會", 4 - failed_time);
            }else{
                /* 把過去登入失敗尚未標記的記錄標記 */
                _logDao.Update_Log(user_data.op_id, "登入失敗:帳號或密碼錯誤");
            }

            /* 取得使用者登入的權限模組 */
            List<RoleModuleProcess> _DataList = _dao.GetUserProcess(req.op_id);
            List<string> md_id_list = new List<string>();
            for (int i = 0; i < _DataList.Count; i++)
            {
                md_id_list.Add(_DataList[i].md_id_token);
            }

            JWTModel user = new JWTModel();
            if (user_data != null)
            {
                user = new JWTModel
                {
                    op_id = user_data.op_id,
                    op_name = user_data.op_name,
                    email = user_data.email,
                    unit = user_data.unit,
                    dashboard_cfg = user_data.dashboard_cfg,
                    role_id = user_data.role_id,
                    role_name = user_data.role_name,
                    pswd_date = user_data.pswd_date,
                    md_id_arr = md_id_list.ToArray(),
                };
            }

            var token = generateJwtToken(user);
            return new JWTResponse(user.op_id, user.op_name, user.email, user.unit, user.dashboard_cfg, user.role_id, user.role_name, user.pswd_date, user.md_id_arr, token);
        }
        #endregion

        public JWTModel GetById(string op_id)
        {
            return _users.FirstOrDefault(m => m.op_id == op_id);
        }

        #region token內容資訊及相關設定
        private string generateJwtToken(JWTModel user)
        {
            var token_handler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_appSettings.jwt_secret);
        
            var token_descriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(
                    new[] { new Claim("op_id", user.op_id),
                            new Claim("email", user.email), 
                            new Claim("dashboard_cfg", user.dashboard_cfg),
                            new Claim("role_id", user.role_id.ToString()),
                            new Claim("unit", user.unit),
                            new Claim("pswd_date", user.pswd_date.ToString()),
                            new Claim("md_id_arr", JsonConvert.SerializeObject(user.md_id_arr))
                        }),
                Expires = DateTime.UtcNow.AddMinutes(_appSettings.expires), // 到期時間 30 分鐘
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = token_handler.CreateToken(token_descriptor);
            return $"Bearer {token_handler.WriteToken(token)}";
        }
        #endregion
    }

    #region 驗證使用者角色權限
    public class RoleProcessService
    {
        private readonly AppSettings _appSettings;
        private readonly RoleProcessDao _roleDao;

        public RoleProcessService(IOptions<AppSettings> appSettings, RoleProcessDao roleDao)
        {
            this._appSettings = appSettings.Value;
            this._roleDao = roleDao;
        }

        public bool GetRoleProcessList(string URLMethod,string Path,string role_id)
        {
            return this._roleDao.GetRoleProcessList(URLMethod,Path,role_id);
        }
    }
    #endregion
}