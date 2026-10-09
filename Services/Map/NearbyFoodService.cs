// 檔案路徑：System\Services\Map\NearbyFoodService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    /// <summary>
    /// 用餐時間推播：前端在用餐時間跳出手機本機通知（不用伺服器推播，iOS 也不需要 Apple 開發者帳號），
    /// 點開後帶目前 GPS 呼叫 GET api/Map/NearbyFood，這裡找出附近的美食。
    /// 1. 餐廳來源：Neo4j 的 Restaurant（政府開放資料，AI service 連不上時略過）＋ MySQL place 的美食
    ///    （分類「飲食」或 place_type 標成 Restaurant，含商家自建的餐廳），重複的合併成一筆
    /// 2. 附上店家目前可領的 QR Code 優惠券；有優惠券的排前面（O2O 導流），其餘由近到遠
    /// 3. 已知目前沒營業的不列出；營業時間沒有資料或看不懂的照樣列出（is_open_now = null）
    /// </summary>
    public class NearbyFoodService
    {
        public const int DefaultRadiusMeters = 800;
        public const int MinRadiusMeters = 100;
        public const int MaxRadiusMeters = 3000;
        public const int DefaultLimit = 10;
        public const int MaxLimit = 30;

        private const double SameNameMergeMeters = 100;   // 同名、相距這個距離內的視為同一間（Neo4j 與 MySQL 重複）

        private readonly Neo4jService _neo4j;
        private readonly NearbyFoodDao _dao;

        public NearbyFoodService(Neo4jService neo4j, NearbyFoodDao dao)
        {
            _neo4j = neo4j;
            _dao = dao;
        }

        public async Task<NearbyFoodResponse> GetNearbyFoodAsync(double lat, double lng, int? radiusMeters, int? limit)
        {
            if (lat < -90 || lat > 90 || lng < -180 || lng > 180 || (lat == 0 && lng == 0))
                throw new BadRequestException("請帶目前位置的經緯度（lat、lng）");

            int radius = Math.Clamp(radiusMeters ?? DefaultRadiusMeters, MinRadiusMeters, MaxRadiusMeters);
            int take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            DateTime now = DateTime.UtcNow.AddHours(8);   // 台灣時間

            double latDelta = radius / 111320.0;
            double lonDelta = radius / (111320.0 * Math.Cos(lat * Math.PI / 180));
            var bounds = (minLat: lat - latDelta, maxLat: lat + latDelta, minLon: lng - lonDelta, maxLon: lng + lonDelta);

            // ── 1. 兩個來源 ──
            List<NearbyFoodPlace> places = new();

            List<Neo4jFoodRow> fromNeo4j = await _neo4j.ExecuteCypherAsync<List<Neo4jFoodRow>>(Neo4jFoodQuery, new
            {
                lat, lng, radius_m = radius, bounds.minLat, bounds.maxLat, bounds.minLon, bounds.maxLon
            }) ?? new List<Neo4jFoodRow>();   // null 代表 AI service 的 Neo4j 連不上，只用 MySQL

            foreach (Neo4jFoodRow r in fromNeo4j.Where(r => !string.IsNullOrWhiteSpace(r.name)))
            {
                var (open, todayText) = OpenStatus(r.hours, now);
                places.Add(new NearbyFoodPlace
                {
                    place_id = r.uid,
                    name = r.name.Trim(),
                    address = r.address,
                    lat = r.lat,
                    lng = r.lon,
                    distance_m = (int)Math.Round(r.distance_m),
                    image_url = r.image_url,
                    open_time = todayText,
                    is_open_now = open
                });
            }

            foreach (NearbyFoodDao.FoodPlaceRow r in await _dao.GetFoodPlacesInBoundsAsync(bounds.minLat, bounds.maxLat, bounds.minLon, bounds.maxLon))
            {
                double distance = Geo.DistanceMeters(lat, lng, r.lat, r.lng);
                if (distance > radius) continue;

                var (open, todayText) = OpenStatus(r.p_open_time, now);
                var place = new NearbyFoodPlace
                {
                    place_id = r.uid ?? $"place-{r.p_id}",
                    name = (r.p_name ?? "").Trim(),
                    address = r.p_address,
                    type = r.p_type,
                    lat = r.lat,
                    lng = r.lng,
                    distance_m = (int)Math.Round(distance),
                    image_url = r.p_image,
                    open_time = todayText,
                    is_open_now = open
                };

                NearbyFoodPlace same = places.FirstOrDefault(p => p.place_id == place.place_id
                    || (NormalizeName(p.name) == NormalizeName(place.name) && Geo.DistanceMeters(p.lat, p.lng, place.lat, place.lng) <= SameNameMergeMeters));
                if (same == null)
                {
                    places.Add(place);
                    continue;
                }

                // 重複的合併：Neo4j 沒有的資料用 MySQL 補上
                same.address ??= place.address;
                same.type ??= place.type;
                same.image_url ??= place.image_url;
                if (same.is_open_now == null && place.is_open_now != null)
                {
                    same.is_open_now = place.is_open_now;
                    same.open_time = place.open_time;
                }
            }

            // ── 2. 優惠券、排序 ──
            ILookup<string, NearbyFoodCoupon> coupons = await _dao.GetActiveCouponsAsync(places.Select(p => p.place_id));

            List<NearbyFoodPlace> result = places
                .Where(p => p.is_open_now != false)
                .Select(p =>
                {
                    p.coupons = coupons[p.place_id].ToList();
                    p.maps_deeplink_url = $"https://www.google.com/maps/search/?api=1&query={p.lat.ToString(CultureInfo.InvariantCulture)},{p.lng.ToString(CultureInfo.InvariantCulture)}";
                    return p;
                })
                .OrderByDescending(p => p.coupons.Count > 0)
                .ThenBy(p => p.distance_m)
                .Take(take)
                .ToList();

            string meal = MealOf(now);
            return new NearbyFoodResponse
            {
                meal = meal,
                message = BuildMessage(meal, result, radius),
                radius_m = radius,
                places = result
            };
        }

        #region Neo4j

        // 政府開放資料的欄位在不同來源命名不一致（name／EventName、lat／PositionLat），用 coalesce 統一
        private const string Neo4jFoodQuery = @"
            MATCH (r:Restaurant)
            WITH r, toFloat(coalesce(r.lat, r.PositionLat)) AS lat, toFloat(coalesce(r.lon, r.PositionLon)) AS lon
            WHERE lat IS NOT NULL AND lon IS NOT NULL
              AND lat >= $minLat AND lat <= $maxLat AND lon >= $minLon AND lon <= $maxLon
              AND NOT coalesce(toString(r.business_status), '') CONTAINS '歇業'
              AND NOT coalesce(toString(r.business_status), '') CONTAINS '停業'
            WITH r, lat, lon, point.distance(point({latitude: $lat, longitude: $lng}), point({latitude: lat, longitude: lon})) AS distance_m
            WHERE distance_m <= $radius_m
            OPTIONAL MATCH (r)-[:HAS_IMAGE]->(img:Image)
            OPTIONAL MATCH (r)-[:HAS_OPERATING_HOURS]->(oh:OperatingHours)
            WITH r, lat, lon, distance_m, collect(DISTINCT img.url)[0] AS image_url,
                 collect(DISTINCT CASE WHEN oh IS NULL THEN null
                        ELSE {day_of_week: toString(oh.dayOfWeek), open_time: toString(oh.openTime), close_time: toString(oh.closeTime)} END) AS hours
            RETURN r.uid AS uid, coalesce(r.name, r.EventName) AS name, coalesce(r.address, r.Address) AS address,
                   lat, lon, distance_m, image_url, hours
            ORDER BY distance_m ASC
            LIMIT 200";

        public class Neo4jFoodRow
        {
            public string uid { get; set; }
            public string name { get; set; }
            public string address { get; set; }
            public double lat { get; set; }
            public double lon { get; set; }
            public double distance_m { get; set; }
            public string image_url { get; set; }
            public List<OperatingHour> hours { get; set; } = new();
        }

        /// <summary>Neo4j OperatingHours 的一筆（星期幾、開始、結束）</summary>
        public class OperatingHour
        {
            public string day_of_week { get; set; }
            public string open_time { get; set; }
            public string close_time { get; set; }
        }

        #endregion

        #region 用餐時段、營業時間（純函式，單元測試用）

        /// <summary>台灣時間對應的餐別：5–10 點早餐、10–14 點午餐、14–17 點下午茶、17–21 點晚餐、其餘宵夜</summary>
        internal static string MealOf(DateTime taiwanTime) => taiwanTime.Hour switch
        {
            >= 5 and < 10 => "早餐",
            >= 10 and < 14 => "午餐",
            >= 14 and < 17 => "下午茶",
            >= 17 and < 21 => "晚餐",
            _ => "宵夜"
        };

        private static readonly Regex TimeRange = new(@"(\d{1,2})[:：](\d{2})\s*[-~～－–至到]\s*(\d{1,2})[:：](\d{2})");

        /// <summary>
        /// MySQL 的營業時間文字（例如「06:30-14:00」「11:00-14:00、17:00-21:00」「24小時」）。
        /// 回傳（現在有沒有營業, 今天的營業時間文字）；看不懂時營業狀態為 null。
        /// </summary>
        internal static (bool? open, string todayText) OpenStatus(string openTimeText, DateTime taiwanNow)
        {
            if (string.IsNullOrWhiteSpace(openTimeText)) return (null, null);
            string text = openTimeText.Trim();
            if (text.Contains("24小時") || text.Contains("24 小時")) return (true, text);

            var ranges = TimeRange.Matches(text)
                .Select(m => (from: Minutes(m.Groups[1].Value, m.Groups[2].Value), to: Minutes(m.Groups[3].Value, m.Groups[4].Value)))
                .Where(r => r.from != null && r.to != null)
                .Select(r => (r.from.Value, r.to.Value))
                .ToList();
            if (ranges.Count == 0) return (null, text);

            return (ranges.Any(r => InRange(taiwanNow, r.Item1, r.Item2)), text);
        }

        /// <summary>
        /// Neo4j 的營業時間（每天一筆以上）。星期幾接受 Monday／Mon、星期一／週一、1–7（1＝星期一，0 或 7＝星期日）。
        /// 有任何一筆看不懂就當作不確定（null）；今天沒有任何一筆代表今天沒營業。
        /// </summary>
        internal static (bool? open, string todayText) OpenStatus(List<OperatingHour> hours, DateTime taiwanNow)
        {
            List<OperatingHour> list = (hours ?? new List<OperatingHour>()).Where(h => h != null).ToList();
            if (list.Count == 0) return (null, null);

            var parsed = new List<(DayOfWeek day, int from, int to)>();
            foreach (OperatingHour h in list)
            {
                DayOfWeek? day = ParseDay(h.day_of_week);
                int? from = ParseTime(h.open_time), to = ParseTime(h.close_time);
                if (day == null || from == null || to == null) return (null, null);
                parsed.Add((day.Value, from.Value, to.Value));
            }

            var today = parsed.Where(p => p.day == taiwanNow.DayOfWeek).OrderBy(p => p.from).ToList();
            if (today.Count == 0) return (false, "今天公休");

            string todayText = string.Join("、", today.Select(t => $"{Clock(t.from)}-{Clock(t.to)}"));
            return (today.Any(t => InRange(taiwanNow, t.from, t.to)), todayText);
        }

        private static bool InRange(DateTime now, int from, int to)
        {
            int m = now.Hour * 60 + now.Minute;
            if (from == to) return true;                       // 例如 00:00-00:00 當作全天
            return from < to ? m >= from && m < to             // 一般
                             : m >= from || m < to;            // 跨夜，例如 22:00-02:00
        }

        private static int? Minutes(string h, string m) =>
            int.TryParse(h, out int hh) && int.TryParse(m, out int mm) && hh is >= 0 and <= 24 && mm is >= 0 and < 60 ? hh * 60 + mm : null;

        private static int? ParseTime(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            Match m = Regex.Match(text.Trim(), @"^(\d{1,2})[:：]?(\d{2})");
            return m.Success ? Minutes(m.Groups[1].Value, m.Groups[2].Value) : null;
        }

        private static string Clock(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

        private static readonly string[] ChineseDays = { "日", "一", "二", "三", "四", "五", "六" };

        private static DayOfWeek? ParseDay(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = text.Trim();

            if (int.TryParse(t, out int n))
                return n switch { 0 or 7 => DayOfWeek.Sunday, >= 1 and <= 6 => (DayOfWeek)n, _ => null };

            foreach (DayOfWeek d in Enum.GetValues<DayOfWeek>())
            {
                string en = d.ToString();
                if (t.Equals(en, StringComparison.OrdinalIgnoreCase) || t.Equals(en[..3], StringComparison.OrdinalIgnoreCase))
                    return d;
            }

            t = t.Replace("星期", "").Replace("週", "").Replace("周", "").Replace("禮拜", "").Replace("天", "日");
            int idx = Array.IndexOf(ChineseDays, t);
            return idx >= 0 ? (DayOfWeek)idx : null;
        }

        /// <summary>通知或頁面標題用的一句話</summary>
        internal static string BuildMessage(string meal, List<NearbyFoodPlace> places, int radiusMeters)
        {
            if (places == null || places.Count == 0)
                return $"{meal}時間到了！附近 {Distance(radiusMeters)}內暫時沒有找到美食，換個地方看看吧";

            NearbyFoodPlace first = places[0];
            string msg = $"{meal}時間到了！附近 {Distance(first.distance_m)}有「{first.name}」";
            if (first.coupons.Count > 0) msg += "，還有優惠券可以領";
            if (places.Count > 1) msg += $"，另外還有 {places.Count - 1} 間美食";
            return msg;
        }

        private static string Distance(int meters) =>
            meters < 1000 ? $"{meters} 公尺" : $"{(meters / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)} 公里";

        private static string NormalizeName(string name) =>
            (name ?? "").Replace(" ", "").Replace("台", "臺").Trim().ToLowerInvariant();

        #endregion
    }
}
