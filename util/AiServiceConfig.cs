// 檔案路徑：System\util\AiServiceConfig.cs
// 統一管理外部 AI Service 的網址，所有呼叫 AI Service 的地方都從這裡取得 URL。
// 網址設定於 appsettings.json 的 AiService:BaseUrl，於 Startup 啟動時載入。
// API 文件：https://aiservice.angelalala.com/api/docs
using Microsoft.Extensions.Configuration;

namespace backend.utils
{
    public static class AiServiceConfig
    {
        public const string DefaultBaseUrl = "https://aiservice.angelalala.com";

        /// <summary>AI Service 根網址（結尾不含 /）</summary>
        public static string BaseUrl { get; private set; } = DefaultBaseUrl;

        /// <summary>
        /// 由 Startup 呼叫，從設定檔讀取 AiService:BaseUrl。
        /// </summary>
        public static void Init(IConfiguration configuration)
        {
            string url = configuration["AiService:BaseUrl"];
            if (!string.IsNullOrWhiteSpace(url))
                BaseUrl = url.TrimEnd('/');
        }

        /// <summary>
        /// 組出完整 API 網址，例如 AiServiceConfig.Url("/api/neo4j/cypher")。
        /// </summary>
        public static string Url(string path) => $"{BaseUrl}/{path.TrimStart('/')}";

        // 常用端點
        public static string Neo4jCypherUrl => Url("/api/neo4j/cypher");
    }
}
