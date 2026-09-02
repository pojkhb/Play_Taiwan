using System.Text.Json.Serialization;

namespace backend.Models
{
    // 送給 Vlog API 的 Cypher 請求
    public class Neo4jCypherRequest
    {
        public string query { get; set; } = "";
        public object parameters { get; set; } = new { };
    }

    // 接收景點的回傳結構
    public class AttractionNode
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
    }

    // 接收「可用地區」的回傳結構
    public class ValidRegionNode
    {
        [JsonPropertyName("city")]
        public string City { get; set; } = "";
        
        [JsonPropertyName("town")]
        public string Town { get; set; } = "";
        
        [JsonPropertyName("spot_count")]
        public int SpotCount { get; set; }
    }
}