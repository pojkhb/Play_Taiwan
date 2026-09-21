// 檔案路徑：System\Services\Common\Neo4jService.cs
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


        /// <summary>
        /// 依景點名稱（模糊比對）查詢 Neo4j 裡真實、準確的經緯度，並一併帶回該景點的 uid。
        /// 用於 AI 生成劇本節點時，優先用這個真實資料源，避免 Nominatim 誤配到錯誤縣市的座標；
        /// 找到 uid 時，呼叫端應該把它存成 md_story_node.place_id，這樣任務生成（md_place_type／
        /// Neo4j 圖片查詢）才能正確關聯到這個景點——只有座標沒有 uid 的話，任務系統會完全查不到這個節點。
        /// 查無結果時三個欄位皆回傳 null。
        /// </summary>
        public async Task<(double? lat, double? lon, string uid)> FindAttractionCoordinatesAsync(string placeName)
        {
            if (string.IsNullOrWhiteSpace(placeName)) return (null, null, null);

            string cypherQuery = @"
                MATCH (a:Attraction)
                WHERE a.name CONTAINS $keyword AND a.lat IS NOT NULL AND a.lon IS NOT NULL
                RETURN a.lat AS lat, a.lon AS lon, a.uid AS uid
                LIMIT 1
            ";

            var result = await ExecuteCypherAsync<List<AttractionMatchRow>>(cypherQuery, new { keyword = placeName });

            if (result != null && result.Count > 0)
            {
                return (result[0].lat, result[0].lon, result[0].uid);
            }

            return (null, null, null);
        }

        private class AttractionMatchRow
        {
            public double? lat { get; set; }
            public double? lon { get; set; }
            public string uid { get; set; }
        }
    }
}