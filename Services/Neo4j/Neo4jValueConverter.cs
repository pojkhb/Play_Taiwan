using System.Collections.Generic;
using System.Text.Json;

namespace backend.Services.Neo4j
{
    /// <summary>
    /// 統一把 Neo4j 查詢結果的欄位值轉成一般 .NET 型別。
    /// 兩種 INeo4jGatewayService 實作回傳的原始型別不一樣：
    /// LocalNeo4jDriverGatewayService 用官方 Driver，值是原生 .NET 型別（string/List&lt;object&gt;…）；
    /// RemoteNeo4jApiGatewayService 是反序列化 JSON，值會是 System.Text.Json.JsonElement。
    /// 上層業務邏輯（PlaceVersionChainService）不應該關心目前是哪一種，統一透過這裡轉換。
    /// </summary>
    public static class Neo4jValueConverter
    {
        public static string AsString(object value)
        {
            if (value == null) return null;
            if (value is JsonElement element)
            {
                return element.ValueKind == JsonValueKind.Null ? null : element.ToString();
            }
            return value.ToString();
        }

        public static List<string> AsStringList(object value)
        {
            var result = new List<string>();
            if (value == null) return result;

            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        result.Add(item.ToString());
                    }
                }
                return result;
            }

            if (value is System.Collections.IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    result.Add(item?.ToString());
                }
            }
            return result;
        }

        public static Dictionary<string, object> AsDictionary(object value)
        {
            if (value == null) return null;

            if (value is JsonElement element)
            {
                if (element.ValueKind != JsonValueKind.Object) return null;
                var dict = new Dictionary<string, object>();
                foreach (JsonProperty prop in element.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.ToString();
                }
                return dict;
            }

            if (value is IDictionary<string, object> nativeDict)
            {
                return new Dictionary<string, object>(nativeDict);
            }

            return null;
        }

        /// <summary>
        /// 把 collect(...) 這類回傳的清單轉成通用 List&lt;object&gt;，清單裡每個元素可能是
        /// map（用 AsDictionary 再轉一次）或純量值，呼叫端自行決定怎麼處理每個元素。
        /// </summary>
        public static List<object> AsList(object value)
        {
            var result = new List<object>();
            if (value == null) return result;

            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        result.Add(item);
                    }
                }
                return result;
            }

            if (value is string)
            {
                return result;
            }

            if (value is System.Collections.IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    result.Add(item);
                }
            }
            return result;
        }

        public static double? AsDouble(object value)
        {
            if (value == null) return null;

            if (value is JsonElement element)
            {
                return element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double jsonNumber)
                    ? jsonNumber
                    : (double?)null;
            }

            return value switch
            {
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                decimal dec => (double)dec,
                _ => double.TryParse(value.ToString(), out double parsed) ? parsed : (double?)null
            };
        }
    }
}
