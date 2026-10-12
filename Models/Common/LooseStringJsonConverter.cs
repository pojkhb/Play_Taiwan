using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace backend.Models
{
    /// <summary>
    /// AI（LLM）產生的文字欄位型態不固定：要求輸出字串，有時卻拆成陣列或包成物件。
    /// 一律轉成一段字串，避免整份回應反序列化失敗：
    /// 字串照用；陣列每一項各佔一行；物件取 line / text / content / dialogue 欄位，都沒有就保留原始 JSON。
    /// </summary>
    public class LooseStringJsonConverter : JsonConverter<string>
    {
        private static readonly string[] TextFields = { "line", "text", "content", "dialogue" };

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            return ToText(doc.RootElement);
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);

        private static string ToText(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.String:
                    return e.GetString();
                case JsonValueKind.Array:
                    var lines = new List<string>();
                    foreach (JsonElement item in e.EnumerateArray())
                    {
                        string line = ToText(item);
                        if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
                    }
                    return string.Join("\n", lines);
                case JsonValueKind.Object:
                    foreach (string field in TextFields)
                        if (e.TryGetProperty(field, out JsonElement text) && text.ValueKind == JsonValueKind.String)
                            return text.GetString();
                    return e.GetRawText();
                default:
                    return e.GetRawText();   // 數字、true / false
            }
        }
    }
}
