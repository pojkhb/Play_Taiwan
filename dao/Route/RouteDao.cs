// 檔案路徑：System\dao\Route\RouteDao.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using backend.utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    /// <summary>
    /// 劇本交通路線用的查詢：劇本節點座標、附近公車站牌、直達公車。
    /// </summary>
    public class RouteDao
    {
        private readonly AppSettings _appSettings;

        public RouteDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        private MySqlConnection Open() => new MySqlConnection(_appSettings.mydb);


        #region 劇本與節點

        public class StoryRow
        {
            public int s_id { get; set; }
            public int au_id { get; set; }
            public string story_title { get; set; }
            public string city_name { get; set; }
            public string district_name { get; set; }
        }

        public class NodeRow
        {
            public int sn_id { get; set; }
            public int s_id { get; set; }
            public int sn_order { get; set; }
            public string sn_title { get; set; }
            public string place_id { get; set; }
        }

        public async Task<StoryRow> GetStoryAsync(int storyId)
        {
            using var conn = Open();
            return await conn.QueryFirstOrDefaultAsync<StoryRow>(@"
                SELECT s_id, au_id, story_title, city_name, district_name
                FROM story WHERE s_id = @storyId;", new { storyId });
        }

        /// <summary>使用者自己的劇本（有節點的），新的在前；自己沒有劇本時回傳最近的公開劇本（測試帳號也能看到測試資料）</summary>
        public async Task<List<StoryRow>> GetStoriesAsync(int auId, int limit)
        {
            const string sql = @"
                SELECT s.s_id, s.au_id, s.story_title, s.city_name, s.district_name
                FROM story s
                WHERE s.is_active = 1 AND (@auId = 0 OR s.au_id = @auId)
                  AND EXISTS (SELECT 1 FROM story_node sn WHERE sn.s_id = s.s_id)
                ORDER BY s.created_at DESC, s.s_id DESC
                LIMIT @limit;";

            using var conn = Open();
            var own = (await conn.QueryAsync<StoryRow>(sql, new { auId, limit })).ToList();
            if (own.Count > 0 || auId == 0) return own;
            return (await conn.QueryAsync<StoryRow>(sql, new { auId = 0, limit })).ToList();
        }

        public async Task<List<NodeRow>> GetStoryNodesAsync(IEnumerable<int> storyIds)
        {
            var ids = storyIds.Distinct().ToList();
            if (ids.Count == 0) return new List<NodeRow>();

            using var conn = Open();
            return (await conn.QueryAsync<NodeRow>(@"
                SELECT sn_id, s_id, sn_order, sn_title, place_id
                FROM story_node WHERE s_id IN @ids
                ORDER BY s_id, sn_order, sn_id;", new { ids })).ToList();
        }

        /// <summary>
        /// 景點 uuid → 景點名稱與座標。
        /// story_node 存的是 Neo4j uuid，MySQL 用 place_type（uuid + 名稱）→ place（名稱 + 座標）橋接（同 MapDao 的作法）。
        /// </summary>
        public async Task<Dictionary<string, (string name, double? lat, double? lng)>> GetPlacePointsAsync(IEnumerable<string> placeIds)
        {
            var ids = placeIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
            var result = new Dictionary<string, (string name, double? lat, double? lng)>();
            if (ids.Count == 0) return result;

            using var conn = Open();
            var names = (await conn.QueryAsync<(string place_id, string place_name)>(@"
                SELECT place_id, MIN(place_name) AS place_name FROM place_type
                WHERE place_id IN @ids GROUP BY place_id;", new { ids })).ToList();

            var nameList = names.Select(n => n.place_name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
            var coords = nameList.Count == 0
                ? new Dictionary<string, (double lat, double lng)>()
                : (await conn.QueryAsync<(string p_name, double lat, double lng)>(@"
                    SELECT p_name, p_latitude AS lat, p_longitude AS lng FROM place
                    WHERE p_name IN @nameList AND p_latitude IS NOT NULL AND p_longitude IS NOT NULL
                    ORDER BY p_id;", new { nameList }))
                    .GroupBy(p => p.p_name).ToDictionary(g => g.Key, g => (g.First().lat, g.First().lng));

            foreach (var n in names)
            {
                bool found = coords.TryGetValue(n.place_name ?? "", out var c);
                result[n.place_id] = (n.place_name, found ? c.lat : (double?)null, found ? c.lng : (double?)null);
            }
            return result;
        }

        #endregion


        #region 公車

        public class StopRow
        {
            public int bs_id { get; set; }
            public string stop_name { get; set; }
            public double lat { get; set; }
            public double lng { get; set; }
            public double distance_m { get; set; }
            public int route_count { get; set; }
        }

        public class DirectBusRow
        {
            public int brp_id { get; set; }
            public int br_id { get; set; }
            public string route_name { get; set; }
            public int route_type { get; set; }
            public string sub_route_name { get; set; }
            public string headsign { get; set; }
            public int board_bs_id { get; set; }
            public int board_seq { get; set; }
            public int alight_bs_id { get; set; }
            public int alight_seq { get; set; }
        }

        public class PatternStopRow
        {
            public int stop_sequence { get; set; }
            public string stop_name { get; set; }
            public double lat { get; set; }
            public double lng { get; set; }
        }

        private static object BoxParams(double lat, double lng, int radiusM, int limit)
        {
            double dLat = radiusM / 111000.0;
            double dLng = radiusM / (111000.0 * Math.Cos(lat * Math.PI / 180));
            return new { lat, lng, radiusM, limit, minLat = lat - dLat, maxLat = lat + dLat, minLng = lng - dLng, maxLng = lng + dLng };
        }

        /// <summary>座標附近的站牌（直線距離，由近到遠）</summary>
        public async Task<List<StopRow>> GetBusStopsNearAsync(double lat, double lng, int radiusM, int limit)
        {
            using var conn = Open();
            return (await conn.QueryAsync<StopRow>(@"
                SELECT bs_id, stop_name, bs_latitude AS lat, bs_longitude AS lng,
                       ST_Distance_Sphere(POINT(@lng, @lat), POINT(bs_longitude, bs_latitude)) AS distance_m
                FROM bus_stop
                WHERE bs_latitude BETWEEN @minLat AND @maxLat AND bs_longitude BETWEEN @minLng AND @maxLng
                HAVING distance_m <= @radiusM
                ORDER BY distance_m
                LIMIT @limit;", BoxParams(lat, lng, radiusM, limit))).ToList();
        }

        /// <summary>附近有營運中路線經過的站牌，依經過的路線數多到少（找建議起點用）</summary>
        public async Task<List<StopRow>> GetBusHubsNearAsync(double lat, double lng, int radiusM, int limit)
        {
            using var conn = Open();
            return (await conn.QueryAsync<StopRow>(@"
                SELECT bs.bs_id, bs.stop_name, bs.bs_latitude AS lat, bs.bs_longitude AS lng,
                       ST_Distance_Sphere(POINT(@lng, @lat), POINT(bs.bs_longitude, bs.bs_latitude)) AS distance_m,
                       COUNT(DISTINCT br.br_id) AS route_count
                FROM bus_stop bs
                INNER JOIN bus_route_stop brs ON brs.bs_id = bs.bs_id
                INNER JOIN bus_route_pattern brp ON brp.brp_id = brs.brp_id
                INNER JOIN bus_sub_route bsr ON bsr.bsr_id = brp.bsr_id
                INNER JOIN bus_route br ON br.br_id = bsr.br_id AND br.is_active = 1
                WHERE bs.bs_latitude BETWEEN @minLat AND @maxLat AND bs.bs_longitude BETWEEN @minLng AND @maxLng
                GROUP BY bs.bs_id, bs.stop_name, bs.bs_latitude, bs.bs_longitude
                HAVING distance_m <= @radiusM
                ORDER BY route_count DESC, distance_m
                LIMIT @limit;", BoxParams(lat, lng, radiusM, limit))).ToList();
        }

        /// <summary>從 boardStops 任一站上車、在 alightStops 任一站下車的直達路線（同一個行駛型態、下車站序較大）</summary>
        public async Task<List<DirectBusRow>> FindDirectBusAsync(List<int> boardStops, List<int> alightStops)
        {
            if (boardStops.Count == 0 || alightStops.Count == 0) return new List<DirectBusRow>();

            using var conn = Open();
            return (await conn.QueryAsync<DirectBusRow>(@"
                SELECT a.brp_id, br.br_id, br.route_name, br.route_type, bsr.sub_route_name, brp.headsign,
                       a.bs_id AS board_bs_id, a.stop_sequence AS board_seq,
                       b.bs_id AS alight_bs_id, b.stop_sequence AS alight_seq
                FROM bus_route_stop a
                INNER JOIN bus_route_stop b ON b.brp_id = a.brp_id AND b.stop_sequence > a.stop_sequence
                INNER JOIN bus_route_pattern brp ON brp.brp_id = a.brp_id
                INNER JOIN bus_sub_route bsr ON bsr.bsr_id = brp.bsr_id
                INNER JOIN bus_route br ON br.br_id = bsr.br_id AND br.is_active = 1
                WHERE a.bs_id IN @boardStops AND b.bs_id IN @alightStops;", new { boardStops, alightStops })).ToList();
        }

        /// <summary>行駛型態在兩個站序之間（含）經過的站牌</summary>
        public async Task<List<PatternStopRow>> GetPatternStopsAsync(int brpId, int fromSeq, int toSeq)
        {
            using var conn = Open();
            return (await conn.QueryAsync<PatternStopRow>(@"
                SELECT brs.stop_sequence, bs.stop_name, bs.bs_latitude AS lat, bs.bs_longitude AS lng
                FROM bus_route_stop brs INNER JOIN bus_stop bs ON bs.bs_id = brs.bs_id
                WHERE brs.brp_id = @brpId AND brs.stop_sequence BETWEEN @fromSeq AND @toSeq
                ORDER BY brs.stop_sequence;", new { brpId, fromSeq, toSeq })).ToList();
        }

        public async Task<string> GetPatternShapeAsync(int brpId)
        {
            using var conn = Open();
            return await conn.ExecuteScalarAsync<string>("SELECT geometry FROM bus_route_shape WHERE brp_id = @brpId;", new { brpId });
        }

        #endregion
    }
}
