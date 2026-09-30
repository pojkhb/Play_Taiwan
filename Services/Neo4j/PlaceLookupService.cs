// 檔案路徑：System\Services\Neo4j\PlaceLookupService.cs
// 景點座標與基本資料查詢。劇本節點（story_node.place_id）存的是 Neo4j uid，
// 地圖、抵達、任務作答、導航都從這裡查同一份座標，避免各處來源不一致。
// 走 INeo4jGatewayService（依 Neo4j:Mode 直連或走 API），不再經過 MySQL place 表的名稱對應。
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using backend.utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.Services.Neo4j
{
    public class PlaceLookupService
    {
        // Neo4j 連不上時，行程規劃核心改用 MySQL place 表（StoryDao.GetPlacesInBoundsAsync），
        // 這些節點的 place_id 是 "place-{p_id}"，Neo4j 沒有這個 uid，只能回 place 表查。
        private const string MySqlPlacePrefix = "place-";

        private readonly INeo4jGatewayService _gateway;
        private readonly AppSettings _appSettings;

        public PlaceLookupService(INeo4jGatewayService gateway, IOptions<AppSettings> appSettings)
        {
            _gateway = gateway;
            _appSettings = appSettings.Value;
        }

        public class PlaceInfo
        {
            public string uid { get; set; }
            public string name { get; set; }
            public double lat { get; set; }
            public double lng { get; set; }
            public string description { get; set; }
        }

        /// <summary>
        /// 一次查多個景點（key = place_id）。查不到或沒有座標的景點不會出現在結果裡。
        /// 商家補充過的景點以最新版本（:Current）的名稱、介紹為準；座標一律取身分節點。
        /// </summary>
        public async Task<Dictionary<string, PlaceInfo>> GetPlacesAsync(IEnumerable<string> placeIds)
        {
            List<string> ids = (placeIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            var result = new Dictionary<string, PlaceInfo>();

            List<string> uids = ids.Where(id => !id.StartsWith(MySqlPlacePrefix)).ToList();
            if (uids.Count > 0)
            {
                var rows = await _gateway.ExecuteCypherAsync(@"
                    MATCH (p:Place) WHERE p.uid IN $uids
                    OPTIONAL MATCH (p)-[:HAS_VERSION]->(v:Current)
                    RETURN p.uid AS uid,
                           coalesce(v.name, p.name, p.EventName) AS name,
                           coalesce(p.lat, p.PositionLat) AS lat,
                           coalesce(p.lon, p.PositionLon) AS lon,
                           coalesce(v.description, p.description, p.Description) AS description", new { uids });

                foreach (var row in rows)
                {
                    PlaceInfo place = ToPlaceInfo(row);
                    if (place != null) result[place.uid] = place;
                }
            }

            List<int> pIds = ids
                .Where(id => id.StartsWith(MySqlPlacePrefix))
                .Select(id => int.TryParse(id.Substring(MySqlPlacePrefix.Length), out int pId) ? pId : 0)
                .Where(pId => pId > 0)
                .ToList();
            if (pIds.Count > 0)
            {
                using var conn = new MySqlConnection(_appSettings.mydb);
                var rows = await conn.QueryAsync<(int p_id, string p_name, double lat, double lng, string p_introduction)>(@"
                    SELECT p_id, p_name, p_latitude AS lat, p_longitude AS lng, p_introduction
                    FROM place
                    WHERE p_id IN @pIds AND p_latitude IS NOT NULL AND p_longitude IS NOT NULL;", new { pIds });

                foreach (var r in rows)
                {
                    string id = MySqlPlacePrefix + r.p_id;
                    result[id] = new PlaceInfo { uid = id, name = r.p_name, lat = r.lat, lng = r.lng, description = r.p_introduction };
                }
            }

            return result;
        }

        /// <summary>單一景點，查不到或沒有座標時回傳 null。</summary>
        public async Task<PlaceInfo> GetPlaceAsync(string placeId)
        {
            if (string.IsNullOrWhiteSpace(placeId)) return null;
            return (await GetPlacesAsync(new[] { placeId })).GetValueOrDefault(placeId);
        }

        /// <summary>
        /// 指定 uid 中，座標落在範圍內的景點（行程規劃補商家景點用）。
        /// 已刪除帳號的商家不在 uid 清單中（呼叫端傳現有商家的 store_uid），不會被查到。
        /// </summary>
        public async Task<List<PlaceInfo>> GetPlacesInBoundsAsync(IEnumerable<string> uids, double minLat, double maxLat, double minLon, double maxLon)
        {
            List<string> ids = (uids ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
            if (ids.Count == 0) return new List<PlaceInfo>();

            var rows = await _gateway.ExecuteCypherAsync(@"
                MATCH (p:Place) WHERE p.uid IN $ids
                WITH p, coalesce(p.lat, p.PositionLat) AS lat, coalesce(p.lon, p.PositionLon) AS lon
                WHERE lat >= $minLat AND lat <= $maxLat AND lon >= $minLon AND lon <= $maxLon
                OPTIONAL MATCH (p)-[:HAS_VERSION]->(v:Current)
                RETURN p.uid AS uid,
                       coalesce(v.name, p.name, p.EventName) AS name,
                       lat, lon,
                       coalesce(v.description, p.description, p.Description) AS description",
                new { ids, minLat, maxLat, minLon, maxLon });

            return rows.Select(ToPlaceInfo).Where(p => p != null).ToList();
        }

        public class PlaceDistrict
        {
            public string city_name { get; set; }
            public string district_name { get; set; }
        }

        // 找最近景點的範圍（經緯度各 ±0.03 度，約 3 公里）
        private const double NearestDistrictSearchDegrees = 0.03;

        /// <summary>
        /// 景點所屬的縣市／鄉鎮市區（key = place_id）：用 (Place)-[:LOCATED_IN_TOWN]->(Town)-[:PART_OF]->(City)；
        /// 沒掛行政區的景點（約 1000 筆政府資料缺漏、改版前建立的商家自建景點）改取座標附近最近一個有行政區的景點。
        /// MySQL place 表的景點（place-{p_id}）、查不到或附近沒有景點的不會出現在結果裡。
        /// </summary>
        public async Task<Dictionary<string, PlaceDistrict>> GetPlaceDistrictsAsync(IEnumerable<string> placeIds)
        {
            List<string> uids = (placeIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith(MySqlPlacePrefix))
                .Distinct()
                .ToList();

            var result = new Dictionary<string, PlaceDistrict>();
            if (uids.Count == 0) return result;

            var rows = await _gateway.ExecuteCypherAsync(@"
                MATCH (p:Place) WHERE p.uid IN $uids
                OPTIONAL MATCH (p)-[:LOCATED_IN_TOWN]->(t:Town)
                OPTIONAL MATCH (t)-[:PART_OF]->(c:City)
                WITH p, head(collect(t)) AS townNode, head(collect(c.name)) AS city
                RETURN p.uid AS uid,
                       townNode.name AS town, townNode.id AS town_id, city,
                       coalesce(p.lat, p.PositionLat) AS lat,
                       coalesce(p.lon, p.PositionLon) AS lon", new { uids });

            var withoutTown = new List<Dictionary<string, object>>();

            foreach (var row in rows)
            {
                string uid = Neo4jValueConverter.AsString(row.GetValueOrDefault("uid"));
                if (string.IsNullOrWhiteSpace(uid)) continue;

                PlaceDistrict district = ToPlaceDistrict(row);
                if (district != null)
                {
                    result[uid] = district;
                    continue;
                }

                double? lat = Neo4jValueConverter.AsDouble(row.GetValueOrDefault("lat"));
                double? lng = Neo4jValueConverter.AsDouble(row.GetValueOrDefault("lon"));
                if (lat.HasValue && lng.HasValue)
                    withoutTown.Add(new Dictionary<string, object> { ["key"] = uid, ["lat"] = lat.Value, ["lng"] = lng.Value });
            }

            foreach (var (uid, district) in await GetNearestDistrictsAsync(withoutTown))
                result[uid] = district;

            return result;
        }

        /// <summary>座標所在的縣市／鄉鎮市區：取附近最近一個有行政區的景點，附近沒有景點時回傳 null。</summary>
        public async Task<PlaceDistrict> GetDistrictByCoordinatesAsync(double lat, double lng)
        {
            var points = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { ["key"] = "here", ["lat"] = lat, ["lng"] = lng }
            };
            return (await GetNearestDistrictsAsync(points)).GetValueOrDefault("here");
        }

        /// <summary>每個座標（key、lat、lng）各自取附近最近一個有行政區的景點的縣市／鄉鎮市區。</summary>
        private async Task<Dictionary<string, PlaceDistrict>> GetNearestDistrictsAsync(List<Dictionary<string, object>> points)
        {
            var result = new Dictionary<string, PlaceDistrict>();
            if (points.Count == 0) return result;

            var rows = await _gateway.ExecuteCypherAsync(@"
                UNWIND $points AS pt
                CALL {
                    WITH pt
                    MATCH (q:Place)-[:LOCATED_IN_TOWN]->(t:Town)
                    WITH pt, t, coalesce(q.lat, q.PositionLat) AS qLat, coalesce(q.lon, q.PositionLon) AS qLon
                    WHERE qLat >= pt.lat - $delta AND qLat <= pt.lat + $delta
                      AND qLon >= pt.lng - $delta AND qLon <= pt.lng + $delta
                    WITH t, point.distance(point({latitude: pt.lat, longitude: pt.lng}),
                                           point({latitude: qLat, longitude: qLon})) AS distance_m
                    ORDER BY distance_m ASC
                    LIMIT 1
                    OPTIONAL MATCH (t)-[:PART_OF]->(c:City)
                    RETURN t.name AS town, t.id AS town_id, c.name AS city
                }
                RETURN pt.key AS key, town, town_id, city", new { points, delta = NearestDistrictSearchDegrees });

            foreach (var row in rows)
            {
                string key = Neo4jValueConverter.AsString(row.GetValueOrDefault("key"));
                PlaceDistrict district = ToPlaceDistrict(row);
                if (!string.IsNullOrWhiteSpace(key) && district != null) result[key] = district;
            }

            return result;
        }

        /// <summary>Town.id 是「縣市_鄉鎮市區」，Town 沒掛 City 時從 id 取縣市</summary>
        private static PlaceDistrict ToPlaceDistrict(Dictionary<string, object> row)
        {
            string town = Neo4jValueConverter.AsString(row.GetValueOrDefault("town"));
            if (string.IsNullOrWhiteSpace(town)) return null;

            string city = Neo4jValueConverter.AsString(row.GetValueOrDefault("city"));
            if (string.IsNullOrWhiteSpace(city))
            {
                string townId = Neo4jValueConverter.AsString(row.GetValueOrDefault("town_id")) ?? "";
                int split = townId.IndexOf('_');
                city = split > 0 ? townId.Substring(0, split) : null;
            }

            return new PlaceDistrict { city_name = city, district_name = town };
        }

        private static PlaceInfo ToPlaceInfo(Dictionary<string, object> row)
        {
            string uid = Neo4jValueConverter.AsString(row.GetValueOrDefault("uid"));
            double? lat = Neo4jValueConverter.AsDouble(row.GetValueOrDefault("lat"));
            double? lng = Neo4jValueConverter.AsDouble(row.GetValueOrDefault("lon"));

            if (string.IsNullOrWhiteSpace(uid) || !lat.HasValue || !lng.HasValue) return null;

            return new PlaceInfo
            {
                uid = uid,
                name = Neo4jValueConverter.AsString(row.GetValueOrDefault("name")),
                lat = lat.Value,
                lng = lng.Value,
                description = Neo4jValueConverter.AsString(row.GetValueOrDefault("description"))
            };
        }
    }
}
