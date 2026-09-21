// 檔案路徑：System\util\CurrentUser.cs
// 統一取得目前登入者的 au_id。
// 登入 Token 由 AuthService.GenerateJwtToken 簽發，帶 au_id 與 ClaimTypes.NameIdentifier 兩種 Claim。
using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace backend.utils
{
    public static class CurrentUser
    {
        /// <summary>
        /// 取得目前登入者的 au_id，取不到就丟例外（由 Controller 的 try/catch 轉成錯誤訊息）。
        /// </summary>
        public static int GetAuId(this ClaimsPrincipal user)
        {
            Claim claim = user?.FindFirst("au_id") ?? user?.FindFirst(ClaimTypes.NameIdentifier);

            if (claim == null || !int.TryParse(claim.Value, out int auId))
            {
                throw new UnauthorizedAccessException("無法取得當前登入身分，請重新登入");
            }

            return auId;
        }

        /// <summary>
        /// 取得目前登入者的 au_id；DAO/Service 透過 IHttpContextAccessor 取用時使用。
        /// </summary>
        public static int GetAuId(this HttpContext context)
        {
            return GetAuId(context?.User);
        }
    }
}
