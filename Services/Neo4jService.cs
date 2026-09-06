using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using backend.Models;

namespace backend.Services
{
    // 用來承接 FastAPI 格式的 Response
    public class Neo4jApiResponse<TData>
    {
        public string status { get; set; }
        public int count { get; set; }
        public TData data { get; set; }
    }

    public class Neo4jService
    {
        private readonly HttpClient _httpClient;
        
        // 對應 Swagger 上的 Cypher 查詢 API
        private readonly string _neo4jApiUrl = "https://vlog.angelalala.com/api/neo4j/cypher"; 

        public Neo4jService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<List<ValidRegionNode>> GetValidRegionsAsync()
        {
            try
            {
                var requestBody = new Neo4jCypherRequest
                {
                    query = "MATCH (a:Attraction) WITH a.city AS city, a.town AS town, count(a) AS spot_count WHERE spot_count >= 5 RETURN city, town, spot_count",
                    parameters = new { }
                };

                var response = await _httpClient.PostAsJsonAsync(_neo4jApiUrl, requestBody);
                
                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    
                    var apiResponse = JsonSerializer.Deserialize<Neo4jApiResponse<List<ValidRegionNode>>>(jsonString, options);
                    return apiResponse?.data ?? new List<ValidRegionNode>();
                }
                
                return new List<ValidRegionNode>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Neo4j API 查詢發生錯誤: {ex.Message}");
                return new List<ValidRegionNode>();
            }
        }

        public async Task<T> ExecuteCypherAsync<T>(string cypherQuery, object parameters = null)
        {
            try
            {
                var requestBody = new Neo4jCypherRequest
                {
                    query = cypherQuery,
                    parameters = parameters ?? new { }
                };

                var response = await _httpClient.PostAsJsonAsync(_neo4jApiUrl, requestBody);
                
                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    
                    var apiResponse = JsonSerializer.Deserialize<Neo4jApiResponse<T>>(jsonString, options);
                    return apiResponse != null ? apiResponse.data : default;
                }
                
                var errorMsg = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Neo4j API 請求失敗 (HTTP {response.StatusCode}): {errorMsg}");
                return default;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"執行共用 Cypher 查詢失敗: {ex.Message}");
                return default;
            }
        }
    }
}