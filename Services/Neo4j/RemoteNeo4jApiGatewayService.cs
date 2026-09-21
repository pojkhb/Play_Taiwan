using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using backend.Models;
using backend.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace backend.Services.Neo4j
{
    /// <summary>
    /// 正式環境用實作：呼叫外部 POST /api/neo4j/cypher（目前只開放 MATCH 查詢，
    /// 之後同一支端點會開放完整 CRUD，屆時這支實作不用改，只要外部 API 開放寫入即可生效）。
    /// 回應格式比照現有 Services/Neo4jService.cs 的 Neo4jApiResponse&lt;T&gt;。
    /// </summary>
    public class RemoteNeo4jApiGatewayService : INeo4jGatewayService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;
        private readonly ILogger<RemoteNeo4jApiGatewayService> _logger;

        public RemoteNeo4jApiGatewayService(HttpClient httpClient, IConfiguration configuration, ILogger<RemoteNeo4jApiGatewayService> logger)
        {
            _httpClient = httpClient;
            _apiUrl = configuration["Neo4j:RemoteApiUrl"] ?? "https://vlog.angelalala.com/api/neo4j/cypher";
            _logger = logger;
        }

        public async Task<List<Dictionary<string, object>>> ExecuteCypherAsync(string query, object parameters = null)
        {
            var requestBody = new Neo4jCypherRequest
            {
                query = query,
                parameters = parameters ?? new { }
            };

            HttpResponseMessage response = await _httpClient.PostAsJsonAsync(_apiUrl, requestBody);
            string jsonString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Neo4j 外部 API 請求失敗 (HTTP {StatusCode})：{Body}", response.StatusCode, jsonString);
                throw new Exception($"Neo4j 外部 API 請求失敗 (HTTP {response.StatusCode})");
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var apiResponse = JsonSerializer.Deserialize<Neo4jApiResponse<List<Dictionary<string, object>>>>(jsonString, options);
            return apiResponse?.data ?? new List<Dictionary<string, object>>();
        }
    }
}
