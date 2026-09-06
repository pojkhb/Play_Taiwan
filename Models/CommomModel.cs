using System.Text.Json.Serialization;

namespace backend.Models
{
    public class Location
    {
        public double? Lat     { get; set; }
        public double? Lon     { get; set; }
        public string  PlaceId { get; set; }  // 景點代號（由 GetPlaceLocation 一併回傳）
        public string  StoryId { get; set; }  // 劇本代號
    }
    public class CypherQueryRequest
    {
        [JsonPropertyName("query")]
        public string Query { get; set; }

        [JsonPropertyName("parameters")]
        public object Parameters { get; set; }
    }
}