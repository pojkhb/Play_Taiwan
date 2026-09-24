// 檔案路徑：System\dao\Bus\BusDao.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using backend.Services;
using backend.utils;
using backend.ViewModels;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    /// <summary>
    /// 公車 / 台灣好行資料表：bus_route、bus_sub_route、bus_route_pattern、bus_route_stop、bus_stop、
    /// bus_route_shape、bus_service_calendar、bus_timetable、taiwan_tripper_route、place_bus_stop。
    /// </summary>
    public class BusDao
    {
        public const int RouteTypeCity = 1;
        public const int RouteTypeInterCity = 2;
        public const int RouteTypeTaiwanTrip = 3;

        private readonly AppSettings _appSettings;

        public BusDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        private MySqlConnection Open() => new MySqlConnection(_appSettings.mydb);


        #region 同步寫入

        /// <summary>
        /// 把一批 TDX 資料（某縣市市區公車，或全部台灣好行）寫入資料庫，同一個交易。
        /// cityName 只用在停用舊路線的範圍（台灣好行傳 null = 全部好行路線）。
        /// </summary>
        public async Task<BusSyncResult> SaveBusDataAsync(
            int routeType, string cityName,
            List<TdxRoute> routes, List<TdxStopOfRoute> stopOfRoutes, List<TdxShape> shapes, List<TdxSchedule> schedules)
        {
            var result = new BusSyncResult();
            routes ??= new List<TdxRoute>();
            stopOfRoutes ??= new List<TdxStopOfRoute>();
            shapes ??= new List<TdxShape>();
            schedules ??= new List<TdxSchedule>();

            using var conn = Open();
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();

            try
            {
                // 市區公車同步時，跳過已經被標成台灣好行的路線（好行資料由好行同步負責維護）
                if (routeType != RouteTypeTaiwanTrip)
                {
                    var tripperUids = (await conn.QueryAsync<string>(
                        "SELECT route_uid FROM bus_route WHERE route_type = @t;", new { t = RouteTypeTaiwanTrip }, tx)).ToHashSet();
                    routes = routes.Where(r => !tripperUids.Contains(r.RouteUID)).ToList();
                    stopOfRoutes = stopOfRoutes.Where(s => !tripperUids.Contains(s.RouteUID)).ToList();
                    shapes = shapes.Where(s => !tripperUids.Contains(s.RouteUID)).ToList();
                    schedules = schedules.Where(s => !tripperUids.Contains(s.RouteUID)).ToList();
                }

                // ── 1. 路線 ──
                var headways = schedules
                    .Where(s => s.Frequencys != null && s.Frequencys.Count > 0)
                    .GroupBy(s => s.RouteUID)
                    .ToDictionary(g => g.Key, g => BuildHeadwayDesc(g.SelectMany(s => s.Frequencys)));

                var routeRows = routes
                    .Where(r => !string.IsNullOrWhiteSpace(r.RouteUID))
                    .GroupBy(r => r.RouteUID).Select(g => g.First())
                    .Select(r => new
                    {
                        uid = r.RouteUID,
                        type = routeType,
                        name = Truncate(RouteDisplayName(r, routeType), 100),
                        city = routeType == RouteTypeTaiwanTrip
                            ? (r.City != null && TdxClient.CityNames.TryGetValue(r.City, out string c) ? c : null)
                            : cityName,
                        dep = Truncate(r.DepartureStopNameZh, 255),
                        dest = Truncate(r.DestinationStopNameZh, 255),
                        fare = r.TicketPriceDescriptionZh,
                        map = r.RouteMapImageUrl,
                        headway = headways.TryGetValue(r.RouteUID, out string h) ? Truncate(h, 255) : null,
                        updated = ParseTdxTime(r.UpdateTime)
                    }).ToList();

                await conn.ExecuteAsync(@"
                    INSERT INTO bus_route (route_uid, route_type, route_name, city_name, departure_stop_name, destination_stop_name,
                                           fare_desc, route_map_url, headway_desc, is_active, data_updated_at)
                    VALUES (@uid, @type, @name, @city, @dep, @dest, @fare, @map, @headway, 1, @updated)
                    ON DUPLICATE KEY UPDATE
                        route_type = VALUES(route_type), route_name = VALUES(route_name), city_name = VALUES(city_name),
                        departure_stop_name = VALUES(departure_stop_name), destination_stop_name = VALUES(destination_stop_name),
                        fare_desc = VALUES(fare_desc), route_map_url = VALUES(route_map_url), headway_desc = VALUES(headway_desc),
                        is_active = 1, data_updated_at = VALUES(data_updated_at);", routeRows, tx);

                List<string> routeUids = routeRows.Select(r => r.uid).ToList();
                var brIds = await QueryIdMapAsync(conn, tx, "SELECT route_uid AS k, br_id AS v FROM bus_route WHERE route_uid IN @keys;", routeUids);
                result.route_count = brIds.Count;

                // TDX 已經沒有的路線 → 停用（不刪除，舊劇本的公車方案還要能查）
                result.deactivated_route_count = await conn.ExecuteAsync(@"
                    UPDATE bus_route SET is_active = 2
                    WHERE route_type = @routeType AND is_active = 1
                      AND (@cityName IS NULL OR city_name = @cityName)
                      AND route_uid NOT IN @routeUids;",
                    new { routeType, cityName, routeUids = routeUids.Count > 0 ? routeUids : new List<string> { "" } }, tx);

                if (routeType == RouteTypeTaiwanTrip && brIds.Count > 0)
                {
                    await conn.ExecuteAsync("INSERT IGNORE INTO taiwan_tripper_route (br_id) VALUES (@id);",
                        brIds.Values.Select(id => new { id }), tx);
                }

                // ── 2. 子路線 ──
                var subRouteRows = new Dictionary<string, (string routeUid, string name)>();
                foreach (var r in routes)
                    foreach (var sr in r.SubRoutes ?? new List<TdxSubRoute>())
                        if (!string.IsNullOrWhiteSpace(sr.SubRouteUID) && !subRouteRows.ContainsKey(sr.SubRouteUID))
                            subRouteRows[sr.SubRouteUID] = (r.RouteUID, PickName(routeType, sr.SubRouteName, sr.TaiwanTripName));
                foreach (var s in stopOfRoutes)
                    if (!string.IsNullOrWhiteSpace(s.SubRouteUID) && !subRouteRows.ContainsKey(s.SubRouteUID))
                        subRouteRows[s.SubRouteUID] = (s.RouteUID, PickName(routeType, s.SubRouteName, s.TaiwanTripName));

                await conn.ExecuteAsync(@"
                    INSERT INTO bus_sub_route (br_id, sub_route_uid, sub_route_name)
                    VALUES (@brId, @uid, @name)
                    ON DUPLICATE KEY UPDATE br_id = VALUES(br_id), sub_route_name = VALUES(sub_route_name);",
                    subRouteRows.Where(p => brIds.ContainsKey(p.Value.routeUid))
                        .Select(p => new { brId = brIds[p.Value.routeUid], uid = p.Key, name = Truncate(p.Value.name, 100) }), tx);

                var bsrIds = await QueryIdMapAsync(conn, tx,
                    "SELECT sub_route_uid AS k, bsr_id AS v FROM bus_sub_route WHERE sub_route_uid IN @keys;", subRouteRows.Keys.ToList());
                result.sub_route_count = bsrIds.Count;

                // ── 3. 行駛型態（子路線 × 方向）：同一組有多種停靠方式時，優先 KeyPattern，其次站數最多 ──
                var headsigns = routes.SelectMany(r => r.SubRoutes ?? new List<TdxSubRoute>())
                    .Where(sr => sr.SubRouteUID != null)
                    .GroupBy(sr => (sr.SubRouteUID, NormalizeDirection(sr.Direction)))
                    .ToDictionary(g => g.Key, g => g.First().Headsign);

                var patterns = stopOfRoutes
                    .Where(s => s.SubRouteUID != null && bsrIds.ContainsKey(s.SubRouteUID) && s.Stops != null && s.Stops.Count > 1)
                    .GroupBy(s => (s.SubRouteUID, dir: NormalizeDirection(s.Direction)))
                    .Select(g => g.OrderByDescending(s => s.KeyPattern == true).ThenByDescending(s => s.Stops.Count).First())
                    .ToList();

                await conn.ExecuteAsync(@"
                    INSERT INTO bus_route_pattern (bsr_id, direction, headsign)
                    VALUES (@bsrId, @dir, @headsign)
                    ON DUPLICATE KEY UPDATE headsign = VALUES(headsign);",
                    patterns.Select(p => new
                    {
                        bsrId = bsrIds[p.SubRouteUID],
                        dir = NormalizeDirection(p.Direction),
                        headsign = Truncate(headsigns.TryGetValue((p.SubRouteUID, NormalizeDirection(p.Direction)), out string hs) ? hs : null, 255)
                    }), tx);

                var patternRows = await conn.QueryAsync<(int brp_id, int bsr_id, int direction)>(
                    "SELECT brp_id, bsr_id, direction FROM bus_route_pattern WHERE bsr_id IN @ids;",
                    new { ids = bsrIds.Values.DefaultIfEmpty(-1).ToList() }, tx);
                var brpIds = patternRows.ToDictionary(p => (p.bsr_id, p.direction), p => p.brp_id);
                result.pattern_count = patterns.Count;

                // ── 4. 站牌 ──
                var stopRows = patterns.SelectMany(p => p.Stops)
                    .Where(s => !string.IsNullOrWhiteSpace(s.StopUID) && s.StopPosition != null)
                    .GroupBy(s => s.StopUID).Select(g => g.First())
                    .Select(s => new
                    {
                        uid = s.StopUID,
                        station = s.StationID,
                        name = Truncate(s.StopName?.Zh_tw ?? s.StopUID, 255),
                        city = CityNameFromCode(s.LocationCityCode) ?? CityNameFromCode(s.StopUID),
                        lat = s.StopPosition.PositionLat,
                        lng = s.StopPosition.PositionLon
                    }).ToList();

                await conn.ExecuteAsync(@"
                    INSERT INTO bus_stop (stop_uid, station_id, stop_name, city_name, bs_latitude, bs_longitude)
                    VALUES (@uid, @station, @name, @city, @lat, @lng)
                    ON DUPLICATE KEY UPDATE station_id = VALUES(station_id), stop_name = VALUES(stop_name),
                        city_name = VALUES(city_name), bs_latitude = VALUES(bs_latitude), bs_longitude = VALUES(bs_longitude);", stopRows, tx);

                var bsIds = await QueryIdMapAsync(conn, tx, "SELECT stop_uid AS k, bs_id AS v FROM bus_stop WHERE stop_uid IN @keys;",
                    stopRows.Select(s => s.uid).ToList());
                result.stop_count = bsIds.Count;

                // ── 5. 站序：只有站序真的變了才重建，brs_id 不變，舊劇本的 story_node_transit 才不會失效 ──
                var existing = (await conn.QueryAsync<(int brp_id, int bs_id, int stop_sequence)>(
                        "SELECT brp_id, bs_id, stop_sequence FROM bus_route_stop WHERE brp_id IN @ids ORDER BY brp_id, stop_sequence;",
                        new { ids = brpIds.Values.DefaultIfEmpty(-1).ToList() }, tx))
                    .GroupBy(r => r.brp_id)
                    .ToDictionary(g => g.Key, g => string.Join(";", g.Select(r => r.stop_sequence + ":" + r.bs_id)));

                foreach (var p in patterns)
                {
                    if (!brpIds.TryGetValue((bsrIds[p.SubRouteUID], NormalizeDirection(p.Direction)), out int brpId)) continue;

                    var seq = p.Stops
                        .Where(s => s.StopUID != null && bsIds.ContainsKey(s.StopUID))
                        .GroupBy(s => s.StopSequence).Select(g => g.First())
                        .OrderBy(s => s.StopSequence)
                        .Select(s => new { brpId, bsId = bsIds[s.StopUID], seq = s.StopSequence })
                        .ToList();

                    string signature = string.Join(";", seq.Select(s => s.seq + ":" + s.bsId));
                    if (existing.TryGetValue(brpId, out string old) && old == signature) continue;

                    await conn.ExecuteAsync("DELETE FROM bus_route_stop WHERE brp_id = @brpId;", new { brpId }, tx);
                    await conn.ExecuteAsync("INSERT INTO bus_route_stop (brp_id, bs_id, stop_sequence) VALUES (@brpId, @bsId, @seq);", seq, tx);
                    result.rebuilt_pattern_count++;
                }

                // ── 6. 線形 ──
                // 有 SubRouteUID 的線形直接對應子路線。只有 RouteUID 的線形（臺北市大多是這種）套到同路線、同方向還沒有線形的子路線，
                // 但子路線的起訖站要依序落在線形上（同 SliceShape 的判斷），繞駛、環狀的子路線不套，規劃時改沿道路計算。
                var shapeByPattern = new Dictionary<int, string>();
                foreach (var s in shapes.Where(s => s.SubRouteUID != null && !string.IsNullOrWhiteSpace(s.Geometry) && bsrIds.ContainsKey(s.SubRouteUID)))
                    if (brpIds.TryGetValue((bsrIds[s.SubRouteUID], NormalizeDirection(s.Direction)), out int brpId))
                        shapeByPattern.TryAdd(brpId, s.Geometry);

                foreach (var s in shapes.Where(s => s.SubRouteUID == null && s.RouteUID != null && !string.IsNullOrWhiteSpace(s.Geometry)))
                {
                    foreach (var p in patterns.Where(p => p.RouteUID == s.RouteUID && NormalizeDirection(p.Direction) == NormalizeDirection(s.Direction)))
                    {
                        if (!brpIds.TryGetValue((bsrIds[p.SubRouteUID], NormalizeDirection(p.Direction)), out int brpId) || shapeByPattern.ContainsKey(brpId)) continue;

                        var ends = p.Stops.Where(st => st.StopPosition != null).OrderBy(st => st.StopSequence).ToList();
                        if (ends.Count >= 2 && SliceShape(s.Geometry, ends[0].StopPosition.PositionLat, ends[0].StopPosition.PositionLon,
                                                          ends[^1].StopPosition.PositionLat, ends[^1].StopPosition.PositionLon) != null)
                            shapeByPattern[brpId] = s.Geometry;
                    }
                }

                await conn.ExecuteAsync("REPLACE INTO bus_route_shape (brp_id, geometry) VALUES (@brpId, @geometry);",
                    shapeByPattern.Select(p => new { brpId = p.Key, geometry = p.Value }), tx);

                // ── 7. 班表（起站發車時間）──
                var trips = new List<(int brpId, TdxServiceDay day, string time)>();
                foreach (var s in schedules.Where(s => s.Timetables != null && s.SubRouteUID != null && bsrIds.ContainsKey(s.SubRouteUID)))
                {
                    if (!brpIds.TryGetValue((bsrIds[s.SubRouteUID], NormalizeDirection(s.Direction)), out int brpId)) continue;
                    foreach (var t in s.Timetables)
                    {
                        var first = t.StopTimes?.OrderBy(st => st.StopSequence).FirstOrDefault();
                        string time = NormalizeTime(first?.DepartureTime ?? first?.ArrivalTime);
                        if (time != null && t.ServiceDay != null) trips.Add((brpId, t.ServiceDay, time));
                    }
                }

                var dayKeys = trips.Select(t => CalendarKey(t.day)).Distinct().ToList();
                await conn.ExecuteAsync(@"
                    INSERT IGNORE INTO bus_service_calendar (run_mon, run_tue, run_wed, run_thu, run_fri, run_sat, run_sun, run_holiday)
                    VALUES (@mon, @tue, @wed, @thu, @fri, @sat, @sun, @holiday);",
                    dayKeys.Select(k => new { mon = k[0], tue = k[1], wed = k[2], thu = k[3], fri = k[4], sat = k[5], sun = k[6], holiday = k[7] }), tx);

                var calendars = (await conn.QueryAsync<(int bsc_id, int run_mon, int run_tue, int run_wed, int run_thu, int run_fri, int run_sat, int run_sun, int run_holiday)>(
                        "SELECT bsc_id, run_mon, run_tue, run_wed, run_thu, run_fri, run_sat, run_sun, run_holiday FROM bus_service_calendar;", transaction: tx))
                    .ToDictionary(c => string.Join(",", c.run_mon, c.run_tue, c.run_wed, c.run_thu, c.run_fri, c.run_sat, c.run_sun, c.run_holiday), c => c.bsc_id);

                var scheduledBrpIds = trips.Select(t => t.brpId).Distinct().ToList();
                if (scheduledBrpIds.Count > 0)
                {
                    await conn.ExecuteAsync("DELETE FROM bus_timetable WHERE brp_id IN @ids;", new { ids = scheduledBrpIds }, tx);
                    await conn.ExecuteAsync(@"
                        INSERT IGNORE INTO bus_timetable (brp_id, bsc_id, departure_time) VALUES (@brpId, @bscId, @time);",
                        trips.Select(t => new { t.brpId, bscId = calendars[string.Join(",", CalendarKey(t.day))], t.time }).Distinct(), tx);
                }
                result.trip_count = trips.Select(t => (t.brpId, string.Join(",", CalendarKey(t.day)), t.time)).Distinct().Count();

                tx.Commit();
                return result;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }


        private static async Task<Dictionary<string, int>> QueryIdMapAsync(MySqlConnection conn, MySqlTransaction tx, string sql, List<string> keys)
        {
            var map = new Dictionary<string, int>();
            foreach (var chunk in keys.Distinct().Chunk(2000))
            {
                var rows = await conn.QueryAsync<(string k, int v)>(sql, new { keys = chunk }, tx);
                foreach (var r in rows) map[r.k] = r.v;
            }
            return map;
        }

        private static string RouteDisplayName(TdxRoute r, int routeType)
        {
            if (routeType == RouteTypeTaiwanTrip)
            {
                string trip = r.SubRoutes?.Select(sr => sr.TaiwanTripName?.Zh_tw).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                return "台灣好行 " + (trip ?? r.RouteName?.Zh_tw ?? r.RouteUID);
            }
            return r.RouteName?.Zh_tw ?? r.RouteUID;
        }

        private static string PickName(int routeType, TdxName subRouteName, TdxName tripName)
        {
            return routeType == RouteTypeTaiwanTrip
                ? tripName?.Zh_tw ?? subRouteName?.Zh_tw
                : subRouteName?.Zh_tw ?? tripName?.Zh_tw;
        }

        /// <summary>TDX 方向 0 去程、1 返程、2 迴圈，其他值（例如 255 未知）當去程</summary>
        private static int NormalizeDirection(int direction) => direction >= 0 && direction <= 2 ? direction : 0;

        private static string CityNameFromCode(string codeOrUid)
        {
            if (string.IsNullOrWhiteSpace(codeOrUid) || codeOrUid.Length < 3) return null;
            return TdxClient.CityCodes.TryGetValue(codeOrUid.Substring(0, 3), out string city)
                   && TdxClient.CityNames.TryGetValue(city, out string zh) ? zh : null;
        }

        /// <summary>行駛日 → [一, 二, 三, 四, 五, 六, 日, 國定假日]，1 = 行駛、2 = 不行駛。國定假日比照週日</summary>
        private static int[] CalendarKey(TdxServiceDay d)
        {
            int f(int v) => v == 1 ? 1 : 2;
            return new[] { f(d.Monday), f(d.Tuesday), f(d.Wednesday), f(d.Thursday), f(d.Friday), f(d.Saturday), f(d.Sunday), f(d.Sunday) };
        }

        /// <summary>"06:30" → "06:30:00"；格式不對回傳 null</summary>
        private static string NormalizeTime(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return null;
            var m = Regex.Match(t.Trim(), @"^(\d{1,2}):(\d{2})(?::(\d{2}))?$");
            if (!m.Success) return null;
            return $"{int.Parse(m.Groups[1].Value):00}:{m.Groups[2].Value}:{(m.Groups[3].Success ? m.Groups[3].Value : "00")}";
        }

        private static DateTime? ParseTdxTime(string t)
        {
            return DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto) ? dto.LocalDateTime : (DateTime?)null;
        }

        /// <summary>固定班距路線的說明文字，例如「平常日 06:00-07:00 每 15 分、07:00-16:00 每 20 分」</summary>
        private static string BuildHeadwayDesc(IEnumerable<TdxFrequency> frequencies)
        {
            var groups = frequencies
                .Where(f => f.StartTime != null && f.EndTime != null)
                .GroupBy(f => f.ServiceDay?.ServiceTag ?? DescribeDays(f.ServiceDay))
                .Select(g => g.Key + " " + string.Join("、", g
                    .GroupBy(f => (f.StartTime, f.EndTime)).Select(x => x.First())
                    .OrderBy(f => f.StartTime)
                    .Select(f => $"{f.StartTime}-{f.EndTime} 每 " +
                                 (f.MinHeadwayMins == f.MaxHeadwayMins ? $"{f.MinHeadwayMins}" : $"{f.MinHeadwayMins}~{f.MaxHeadwayMins}") + " 分")));
            return string.Join("；", groups);
        }

        public static string DescribeDays(TdxServiceDay d)
        {
            if (d == null) return "";
            return DescribeDays(new[] { d.Monday, d.Tuesday, d.Wednesday, d.Thursday, d.Friday, d.Saturday, d.Sunday }.Select(v => v == 1).ToArray());
        }

        /// <summary>[一..日] 是否行駛 → 「每日」、「週一至週五」、「週六、週日」這類說明</summary>
        public static string DescribeDays(bool[] run)
        {
            string[] names = { "一", "二", "三", "四", "五", "六", "日" };
            if (run.All(r => r)) return "每日";
            if (run.Take(5).All(r => r) && !run[5] && !run[6]) return "週一至週五";
            if (!run.Take(5).Any(r => r) && run[5] && run[6]) return "週六、週日";
            var days = names.Where((_, i) => run[i]).ToList();
            return days.Count == 0 ? "不定期" : "週" + string.Join("、", days);
        }

        private static string Truncate(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);

        #endregion


        #region 景點附近站牌（place_bus_stop）

        public class StopPoint
        {
            public int bs_id { get; set; }
            public double lat { get; set; }
            public double lng { get; set; }
        }

        /// <summary>
        /// 取範圍內營運中路線經過的站牌。tripperOnly = true 時只取台灣好行沿線站牌；否則依 cityName（null = 全部）。
        /// </summary>
        public async Task<List<StopPoint>> GetStopsForScopeAsync(string cityName, bool tripperOnly)
        {
            string sql = @"
                SELECT DISTINCT bs.bs_id, bs.bs_latitude AS lat, bs.bs_longitude AS lng
                FROM bus_stop bs
                INNER JOIN bus_route_stop brs ON brs.bs_id = bs.bs_id
                INNER JOIN bus_route_pattern brp ON brp.brp_id = brs.brp_id
                INNER JOIN bus_sub_route bsr ON bsr.bsr_id = brp.bsr_id
                INNER JOIN bus_route br ON br.br_id = bsr.br_id AND br.is_active = 1
                WHERE (@tripperOnly = 0 OR br.route_type = 3)
                  AND (@cityName IS NULL OR bs.city_name = @cityName);
            ";

            using var conn = Open();
            return (await conn.QueryAsync<StopPoint>(sql, new { cityName, tripperOnly = tripperOnly ? 1 : 0 })).ToList();
        }

        /// <summary>覆寫這批景點的附近站牌（先刪後寫）</summary>
        public async Task SavePlaceBusStopsAsync(List<string> placeIds, List<(string placeId, int bsId, int walkMeters, double walkMinutes)> pairs)
        {
            if (placeIds.Count == 0) return;

            using var conn = Open();
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();

            foreach (var chunk in placeIds.Distinct().Chunk(1000))
                await conn.ExecuteAsync("DELETE FROM place_bus_stop WHERE place_id IN @ids;", new { ids = chunk }, tx);

            await conn.ExecuteAsync(@"
                INSERT IGNORE INTO place_bus_stop (place_id, bs_id, walk_distance_m, walk_minutes)
                VALUES (@placeId, @bsId, @walkMeters, @walkMinutes);",
                pairs.Select(p => new { p.placeId, p.bsId, p.walkMeters, walkMinutes = Math.Round(p.walkMinutes, 1) }), tx);

            tx.Commit();
        }

        #endregion


        #region 查詢

        public async Task<int> CountActiveRoutesAsync()
        {
            using var conn = Open();
            return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bus_route WHERE is_active = 1;");
        }

        public async Task<int> CountPlaceBusStopsAsync()
        {
            using var conn = Open();
            return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM place_bus_stop;");
        }

        /// <summary>座標附近的站牌（直線距離），附經過的營運中路線</summary>
        public async Task<List<BusStopNearbyItem>> GetNearbyStopsAsync(double lat, double lng, int radiusM, int limit)
        {
            double dLat = radiusM / 111000.0;
            double dLng = radiusM / (111000.0 * Math.Cos(lat * Math.PI / 180));

            string stopSql = @"
                SELECT bs_id, stop_name, bs_latitude AS lat, bs_longitude AS lng,
                       ROUND(ST_Distance_Sphere(POINT(@lng, @lat), POINT(bs_longitude, bs_latitude))) AS distance_m
                FROM bus_stop
                WHERE bs_latitude BETWEEN @minLat AND @maxLat AND bs_longitude BETWEEN @minLng AND @maxLng
                HAVING distance_m <= @radiusM
                ORDER BY distance_m
                LIMIT @limit;
            ";

            using var conn = Open();
            var stops = (await conn.QueryAsync<BusStopNearbyItem>(stopSql, new
            {
                lat, lng, radiusM, limit,
                minLat = lat - dLat, maxLat = lat + dLat, minLng = lng - dLng, maxLng = lng + dLng
            })).ToList();

            var routes = await GetRoutesAtStopsAsync(conn, stops.Select(s => s.bs_id).ToList());
            foreach (var s in stops) s.routes = routes.TryGetValue(s.bs_id, out var r) ? r : new List<BusRouteBrief>();

            // 沒有任何營運中路線經過的站牌不顯示
            return stops.Where(s => s.routes.Count > 0).ToList();
        }

        private static async Task<Dictionary<int, List<BusRouteBrief>>> GetRoutesAtStopsAsync(MySqlConnection conn, List<int> stopIds)
        {
            if (stopIds.Count == 0) return new Dictionary<int, List<BusRouteBrief>>();

            var rows = await conn.QueryAsync<(int bs_id, int br_id, string route_name, int route_type)>(@"
                SELECT DISTINCT brs.bs_id, br.br_id, br.route_name, br.route_type
                FROM bus_route_stop brs
                INNER JOIN bus_route_pattern brp ON brp.brp_id = brs.brp_id
                INNER JOIN bus_sub_route bsr ON bsr.bsr_id = brp.bsr_id
                INNER JOIN bus_route br ON br.br_id = bsr.br_id AND br.is_active = 1
                WHERE brs.bs_id IN @stopIds
                ORDER BY br.route_type DESC, br.route_name;", new { stopIds });

            return rows.GroupBy(r => r.bs_id).ToDictionary(g => g.Key, g => g.Select(r => new BusRouteBrief
            {
                br_id = r.br_id,
                route_name = r.route_name,
                route_type = r.route_type,
                route_type_name = StoryDao.RouteTypeName(r.route_type)
            }).ToList());
        }

        /// <summary>路線詳情：各行駛型態的完整站牌、線形、班表；查無路線回傳 null</summary>
        public async Task<BusRouteDetail> GetRouteDetailAsync(int brId)
        {
            using var conn = Open();

            var route = await conn.QueryFirstOrDefaultAsync<BusRouteDetail>(@"
                SELECT br_id, route_uid, route_name, route_type, city_name, departure_stop_name, destination_stop_name,
                       fare_desc, route_map_url, headway_desc
                FROM bus_route WHERE br_id = @brId;", new { brId });
            if (route == null) return null;

            route.route_type_name = StoryDao.RouteTypeName(route.route_type);

            if (route.route_type == RouteTypeTaiwanTrip)
            {
                route.tripper = await conn.QueryFirstOrDefaultAsync<TaiwanTripperInfo>(@"
                    SELECT ttr_theme AS theme, ttr_introduction AS introduction, ttr_cover_image AS cover_image, ttr_website_url AS website_url
                    FROM taiwan_tripper_route WHERE br_id = @brId;", new { brId });
            }

            route.patterns = (await conn.QueryAsync<BusRoutePatternDetail>(@"
                SELECT brp.brp_id, bsr.sub_route_name, brp.direction, brp.headsign
                FROM bus_route_pattern brp
                INNER JOIN bus_sub_route bsr ON bsr.bsr_id = brp.bsr_id
                WHERE bsr.br_id = @brId
                ORDER BY bsr.sub_route_uid, brp.direction;", new { brId })).ToList();

            List<int> brpIds = route.patterns.Select(p => p.brp_id).DefaultIfEmpty(-1).ToList();

            var stops = (await conn.QueryAsync<(int brp_id, int bs_id, int stop_sequence, string stop_name, double lat, double lng)>(@"
                SELECT brs.brp_id, bs.bs_id, brs.stop_sequence, bs.stop_name, bs.bs_latitude AS lat, bs.bs_longitude AS lng
                FROM bus_route_stop brs INNER JOIN bus_stop bs ON bs.bs_id = brs.bs_id
                WHERE brs.brp_id IN @brpIds ORDER BY brs.brp_id, brs.stop_sequence;", new { brpIds })).ToList();

            var shapes = (await conn.QueryAsync<(int brp_id, string geometry)>(
                "SELECT brp_id, geometry FROM bus_route_shape WHERE brp_id IN @brpIds;", new { brpIds }))
                .ToDictionary(s => s.brp_id, s => s.geometry);

            var times = (await conn.QueryAsync<(int brp_id, int run_mon, int run_tue, int run_wed, int run_thu, int run_fri, int run_sat, int run_sun, TimeSpan departure_time)>(@"
                SELECT bt.brp_id, c.run_mon, c.run_tue, c.run_wed, c.run_thu, c.run_fri, c.run_sat, c.run_sun, bt.departure_time
                FROM bus_timetable bt INNER JOIN bus_service_calendar c ON c.bsc_id = bt.bsc_id
                WHERE bt.brp_id IN @brpIds ORDER BY bt.brp_id, bt.departure_time;", new { brpIds })).ToList();

            foreach (var p in route.patterns)
            {
                p.direction_name = p.direction == 1 ? "返程" : p.direction == 2 ? "迴圈" : "去程";
                p.stops = stops.Where(s => s.brp_id == p.brp_id).Select(s => new BusPatternStop
                {
                    bs_id = s.bs_id, stop_sequence = s.stop_sequence, stop_name = s.stop_name, lat = s.lat, lng = s.lng
                }).ToList();
                p.shape = shapes.TryGetValue(p.brp_id, out string wkt) ? ParseWkt(wkt) : new List<double[]>();
                p.departures = times.Where(t => t.brp_id == p.brp_id)
                    .GroupBy(t => DescribeDays(new[] { t.run_mon, t.run_tue, t.run_wed, t.run_thu, t.run_fri, t.run_sat, t.run_sun }.Select(v => v == 1).ToArray()))
                    .Select(g => new BusDepartureGroup { service_days = g.Key, times = g.Select(t => t.departure_time.ToString(@"hh\:mm")).ToList() })
                    .ToList();
            }

            return route;
        }

        /// <summary>WKT LINESTRING / MULTILINESTRING → [經度, 緯度] 清單</summary>
        public static List<double[]> ParseWkt(string wkt)
        {
            var points = new List<double[]>();
            if (string.IsNullOrWhiteSpace(wkt)) return points;

            string body = Regex.Replace(wkt, @"[A-Za-z()]", " ");
            foreach (string pair in body.Split(','))
            {
                string[] parts = pair.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
                {
                    points.Add(new[] { lon, lat });
                }
            }
            return points;
        }

        /// <summary>公車線形在上下車站之間的那一段（[經度, 緯度]）；離線形太遠或方向不對時回傳 null（改用站牌連線）</summary>
        public static List<double[]> SliceShape(string wkt, double boardLat, double boardLng, double alightLat, double alightLng)
        {
            List<double[]> shape = ParseWkt(wkt);
            if (shape.Count < 2) return null;

            var (ia, da) = Geo.NearestVertex(shape, boardLat, boardLng, 0);
            var (ib, db) = Geo.NearestVertex(shape, alightLat, alightLng, ia);   // 下車站從上車站之後找，處理環狀路線
            if (ia < 0 || ib < 0 || da > 300 || db > 300 || ib <= ia) return null;

            var coords = new List<double[]> { new[] { boardLng, boardLat } };
            coords.AddRange(shape.Skip(ia).Take(ib - ia + 1));
            coords.Add(new[] { alightLng, alightLat });
            return coords;
        }

        /// <summary>台灣好行路線列表（營運中）</summary>
        public async Task<List<TaiwanTripperRouteItem>> GetTaiwanTripRoutesAsync()
        {
            using var conn = Open();
            return (await conn.QueryAsync<TaiwanTripperRouteItem>(@"
                SELECT br.br_id, br.route_name, br.city_name, br.departure_stop_name, br.destination_stop_name, br.fare_desc,
                       ttr.ttr_theme AS theme, ttr.ttr_cover_image AS cover_image,
                       (SELECT COUNT(DISTINCT brs.bs_id)
                        FROM bus_sub_route bsr
                        INNER JOIN bus_route_pattern brp ON brp.bsr_id = bsr.bsr_id
                        INNER JOIN bus_route_stop brs ON brs.brp_id = brp.brp_id
                        WHERE bsr.br_id = br.br_id) AS stop_count
                FROM bus_route br
                LEFT JOIN taiwan_tripper_route ttr ON ttr.br_id = br.br_id
                WHERE br.route_type = 3 AND br.is_active = 1
                ORDER BY COALESCE(ttr.sort_order, 0), br.city_name, br.route_name;")).ToList();
        }

        /// <summary>站牌的 TDX StopUID（查即時到站用）；查無回傳 null</summary>
        public async Task<string> GetStopUidAsync(int bsId)
        {
            using var conn = Open();
            return await conn.ExecuteScalarAsync<string>("SELECT stop_uid FROM bus_stop WHERE bs_id = @bsId;", new { bsId });
        }

        #endregion
    }
}
