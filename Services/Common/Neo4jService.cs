// 檔案路徑：System\Services\Common\Neo4jService.cs
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using backend.Models;
using backend.Services.Neo4j;


namespace backend.Services
{
    /// <summary>
    /// 執行 Cypher 並把結果轉成指定型別。直接連本地 Neo4j（透過 INeo4jGatewayService），不經過 AI service。
    /// </summary>
    public class Neo4jService
    {
        private readonly INeo4jGatewayService _gateway;

        // Neo4j 查回來的欄位字典轉成呼叫端的型別；有些景點的經緯度存成字串，一併接受
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };


        public Neo4jService(INeo4jGatewayService gateway)
        {
            _gateway = gateway;
        }


        public async Task<List<ValidRegionNode>> GetValidRegionsAsync()
        {
            return await ExecuteCypherAsync<List<ValidRegionNode>>(
                "MATCH (a:Attraction) WITH a.city AS city, a.town AS town, count(a) AS spot_count WHERE spot_count >= 5 RETURN city, town, spot_count")
                ?? new List<ValidRegionNode>();
        }


        /// <summary>
        /// 執行 Cypher，結果（每一列 RETURN 的別名 → 值）轉成 T，T 通常是 List&lt;某個資料列類別&gt;。
        /// Neo4j 連不上或查詢失敗時回傳 default（null），真的查無資料時是空清單，呼叫端可以藉此決定要不要改用 MySQL。
        /// </summary>
        public async Task<T> ExecuteCypherAsync<T>(string cypherQuery, object parameters = null)
        {
            try
            {
                List<Dictionary<string, object>> rows = await _gateway.ExecuteCypherAsync(cypherQuery, parameters);
                return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(rows), JsonOptions);
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
