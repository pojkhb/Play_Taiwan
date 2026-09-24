// 檔案路徑：System\Services\Map\ValhallaService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using backend.ViewModels;


namespace backend.Services
{
    /// <summary>
    /// 自架 Valhalla 的三個功能：
    /// 1. isochrone：N 分鐘內可到達的範圍（等時圈）
    /// 2. sources_to_targets：一個起點到多個景點的真實交通時間與距離
    /// 3. route：依序經過多個點的實際路線
    /// </summary>
    public class ValhallaService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _baseUrl;

        // 一次矩陣查詢最多帶幾個景點（Valhalla 預設上限是 2500 組，保守切小批）
        private const int MatrixChunkSize = 200;

        // 一次路線查詢最多帶幾個點（Valhalla 各 costing 的 max_locations 最小是 20，保守用 20）
        private const int RouteChunkSize = 20;


        public ValhallaService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _baseUrl = (configuration["Valhalla:BaseUrl"] ?? "http://localhost:8002").TrimEnd('/');
        }


        #region 交通方式對應

        /// <summary>
        /// 前端交通方式 → Valhalla costing。
        /// </summary>
        public static string ToCosting(string transport)
        {
            switch ((transport ?? "").Trim())
            {
                case "步行":
                    return "pedestrian";

                case "腳踏車":
                case "自行車":
                case "騎車":
                    return "bicycle";

                case "機車":
                    return "motor_scooter";

                case "自駕":
                case "汽車":
                case "開車":
                case "其他":
                    return "auto";

                // 目前 Valhalla 沒有匯入 GTFS 大眾運輸資料，公車/捷運先用 bus costing
                // （沿公車可行駛的道路計算，不含等車與轉乘時間，時間會偏短）。
                // 之後建好 transit tiles，可改成 "multimodal"。
                case "公車":
                case "捷運":
                    return "bus";

                default:
                    return "pedestrian";
            }
        }

        #endregion


        #region 1. 等時圈

        /// <summary>
        /// 回傳每個分鐘數對應的等時圈（由小到大排序）。
        /// </summary>
        public async Task<List<IsochroneBand>> GetIsochroneAsync(double lat, double lng, string costing, IEnumerable<int> minutes)
        {
            var body = new
            {
                locations = new[] { new { lat = lat, lon = lng } },
                costing = costing,
                contours = minutes.Select(m => new { time = m }).ToArray(),
                polygons = true,
                generalize = 30 // 邊界簡化到約 30 公尺，減少傳給手機的座標點數
            };

            string json = await PostAsync("isochrone", body, costing);

            var bands = new List<IsochroneBand>();

            using (var doc = JsonDocument.Parse(json))
            {
                foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
                {
                    var geometry = feature.GetProperty("geometry");
                    string type = geometry.GetProperty("type").GetString();
                    var coords = geometry.GetProperty("coordinates");

                    var band = new IsochroneBand
                    {
                        costing = costing,
                        minutes = (int)Math.Round(feature.GetProperty("properties").GetProperty("contour").GetDouble())
                    };

                    if (type == "Polygon")
                    {
                        band.polygons.Add(ParsePolygon(coords));
                    }
                    else if (type == "MultiPolygon")
                    {
                        foreach (var polygon in coords.EnumerateArray())
                        {
                            band.polygons.Add(ParsePolygon(polygon));
                        }
                    }

                    bands.Add(band);
                }
            }

            return bands.OrderBy(b => b.minutes).ToList();
        }


        /// <summary>
        /// 判斷座標是否落在等時圈內（在外框內、且不在任何破洞內）。
        /// </summary>
        public static bool Contains(IsochroneBand band, double lat, double lng)
        {
            foreach (var polygon in band.polygons)
            {
                if (polygon.Count == 0 || !InRing(polygon[0], lat, lng)) continue;

                bool inHole = polygon.Skip(1).Any(hole => InRing(hole, lat, lng));
                if (!inHole) return true;
            }

            return false;
        }


        /// <summary>
        /// 所有等時圈的外框範圍，用來先縮小 Neo4j 的查詢範圍。
        /// </summary>
        public static (double minLat, double maxLat, double minLon, double maxLon) GetBoundingBox(IEnumerable<IsochroneBand> bands)
        {
            double minLat = double.MaxValue, maxLat = double.MinValue;
            double minLon = double.MaxValue, maxLon = double.MinValue;

            foreach (var band in bands)
            {
                foreach (var polygon in band.polygons)
                {
                    if (polygon.Count == 0) continue;

                    foreach (var point in polygon[0])
                    {
                        minLon = Math.Min(minLon, point[0]);
                        maxLon = Math.Max(maxLon, point[0]);
                        minLat = Math.Min(minLat, point[1]);
                        maxLat = Math.Max(maxLat, point[1]);
                    }
                }
            }

            return (minLat, maxLat, minLon, maxLon);
        }

        #endregion


        #region 2. 真實交通時間（時間矩陣）

        /// <summary>
        /// 一個起點到多個終點的真實交通時間（秒）與距離（公里）。
        /// 回傳順序與 targets 相同；路網上到不了的會是 null。
        /// </summary>
        public async Task<List<(double? seconds, double? km)>> GetTravelTimesAsync(
            double lat, double lng, IList<(double lat, double lng)> targets, string costing)
        {
            var results = new List<(double? seconds, double? km)>();

            for (int start = 0; start < targets.Count; start += MatrixChunkSize)
            {
                var chunk = targets.Skip(start).Take(MatrixChunkSize).ToList();

                var body = new
                {
                    sources = new[] { new { lat = lat, lon = lng } },
                    targets = chunk.Select(t => new { lat = t.lat, lon = t.lng }).ToArray(),
                    costing = costing
                };

                string json = await PostAsync("sources_to_targets", body, costing);

                var chunkResults = new (double? seconds, double? km)[chunk.Count];

                using (var doc = JsonDocument.Parse(json))
                {
                    var matrix = doc.RootElement.GetProperty("sources_to_targets");

                    if (matrix.ValueKind == JsonValueKind.Array)
                    {
                        // 預設格式：[[{ to_index, time, distance }, ...]]
                        foreach (var cell in matrix[0].EnumerateArray())
                        {
                            int toIndex = cell.GetProperty("to_index").GetInt32();
                            if (toIndex < 0 || toIndex >= chunk.Count) continue;
                            chunkResults[toIndex] = (ReadNumber(cell, "time"), ReadNumber(cell, "distance"));
                        }
                    }
                    else if (matrix.ValueKind == JsonValueKind.Object)
                    {
                        // 精簡格式：{ durations: [[...]], distances: [[...]] }
                        var durations = matrix.GetProperty("durations")[0];
                        var distances = matrix.GetProperty("distances")[0];
                        for (int i = 0; i < chunk.Count; i++)
                        {
                            chunkResults[i] = (ReadNumber(durations[i]), ReadNumber(distances[i]));
                        }
                    }
                }

                results.AddRange(chunkResults);
            }

            return results;
        }

        #endregion


        #region 3. 路線

        /// <summary>
        /// 依序經過所有點的路線。每兩個相鄰點是一段（leg），各有時間、距離、路線座標。
        /// </summary>
        public async Task<ValhallaRoute> GetRouteAsync(IList<(double lat, double lng)> points, string costing)
        {
            var body = new
            {
                locations = points.Select(p => new { lat = p.lat, lon = p.lng }).ToArray(),
                costing = costing,
                units = "kilometers"
            };

            string json = await PostAsync("route", body, costing);

            var route = new ValhallaRoute { costing = costing };

            using (var doc = JsonDocument.Parse(json))
            {
                var trip = doc.RootElement.GetProperty("trip");

                foreach (var leg in trip.GetProperty("legs").EnumerateArray())
                {
                    var summary = leg.GetProperty("summary");

                    route.legs.Add(new ValhallaRouteLeg
                    {
                        seconds = summary.GetProperty("time").GetDouble(),
                        km = summary.GetProperty("length").GetDouble(),
                        coordinates = DecodePolyline6(leg.GetProperty("shape").GetString())
                    });
                }
            }

            return route;
        }


        /// <summary>
        /// 依序經過所有點、沿道路走的一條線（[經度, 緯度]），例如沒有線形資料的公車路線用站牌串出路徑。
        /// 點太多時分批查詢（前一批的最後一點是下一批的第一點），再接成一條。
        /// </summary>
        public async Task<List<double[]>> GetPathThroughAsync(IList<(double lat, double lng)> points, string costing)
        {
            var coords = new List<double[]>();
            for (int start = 0; start < points.Count - 1; start += RouteChunkSize - 1)
            {
                ValhallaRoute route = await GetRouteAsync(points.Skip(start).Take(RouteChunkSize).ToList(), costing);
                foreach (var leg in route.legs)
                    coords.AddRange(coords.Count == 0 ? leg.coordinates : leg.coordinates.Skip(1));
            }
            return coords;
        }


        /// <summary>
        /// 解碼 Valhalla 的路線形狀（精度 6 位的 encoded polyline），回傳 [經度, 緯度] 清單。
        /// </summary>
        public static List<double[]> DecodePolyline6(string encoded)
        {
            var points = new List<double[]>();
            if (string.IsNullOrEmpty(encoded)) return points;

            int index = 0, lat = 0, lng = 0;

            while (index < encoded.Length)
            {
                lat += DecodeValue(encoded, ref index);
                lng += DecodeValue(encoded, ref index);
                points.Add(new[] { lng / 1e6, lat / 1e6 });
            }

            return points;
        }

        #endregion


        #region 共用工具

        private async Task<string> PostAsync(string action, object body, string costing)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{_baseUrl}/{action}", content);
            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Valhalla {action} 失敗 (costing={costing}, Status={(int)response.StatusCode}): {json}");
            }

            return json;
        }


        private static double? ReadNumber(JsonElement obj, string name)
        {
            return obj.TryGetProperty(name, out var value) ? ReadNumber(value) : null;
        }


        private static double? ReadNumber(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.Number ? value.GetDouble() : (double?)null;
        }


        private static int DecodeValue(string encoded, ref int index)
        {
            int result = 0, shift = 0, b;

            do
            {
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20 && index < encoded.Length);

            return (result & 1) != 0 ? ~(result >> 1) : (result >> 1);
        }


        // GeoJSON 座標順序是 [lon, lat]
        private static List<List<double[]>> ParsePolygon(JsonElement polygonCoords)
        {
            return polygonCoords.EnumerateArray()
                .Select(ring => ring.EnumerateArray()
                    .Select(point => new[] { point[0].GetDouble(), point[1].GetDouble() })
                    .ToList())
                .ToList();
        }


        // 射線法判斷點是否在多邊形環內，x = 經度、y = 緯度
        private static bool InRing(List<double[]> ring, double lat, double lng)
        {
            bool inside = false;

            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                double xi = ring[i][0], yi = ring[i][1];
                double xj = ring[j][0], yj = ring[j][1];

                if ((yi > lat) != (yj > lat) &&
                    lng < (xj - xi) * (lat - yi) / (yj - yi) + xi)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        #endregion
    }


    /// <summary>Valhalla 路線結果（Service 內部使用）</summary>
    public class ValhallaRoute
    {
        public string costing { get; set; }
        public List<ValhallaRouteLeg> legs { get; set; } = new List<ValhallaRouteLeg>();
    }


    /// <summary>路線中的一段（兩個相鄰點之間）</summary>
    public class ValhallaRouteLeg
    {
        public double seconds { get; set; }
        public double km { get; set; }

        /// <summary>[經度, 緯度] 清單</summary>
        public List<double[]> coordinates { get; set; } = new List<double[]>();
    }
}