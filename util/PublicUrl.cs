using System;
using Microsoft.AspNetCore.Http;

namespace backend.util
{
    /// <summary>把站內路徑（例如 /images/npc/shuguang.png）組成前端可以直接用的完整網址</summary>
    public static class PublicUrl
    {
        /// <summary>已經是完整網址或空值時原樣回傳</summary>
        public static string Of(HttpRequest request, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return path;
            return $"{request.Scheme}://{request.Host}/{path.TrimStart('/')}";
        }
    }
}
