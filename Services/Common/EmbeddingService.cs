// 檔案路徑：System\Services\Common\EmbeddingService.cs
// 呼叫後端 repo 附的 embedding 小服務（tools/embedding_service，appsettings 的 EmbeddingService:BaseUrl），
// 把文字轉成向量寫進 Neo4j 景點的 description_embedding。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace backend.Services
{
    /// <summary>
    /// 文字 → 語意向量。模型與寫法跟資料清洗相同（intfloat/multilingual-e5-large、1024 維、正規化），
    /// 商家景點的向量才能跟政府資料景點的向量互相比較。
    /// 服務沒開、逾時或回傳格式不對時回 null，不丟例外：呼叫端照常寫入其他資料，只是先不寫向量。
    /// </summary>
    public class EmbeddingService
    {
        public const string HttpClientName = "Embedding";

        /// <summary>Neo4j 現有景點 description_embedding 的維度</summary>
        public const int Dimension = 1024;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmbeddingService> _logger;

        public EmbeddingService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<EmbeddingService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        private class EmbedResponse
        {
            public List<List<double>> embeddings { get; set; }
        }

        /// <summary>一段文字轉成 1024 維向量；文字空白或轉不出來時回 null。</summary>
        public async Task<List<double>> EmbedAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string baseUrl = _configuration["EmbeddingService:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                _logger.LogWarning("尚未設定 EmbeddingService:BaseUrl，略過 embedding");
                return null;
            }

            try
            {
                HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
                // 先序列化成字串再送，才會帶 Content-Length（PostAsJsonAsync 用 chunked 傳送）
                using var content = new StringContent(
                    JsonSerializer.Serialize(new { texts = new[] { text.Trim() } }), Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await client.PostAsync(baseUrl.TrimEnd('/') + "/embed", content);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("embedding 服務回應 {Status}：{Body}", (int)response.StatusCode, await response.Content.ReadAsStringAsync());
                    return null;
                }

                List<double> vector = (await response.Content.ReadFromJsonAsync<EmbedResponse>())?.embeddings?.FirstOrDefault();
                if (vector?.Count != Dimension)
                {
                    _logger.LogWarning("embedding 服務回傳的向量維度是 {Count}，不是 {Dimension}，略過", vector?.Count, Dimension);
                    return null;
                }
                return vector;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is NotSupportedException)
            {
                _logger.LogWarning(ex, "embedding 服務（{BaseUrl}）呼叫失敗，略過 embedding", baseUrl);
                return null;
            }
        }
    }
}
