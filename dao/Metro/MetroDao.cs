// 檔案路徑：System\dao\Metro\MetroDao.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using backend.utils;
using backend.ViewModels;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    /// <summary>
    /// 捷運 / 輕軌資料表：metro_line、metro_station、metro_line_station、metro_station_link、metro_line_shape。
    /// </summary>
    public class MetroDao
    {
        // TDX 沒有站間行駛時間時，用累計距離估算：平均 35 km/h、每站停 25 秒
        private const double EstimatedSpeedKmh = 35;
        private const int EstimatedStopSeconds = 25;

        private readonly AppSettings _appSettings;

        public MetroDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        private MySqlConnection Open() => new MySqlConnection(_appSettings.mydb);


        #region 同步寫入

        /// <summary>
        /// 把一個捷運系統的 TDX 資料寫入資料庫（同一個交易）。
        /// 車站、路線 upsert（代號不變）；站序、相鄰車站、線形整批重建；TDX 已沒有的路線改成停用。
        /// </summary>
        public async Task<MetroSyncResult> SaveMetroDataAsync(
            string railSystem, string systemName,
            List<TdxMetroLine> lines, List<TdxMetroStation> stations, List<TdxMetroStationOfLine> stationOfLines,
            List<TdxMetroTravelTime> travelTimes, List<TdxMetroShape> shapes)
        {
            var result = new MetroSyncResult { rail_system = railSystem, system_name = systemName };
            lines ??= new List<TdxMetroLine>();
            stations ??= new List<TdxMetroStation>();
            stationOfLines ??= new List<TdxMetroStationOfLine>();
            travelTimes ??= new List<TdxMetroTravelTime>();
            shapes ??= new List<TdxMetroShape>();

            using var conn = Open();
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();

            try
            {
                // ── 1. 車站 ──
                var stationRows = stations
                    .Where(s => !string.IsNullOrWhiteSpace(s.StationID) && s.StationPosition != null)
                    .GroupBy(s => s.StationID).Select(g => g.First())
                    .Select(s => new
                    {
                        uid = string.IsNullOrWhiteSpace(s.StationUID) ? $"{railSystem}-{s.StationID}" : s.StationUID,
                        railSystem,
                        code = s.StationID,
                        name = Truncate(s.StationName?.Zh_tw ?? s.StationID, 100),
                        address = Truncate(s.StationAddress, 255),
                        city = s.LocationCity,
                        town = s.LocationTown,
                        lat = s.StationPosition.PositionLat,
                        lng = s.StationPosition.PositionLon
                    }).ToList();

                await conn.ExecuteAsync(@"
                    INSERT INTO metro_station (station_uid, rail_system, station_code, station_name, station_address, city_name, town_name, mst_latitude, mst_longitude)
                    VALUES (@uid, @railSystem, @code, @name, @address, @city, @town, @lat, @lng)
                    ON DUPLICATE KEY UPDATE station_name = VALUES(station_name), station_address = VALUES(station_address),
                        city_name = VALUES(city_name), town_name = VALUES(town_name),
                        mst_latitude = VALUES(mst_latitude), mst_longitude = VALUES(mst_longitude);", stationRows, tx);

                Dictionary<string, int> stationIds = (await conn.QueryAsync<(string station_code, int mst_id)>(
                    "SELECT station_code, mst_id FROM metro_station WHERE rail_system = @railSystem;", new { railSystem }, tx))
                    .GroupBy(r => r.station_code).ToDictionary(g => g.Key, g => g.First().mst_id);
                result.station_count = stationRows.Count;

                // ── 2. 路線 ──
                var cityByStation = stationRows.ToDictionary(s => s.code, s => s.city);
                var lineRows = lines
                    .Where(l => !string.IsNullOrWhiteSpace(l.Key))
                    .GroupBy(l => l.Key).Select(g => g.First())
                    .Select(l => new
                    {
                        railSystem,
                        systemName,
                        lineNo = l.Key,
                        name = Truncate(l.LineName?.Zh_tw ?? l.Key, 100),
                        color = NormalizeColor(l.LineColor) ?? DefaultLineColor(railSystem, l.Key),
                        city = stationOfLines.Where(s => s.Key == l.Key).SelectMany(s => s.Stations ?? new List<TdxMetroLineStation>())
                            .Select(s => cityByStation.TryGetValue(s.StationID ?? "", out string c) ? c : null)
                            .Where(c => !string.IsNullOrWhiteSpace(c))
                            .GroupBy(c => c).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault()
                    }).ToList();

                await conn.ExecuteAsync(@"
                    INSERT INTO metro_line (rail_system, system_name, line_no, line_name, line_color, city_name, is_active)
                    VALUES (@railSystem, @systemName, @lineNo, @name, @color, @city, 1)
                    ON DUPLICATE KEY UPDATE system_name = VALUES(system_name), line_name = VALUES(line_name),
                        line_color = VALUES(line_color), city_name = VALUES(city_name), is_active = 1;", lineRows, tx);

                Dictionary<string, int> lineIds = (await conn.QueryAsync<(string line_no, int ml_id)>(
                    "SELECT line_no, ml_id FROM metro_line WHERE rail_system = @railSystem;", new { railSystem }, tx))
                    .ToDictionary(r => r.line_no, r => r.ml_id);

                List<string> activeLineNos = lineRows.Select(l => l.lineNo).ToList();
                await conn.ExecuteAsync(
                    "UPDATE metro_line SET is_active = 2 WHERE rail_system = @railSystem AND line_no NOT IN @activeLineNos;",
                    new { railSystem, activeLineNos = activeLineNos.DefaultIfEmpty("").ToList() }, tx);
                result.line_count = lineRows.Count;

                List<int> systemLineIds = lineIds.Values.ToList();
                if (systemLineIds.Count > 0)
                {
                    await conn.ExecuteAsync("DELETE FROM metro_line_station WHERE ml_id IN @systemLineIds;", new { systemLineIds }, tx);
                    await conn.ExecuteAsync("DELETE FROM metro_station_link WHERE ml_id IN @systemLineIds;", new { systemLineIds }, tx);
                    await conn.ExecuteAsync("DELETE FROM metro_line_shape WHERE ml_id IN @systemLineIds;", new { systemLineIds }, tx);
                }

                // ── 3. 站序 ──（同一條路線有多筆 StationOfLine 時，後面的站序往後接，避免重複）
                var sequenceRows = new List<object>();
                foreach (var group in stationOfLines.Where(s => lineIds.ContainsKey(s.Key ?? "")).GroupBy(s => s.Key))
                {
                    int mlId = lineIds[group.Key];
                    int offset = 0;
                    var seen = new HashSet<int>();
                    foreach (TdxMetroStationOfLine sol in group)
                    {
                        foreach (TdxMetroLineStation st in (sol.Stations ?? new List<TdxMetroLineStation>()).OrderBy(s => s.Sequence))
                        {
                            if (!stationIds.TryGetValue(st.StationID ?? "", out int mstId) || !seen.Add(mstId)) continue;
                            sequenceRows.Add(new { mlId, mstId, seq = ++offset, km = st.CumulativeDistance });
                        }
                    }
                }
                await conn.ExecuteAsync(@"
                    INSERT INTO metro_line_station (ml_id, mst_id, station_sequence, cumulative_km)
                    VALUES (@mlId, @mstId, @seq, @km);", sequenceRows, tx);

                // ── 4. 相鄰車站 ──（優先用 TDX 站間行駛時間；沒有的路線用站序 + 累計距離估算）
                var links = new Dictionary<(int from, int to), (int mlId, int run, int stop)>();

                void AddLink(int mlId, int from, int to, int run, int stop)
                {
                    if (from == to) return;
                    foreach (var key in new[] { (from, to), (to, from) })
                    {
                        if (!links.TryGetValue(key, out var old) || run < old.run)
                            links[key] = (mlId, run, stop);
                    }
                }

                foreach (TdxMetroTravelTime tt in travelTimes)
                {
                    if (!lineIds.TryGetValue(tt.Key ?? "", out int mlId)) continue;
                    foreach (TdxMetroStationTime t in tt.TravelTimes ?? new List<TdxMetroStationTime>())
                    {
                        if (stationIds.TryGetValue(t.FromStationID ?? "", out int from) && stationIds.TryGetValue(t.ToStationID ?? "", out int to) && t.RunTime > 0)
                            AddLink(mlId, from, to, t.RunTime, Math.Max(0, t.StopTime));
                    }
                }

                var linkedLines = links.Values.Select(v => v.mlId).ToHashSet();
                foreach (var group in stationOfLines.Where(s => lineIds.ContainsKey(s.Key ?? "")).GroupBy(s => s.Key))
                {
                    int mlId = lineIds[group.Key];
                    if (linkedLines.Contains(mlId)) continue;

                    result.estimated_line_count++;
                    foreach (TdxMetroStationOfLine sol in group)
                    {
                        var ordered = (sol.Stations ?? new List<TdxMetroLineStation>()).OrderBy(s => s.Sequence).ToList();
                        for (int i = 1; i < ordered.Count; i++)
                        {
                            TdxMetroLineStation a = ordered[i - 1], b = ordered[i];
                            // 支線車站接在主線後面時累計距離會變小，那兩站實際上不相鄰
                            double km = (b.CumulativeDistance ?? 0) - (a.CumulativeDistance ?? 0);
                            if (km <= 0) continue;
                            if (stationIds.TryGetValue(a.StationID ?? "", out int from) && stationIds.TryGetValue(b.StationID ?? "", out int to))
                                AddLink(mlId, from, to, (int)Math.Max(60, km / EstimatedSpeedKmh * 3600), EstimatedStopSeconds);
                        }
                    }
                }

                PruneSkippingLinks(links);

                await conn.ExecuteAsync(@"
                    INSERT INTO metro_station_link (ml_id, from_mst_id, to_mst_id, run_seconds, stop_seconds)
                    VALUES (@mlId, @from, @to, @run, @stop)
                    ON DUPLICATE KEY UPDATE ml_id = VALUES(ml_id), run_seconds = VALUES(run_seconds), stop_seconds = VALUES(stop_seconds);",
                    links.Select(l => new { l.Value.mlId, from = l.Key.from, to = l.Key.to, l.Value.run, l.Value.stop }), tx);
                result.link_count = links.Count;

                // ── 5. 線形 ──（同一條路線有多筆時取最長的那筆）
                var shapeRows = shapes
                    .Where(s => lineIds.ContainsKey(s.Key ?? "") && !string.IsNullOrWhiteSpace(s.Geometry))
                    .GroupBy(s => s.Key)
                    .Select(g => new { mlId = lineIds[g.Key], geometry = g.OrderByDescending(s => s.Geometry.Length).First().Geometry })
                    .ToList();
                await conn.ExecuteAsync("REPLACE INTO metro_line_shape (ml_id, geometry) VALUES (@mlId, @geometry);", shapeRows, tx);
                result.shape_count = shapeRows.Count;

                tx.Commit();
                return result;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        /// <summary>
        /// 去掉「跳站」的連線：有些系統（例如桃園機場捷運）的站間時間列出所有站兩兩組合，
        /// A→C 如果跟 A→B→C 差不多久，就不是真的相鄰，拿掉；比 A→B→C 快很多的（直達車）保留。
        /// </summary>
        private static void PruneSkippingLinks(Dictionary<(int from, int to), (int mlId, int run, int stop)> links)
        {
            const int ToleranceSeconds = 90;
            var snapshot = links.ToDictionary(l => l.Key, l => l.Value);
            var neighbors = snapshot.Keys.GroupBy(k => k.from).ToDictionary(g => g.Key, g => g.Select(k => k.to).ToList());

            foreach (var (key, value) in snapshot)
            {
                int cost = value.run + value.stop;
                bool skipping = neighbors[key.from].Any(mid =>
                    mid != key.to
                    && snapshot.TryGetValue((mid, key.to), out var second)
                    && snapshot[(key.from, mid)].mlId == value.mlId && second.mlId == value.mlId
                    && snapshot[(key.from, mid)].run + snapshot[(key.from, mid)].stop + second.run + second.stop <= cost + ToleranceSeconds);

                if (skipping) links.Remove(key);
            }
        }

        private static string NormalizeColor(string color)
        {
            if (string.IsNullOrWhiteSpace(color)) return null;
            string c = color.Trim();
            return c.StartsWith("#") ? c : "#" + c;
        }

        // TDX 沒給顏色的路線（例如高雄捷運）用官方代表色
        private static string DefaultLineColor(string railSystem, string lineNo)
        {
            switch ($"{railSystem}-{lineNo}")
            {
                case "KRTC-R": return "#E20B65";
                case "KRTC-O": return "#F8981D";
                case "KLRT-C": return "#7CBD52";
                case "TRTCMG-MK": return "#77BC1F";
                default: return "#00897B";
            }
        }

        private static string Truncate(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);

        #endregion


        #region 查詢

        public async Task<int> CountStationsAsync()
        {
            using var conn = Open();
            return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM metro_station;");
        }

        /// <summary>各捷運系統的車站數與相鄰車站連線數（檢查資料是否完整）</summary>
        public async Task<Dictionary<string, (int stations, int links)>> GetSystemStatsAsync()
        {
            using var conn = Open();
            var rows = await conn.QueryAsync<(string rail_system, int stations, int links)>(@"
                SELECT ms.rail_system, COUNT(*) AS stations,
                       (SELECT COUNT(*) FROM metro_station_link msl
                        INNER JOIN metro_line ml ON ml.ml_id = msl.ml_id AND ml.is_active = 1
                        WHERE ml.rail_system = ms.rail_system) AS links
                FROM metro_station ms
                GROUP BY ms.rail_system;");
            return rows.ToDictionary(r => r.rail_system, r => (r.stations, r.links), StringComparer.OrdinalIgnoreCase);
        }

        public class LineRow
        {
            public int ml_id { get; set; }
            public string rail_system { get; set; }
            public string system_name { get; set; }
            public string line_no { get; set; }
            public string line_name { get; set; }
            public string line_color { get; set; }
            public string city_name { get; set; }
        }

        public class StationRow
        {
            public int mst_id { get; set; }
            public string rail_system { get; set; }
            public string station_code { get; set; }
            public string station_name { get; set; }
            public double lat { get; set; }
            public double lng { get; set; }
        }

        public class LineStationRow
        {
            public int ml_id { get; set; }
            public int mst_id { get; set; }
            public int station_sequence { get; set; }
        }

        public class LinkRow
        {
            public int ml_id { get; set; }
            public int from_mst_id { get; set; }
            public int to_mst_id { get; set; }
            public int run_seconds { get; set; }
            public int stop_seconds { get; set; }
        }

        /// <summary>整個捷運路網（營運中路線），建車站圖用；資料量小（全台約 300 站），一次載入後快取</summary>
        public async Task<(List<LineRow> lines, List<StationRow> stations, List<LineStationRow> lineStations, List<LinkRow> links, Dictionary<int, string> shapes)> LoadNetworkAsync()
        {
            using var conn = Open();

            var lines = (await conn.QueryAsync<LineRow>(@"
                SELECT ml_id, rail_system, system_name, line_no, line_name, line_color, city_name
                FROM metro_line WHERE is_active = 1;")).ToList();

            var stations = (await conn.QueryAsync<StationRow>(@"
                SELECT mst_id, rail_system, station_code, station_name, mst_latitude AS lat, mst_longitude AS lng
                FROM metro_station;")).ToList();

            var lineStations = (await conn.QueryAsync<LineStationRow>(@"
                SELECT mls.ml_id, mls.mst_id, mls.station_sequence
                FROM metro_line_station mls INNER JOIN metro_line ml ON ml.ml_id = mls.ml_id AND ml.is_active = 1
                ORDER BY mls.ml_id, mls.station_sequence;")).ToList();

            var links = (await conn.QueryAsync<LinkRow>(@"
                SELECT msl.ml_id, msl.from_mst_id, msl.to_mst_id, msl.run_seconds, msl.stop_seconds
                FROM metro_station_link msl INNER JOIN metro_line ml ON ml.ml_id = msl.ml_id AND ml.is_active = 1;")).ToList();

            var shapes = (await conn.QueryAsync<(int ml_id, string geometry)>(
                "SELECT ml_id, geometry FROM metro_line_shape;")).ToDictionary(s => s.ml_id, s => s.geometry);

            return (lines, stations, lineStations, links, shapes);
        }

        #endregion
    }
}
