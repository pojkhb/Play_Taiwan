using System;
using System.Text;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using backend.Middleware.jwt;
using backend.Services;
using backend.utils;
using backend.ViewModels;
using System.Text.Json;

namespace backend.Middleware
{
    public class jwtMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly AppSettings _appSettings;
        public jwtMiddleware(RequestDelegate next, IOptions<AppSettings> appSettings)
        {
            _next = next;
            _appSettings = appSettings.Value;
        }

        public async Task Invoke(HttpContext context, JWTUserService userService, RoleProcessService roleProcessService)
        {
            var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
            await attachUserToContext(context, userService, roleProcessService, token);
            if (context.Response.StatusCode != 401) await _next(context);
        }

        #region 驗證API權限
        private async Task attachUserToContext(HttpContext context, JWTUserService userService, RoleProcessService roleProcessService, string token)
        {
            /* 設定HttpRequest與HttpResponse */
            var Request = context.Request;
            var Response = context.Response;
            /* 抓取路徑 */
            string Path = Request.Path.Value;
            /* 抓取傳送方式，ex：GET、POST、DELETE、PUT */
            string URLMethod = Request.Method;
            var remoteIpAddress = Request.HttpContext.Connection.RemoteIpAddress;
            /* 加密金鑰 */
            var hs256 = new HMACSHA256(Encoding.ASCII.GetBytes(this._appSettings.jwt_secret));

            /* 判斷路徑是否需要做Token驗證 */
            if (RouteChecked(Path, URLMethod))
            {
                /* 判斷Token是否存在 */
                if (string.IsNullOrWhiteSpace(token) || token == "null") await BadResponse(Response);
                else
                {
                    /* 讀取Token的Header與PayLoad */
                    tokenEnCode _TokenEnCode = new tokenEnCode(context);
                    var jwtArr = token.Split('.');
                    var Header = _TokenEnCode.GetHeader();
                    var PayLoad = _TokenEnCode.GetPayLoad();
                    context.Items["op_id"] = PayLoad["op_id"]?.ToString();

                    Boolean success = true;
                    success = success && string.Equals(jwtArr[2], Base64UrlEncoder.Encode(hs256.ComputeHash(Encoding.ASCII.GetBytes(string.Concat(jwtArr[0], ".", jwtArr[1])))));
                    /* 驗證Token安全金鑰的值是否正確 */
                    if (!success) await BadResponse(Response);
                    else
                    {
                        /* 驗證Token是否過期 */
                        if (!TimeChecked(PayLoad["iat"].ToString(), PayLoad["exp"].ToString())) await BadResponse(Response);
                        else
                        {
                            /* 驗證使用者是否存在 */
                            if (!UserChecked(PayLoad["op_id"].ToString(), userService)) await BadResponse(Response);
                            else
                            {
                                /* 驗證使用者角色權限 */
                                if (!RolePermissionsChecked(URLMethod,Path,PayLoad["role_id"].ToString(), roleProcessService)) await BadResponse(Response);
                            }
                        }
                    }
                }
            }
        }
        #endregion

        public async Task BadResponse(HttpResponse Response)
        {
            Response.ContentType = "application/json; charset = utf-8";
            Response.StatusCode = 401;
            var res = new ResultViewModel<object>
            {
                isSuccess = false,
                message = "Unauthorized",
                Status = "Success",
            };
            await Response.WriteAsync(JsonSerializer.Serialize<ResultViewModel<object>>(res));
            await Task.CompletedTask;
        }

        #region 路由不須驗證權限
        public Boolean RouteChecked(string Path, string URLMethod)
        {
            /* 判斷Request路徑是不是登入，如果是登入，就執行Token驗證 */
            if ((Path == "/api/Login" && URLMethod == "POST")) return false;
            /* 動態樣式 */
            if ((Path == "/api/FrameFunction/FrontendStyle" && URLMethod == "GET")) return false;
            /* 跑馬燈 */
            if ((Path == "/api/FrameFunction/Marquee" && URLMethod == "GET")) return false;
            /* 申請帳號 */
            if ((Path == "/api/OperatorApply/Apply" && URLMethod == "POST")) return false;
            /* 重設密碼 */
            if ((Path == "/api/ForgotPassword/checkAccount" && URLMethod == "POST")) return false;
            if ((Path == "/api/ForgotPassword/resetPassword" && URLMethod == "POST")) return false;

            if ((Path.ToLower().Contains("/api/Image".ToLower()) && URLMethod == "GET")) return false;
            if ((Path.ToLower().Contains("/api/DropDown".ToLower()))) return false;
            if ((Path.ToLower().Contains("/api/Marquee".ToLower()))) return false;
            if (Path.ToLower().Contains("/Export".ToLower()) && Path.ToLower() != "/api/operatorsetting/export") return false;

            return true;
        }
        #endregion

        #region 驗證Token是否過期
        public Boolean TimeChecked(string StartTime, string EndTime)
        {
            //將現在時間轉換成Unix時間戳
            Int32 DateNowUnixTimestamp = (Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
            if (DateNowUnixTimestamp >= Int32.Parse(StartTime) && DateNowUnixTimestamp <= Int32.Parse(EndTime))
                return true;

            return false;
        }
        #endregion

        #region 驗證使用者是否存在
        public Boolean UserChecked(string op_id, JWTUserService userService)
        {
            /* 判斷該帳號是否存在 */
            var Users = userService.GetById(op_id);
            if (Users == null) return false;

            return true;
        }
        #endregion

        #region 驗證使用者角色權限
        public Boolean RolePermissionsChecked(string URLMethod,string Path,string role_id, RoleProcessService roleProcessService)
        {
            if (Path.ToLower() == "/api/operatorsetting/export")return true;
            bool PermissionChecked = false;
            PermissionChecked = roleProcessService.GetRoleProcessList(URLMethod,Path,role_id);
            return PermissionChecked;
        }
        #endregion
    }
}