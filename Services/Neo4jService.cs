using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using backend.Models;

namespace backend.Services
{
    public class Neo4jService
    {
        private readonly HttpClient _httpClient;
        
        // 對應 Swagger 上的 Cypher 執行 API
        private readonly string _neo4jApiUrl = "https://vlog.angelalala.com/api/admin/execute_cypher"; 

        public Neo4jService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// 找出 Neo4j 中景點數量大於等於 5 的所有縣市與行政區
        /// </summary>
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
                    
                    // 解析 API 回傳的結果
                    var validRegions = JsonSerializer.Deserialize<List<ValidRegionNode>>(jsonString, options);
                    return validRegions ?? new List<ValidRegionNode>();
                }
                
                return new List<ValidRegionNode>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Neo4j API 查詢可用地區失敗: {ex.Message}");
                return new List<ValidRegionNode>();
            }
        }
    }
}