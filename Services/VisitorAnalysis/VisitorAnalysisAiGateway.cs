// 檔案路徑：Services\VisitorAnalysis\VisitorAnalysisAiGateway.cs
// 呼叫地端 vlog FastAPI（VisitorAnalysisAi:BaseUrl，預設 :2026），
// 路徑 /internal/visitor-analysis/*（RAG + 策略聊天，LLM 用 vlog 地端模型）。
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using backend.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace backend.Services
{
    public class VisitorAnalysisAiGateway
    {
        public const string HttpClientName = "VisitorAnalysisAi";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<VisitorAnalysisAiGateway> _logger;

        public VisitorAnalysisAiGateway(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<VisitorAnalysisAiGateway> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        private string BaseUrl
        {
            get
            {
                string url = _configuration["VisitorAnalysisAi:BaseUrl"];
                if (string.IsNullOrWhiteSpace(url))
                {
                    throw new InvalidOperationException(
                        "尚未設定 VisitorAnalysisAi:BaseUrl（請在 appsettings.json 新增，勿刪既有設定）。");
                }

                return url.TrimEnd('/');
            }
        }

        private HttpClient CreateClient()
        {
            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
            client.BaseAddress = new Uri(BaseUrl + "/");
            return client;
        }

        public async Task<VisitorAttractionListResponse> GetAttractionsAsync(
            string keyword,
            int page,
            int pageSize)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);

            string path =
                "internal/visitor-analysis/attractions" +
                $"?keyword={Uri.EscapeDataString(keyword ?? string.Empty)}" +
                $"&page={page}&page_size={pageSize}";

            using HttpClient client = CreateClient();
            using HttpResponseMessage response = await client.GetAsync(path);
            return await ReadJsonAsync<VisitorAttractionListResponse>(
                response,
                "景點清單查詢");
        }

        public async Task<VisitorStrategyChatResponse> GenerateStrategyAsync(
            VisitorStrategyChatRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.attraction_uid))
            {
                throw new ArgumentException("attraction_uid 不可為空白。");
            }

            using HttpClient client = CreateClient();
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "internal/visitor-analysis/strategy/generate",
                new
                {
                    attraction_uid = request.attraction_uid,
                    business_type = request.business_type,
                    goal = request.goal,
                    budget = request.budget,
                    visitor_type = request.visitor_type,
                    question = request.question
                });

            return await ReadJsonAsync<VisitorStrategyChatResponse>(
                response,
                "經營策略生成");
        }

        private async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, string apiName)
        {
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "{ApiName} 失敗 HTTP {Status}: {Body}",
                    apiName,
                    (int)response.StatusCode,
                    Truncate(body));

                string detail = TryExtractDetail(body) ?? Truncate(body);
                throw new InvalidOperationException(
                    $"{apiName}失敗（HTTP {(int)response.StatusCode}）：{detail}");
            }

            try
            {
                T result = JsonSerializer.Deserialize<T>(body, JsonOptions);
                if (result == null)
                {
                    throw new InvalidOperationException($"{apiName}回傳空白。");
                }

                return result;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"{apiName}回傳格式無法解析：{Truncate(body)}",
                    ex);
            }
        }

        private static string TryExtractDetail(string body)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("detail", out JsonElement detail))
                {
                    return detail.ToString();
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static string Truncate(string text, int max = 500)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }
    }
}
