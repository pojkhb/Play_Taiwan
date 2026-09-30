// 檔案路徑：System\util\CurrentUser.cs
// 統一取得目前登入者的 au_id / 商家 s_id。
// 登入 Token 由 AuthService.GenerateJwtToken 簽發，帶 au_id 與 ClaimTypes.NameIdentifier 兩種 Claim；
// 商家帳號另外帶 s_id Claim。
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

        /// <summary>
        /// 取得目前登入者的 au_id；沒帶 Token（未登入）時回傳 null，給允許匿名呼叫的 API 使用。
        /// </summary>
        public static int? TryGetAuId(this ClaimsPrincipal user)
        {
            Claim claim = user?.FindFirst("au_id") ?? user?.FindFirst(ClaimTypes.NameIdentifier);
            return claim != null && int.TryParse(claim.Value, out int auId) ? auId : null;
        }

        /// <summary>
        /// 取得目前登入商家的 s_id（商家帳號登入時寫進 Token 的 s_id Claim），取不到就丟例外。
        /// </summary>
        public static int GetSId(this ClaimsPrincipal user)
        {
            Claim claim = user?.FindFirst("s_id");

            if (claim == null || !int.TryParse(claim.Value, out int sId))
            {
                throw new UnauthorizedAccessException("無法取得商家身分，請使用商家帳號重新登入");
            }

            return sId;
        }
    }
}
