using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace backend.Models
{
    /// <summary>
    /// AI（LLM）產生的文字欄位型態不固定：要求輸出字串，有時卻拆成陣列或包成物件。
    /// 一律轉成一段字串，避免整份回應反序列化失敗：
    /// 字串照用；陣列每一項各佔一行；物件取 line / text / content / dialogue / tip 欄位，
    /// 都沒有就把各欄位的文字用「：」接起來（例如 {"item": "雨傘", "reason": "午後雷陣雨"} → 雨傘：午後雷陣雨），連文字都沒有才保留原始 JSON。
    /// </summary>
    public class LooseStringJsonConverter : JsonConverter<string>
    {
        private static readonly string[] TextFields = { "line", "text", "content", "dialogue", "tip" };

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            return ToText(doc.RootElement);
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);

        internal static string ToText(JsonElement e)
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
                    var values = new List<string>();
                    foreach (JsonProperty p in e.EnumerateObject())
                        if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                            values.Add(p.Value.GetString());
                    return values.Count > 0 ? string.Join("：", values) : e.GetRawText();
                default:
                    return e.GetRawText();   // 數字、true / false
            }
        }
    }

    /// <summary>
    /// AI（LLM）產生的字串清單：要求輸出字串陣列，有時每一項卻是物件，或整個只回一個字串。
    /// 每一項用 <see cref="LooseStringJsonConverter"/> 的規則轉成一段字串，空白的略過；單一字串當成只有一項的清單。
    /// </summary>
    public class LooseStringListJsonConverter : JsonConverter<List<string>>
    {
        public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Null)
                return null;

            var items = new List<string>();
            foreach (JsonElement item in root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : new[] { root }.AsEnumerable())
            {
                string text = LooseStringJsonConverter.ToText(item);
                if (!string.IsNullOrWhiteSpace(text)) items.Add(text);
            }
            return items;
        }

        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value);
    }
}
