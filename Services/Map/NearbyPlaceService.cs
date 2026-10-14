// 檔案路徑：System\Services\Map\NearbyPlaceService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    /// <summary>
    /// 周邊好去：依使用者「目前位置」動態推薦附近的地點，由近到遠。
    /// 不是進地圖就把整個縣市的地點列出來：沒有帶位置就不推薦，使用者走到哪裡、前端重新帶位置呼叫，就推薦那附近的。
    /// 1. 飲食：跟用餐時間推播同一份資料（NearbyFoodService：Neo4j 餐廳＋MySQL 美食，已知沒營業的不列）
    /// 2. 其他：Neo4j 的景點（Attraction）＋ MySQL place 裡美食以外的地點，重複的合併成一筆
    /// 3. 這份劇本自己的站一律排除：還在迷霧中的站不能從這裡洩漏，已經走過的站也不用再推薦
    /// </summary>
    public class NearbyPlaceService
    {
        public const string FoodCategory = "飲食";
        public const string OtherCategory = "其他";
        public const int DefaultLimit = 20;
        public const int MaxLimit = 50;

        private const double SameNameMergeMeters = 100;   // 同名、相距這個距離內的視為同一個地點（Neo4j 與 MySQL 重複）

        private readonly NearbyFoodService _food;
        private readonly Neo4jService _neo4j;
        private readonly NearbyFoodDao _dao;

        public NearbyPlaceService(NearbyFoodService food, Neo4jService neo4j, NearbyFoodDao dao)
        {
            _food = food;
            _neo4j = neo4j;
            _dao = dao;
        }

        /// <param name="category">「飲食」只要美食、「其他」只要美食以外的，不帶就兩種都要</param>
        public async Task<List<NearbyPlaceResponse>> GetNearbyAsync(int storyId, double? lat, double? lng, string category, int? radiusMeters, int? limit)
        {
            // 沒有位置就不推薦（周邊好去是跟著使用者走的）
            if (!lat.HasValue || !lng.HasValue) return new List<NearbyPlaceResponse>();

            if (lat < -90 || lat > 90 || lng < -180 || lng > 180 || (lat == 0 && lng == 0))
                throw new BadRequestException("請帶目前位置的經緯度（lat、lng）");

            int radius = Math.Clamp(radiusMeters ?? NearbyFoodService.DefaultRadiusMeters, NearbyFoodService.MinRadiusMeters, NearbyFoodService.MaxRadiusMeters);
            int take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            category = (category ?? "").Trim();

            var places = new List<NearbyPlaceResponse>();
            if (category != OtherCategory)
                places.AddRange(await FoodAsync(lat.Value, lng.Value, radius));
            if (category != FoodCategory)
                places.AddRange(await OthersAsync(lat.Value, lng.Value, radius));

            List<NearbyFoodDao.StoryPlaceRow> storyPlaces = await _dao.GetStoryPlacesAsync(storyId);

            return places
                .Where(p => !IsStoryPlace(p, storyPlaces))
                .OrderBy(p => p.distance_m)
                .Take(take)
                .ToList();
        }

        private async Task<List<NearbyPlaceResponse>> FoodAsync(double lat, double lng, int radius)
        {
            NearbyFoodResponse food = await _food.GetNearbyFoodAsync(lat, lng, radius, NearbyFoodService.MaxLimit);

            return food.places.Select(p => new NearbyPlaceResponse
            {
                place_id = PlaceIdOf(p.place_id),
                place_uid = p.place_id,
                category = FoodCategory,
                type = p.type,
                name = p.name,
                address = p.address,
                open_time = p.open_time,
                photo_urls = string.IsNullOrWhiteSpace(p.image_url) ? new List<string>() : new List<string> { p.image_url },
                maps_deeplink_url = p.maps_deeplink_url,
                distance_m = p.distance_m,
                is_open_now = p.is_open_now,
                lat = p.lat,
                lng = p.lng
            }).ToList();
        }

        private async Task<List<NearbyPlaceResponse>> OthersAsync(double lat, double lng, int radius)
        {
            DateTime now = DateTime.UtcNow.AddHours(8);   // 台灣時間
            double latDelta = radius / 111320.0;
            double lonDelta = radius / (111320.0 * Math.Cos(lat * Math.PI / 180));
            var bounds = (minLat: lat - latDelta, maxLat: lat + latDelta, minLon: lng - lonDelta, maxLon: lng + lonDelta);

            var places = new List<NearbyPlaceResponse>();

            List<NearbyFoodService.Neo4jFoodRow> fromNeo4j = await _neo4j.ExecuteCypherAsync<List<NearbyFoodService.Neo4jFoodRow>>(
                NearbyFoodService.NearbyQuery("Attraction"),
                new { lat, lng, radius_m = radius, bounds.minLat, bounds.maxLat, bounds.minLon, bounds.maxLon })
                ?? new List<NearbyFoodService.Neo4jFoodRow>();   // null 代表 Neo4j 連不上，只用 MySQL

            foreach (NearbyFoodService.Neo4jFoodRow r in fromNeo4j.Where(r => !string.IsNullOrWhiteSpace(r.name)))
            {
                var (open, todayText) = NearbyFoodService.OpenStatus(r.hours, now);
                places.Add(new NearbyPlaceResponse
                {
                    place_uid = r.uid,
                    category = OtherCategory,
                    name = r.name.Trim(),
                    address = r.address,
                    open_time = todayText,
                    photo_urls = string.IsNullOrWhiteSpace(r.image_url) ? new List<string>() : new List<string> { r.image_url },
                    maps_deeplink_url = MapsUrl(r.lat, r.lon),
                    distance_m = (int)Math.Round(r.distance_m),
                    is_open_now = open,
                    lat = r.lat,
                    lng = r.lon
                });
            }

            foreach (NearbyFoodDao.FoodPlaceRow r in await _dao.GetOtherPlacesInBoundsAsync(bounds.minLat, bounds.maxLat, bounds.minLon, bounds.maxLon))
            {
                double distance = Geo.DistanceMeters(lat, lng, r.lat, r.lng);
                if (distance > radius) continue;

                string uid = r.uid ?? $"place-{r.p_id}";
                string name = (r.p_name ?? "").Trim();

                NearbyPlaceResponse same = places.FirstOrDefault(p => p.place_uid == uid
                    || (NearbyFoodService.NormalizeName(p.name) == NearbyFoodService.NormalizeName(name)
                        && Geo.DistanceMeters(p.lat.Value, p.lng.Value, r.lat, r.lng) <= SameNameMergeMeters));

                if (same != null)
                {
                    // 重複的合併：Neo4j 沒有的資料用 MySQL 補上
                    same.place_id = r.p_id;
                    same.type ??= r.p_type;
                    same.address ??= r.p_address;
                    same.open_time ??= r.p_open_time;
                    if (same.photo_urls.Count == 0 && r.p_image != null) same.photo_urls.Add(r.p_image);
                    continue;
                }

                var (open, todayText) = NearbyFoodService.OpenStatus(r.p_open_time, now);
                places.Add(new NearbyPlaceResponse
                {
                    place_id = r.p_id,
                    place_uid = uid,
                    category = OtherCategory,
                    type = r.p_type,
                    name = name,
                    address = r.p_address,
                    open_time = todayText ?? r.p_open_time,
                    photo_urls = r.p_image == null ? new List<string>() : new List<string> { r.p_image },
                    maps_deeplink_url = MapsUrl(r.lat, r.lng),
                    distance_m = (int)Math.Round(distance),
                    is_open_now = open,
                    lat = r.lat,
                    lng = r.lng
                });
            }

            return places;
        }

        /// <summary>是不是這份劇本的站：Neo4j uid、MySQL p_id 或名稱對得上就算</summary>
        internal static bool IsStoryPlace(NearbyPlaceResponse place, List<NearbyFoodDao.StoryPlaceRow> storyPlaces) =>
            IsStoryPlace(place.place_uid, place.place_id, place.name, storyPlaces);

        /// <summary>用餐提醒推薦的店家也用同一套判斷（NearbyFoodPlace 的 place_id 就是這裡的 place_uid）</summary>
        internal static bool IsStoryPlace(NearbyFoodPlace place, List<NearbyFoodDao.StoryPlaceRow> storyPlaces) =>
            IsStoryPlace(place.place_id, PlaceIdOf(place.place_id), place.name, storyPlaces);

        private static bool IsStoryPlace(string placeUid, int placeId, string name, List<NearbyFoodDao.StoryPlaceRow> storyPlaces) =>
            storyPlaces.Any(s =>
                (s.uid != null && s.uid == placeUid)
                || (s.p_id.HasValue && placeId != 0 && s.p_id == placeId)
                || (!string.IsNullOrWhiteSpace(s.name) && NearbyFoodService.NormalizeName(s.name) == NearbyFoodService.NormalizeName(name)));

        /// <summary>只有 MySQL 資料的地點 place_uid 是 place-{p_id}，取出 p_id；Neo4j 的地點回傳 0</summary>
        private static int PlaceIdOf(string placeUid) =>
            placeUid != null && placeUid.StartsWith("place-") && int.TryParse(placeUid["place-".Length..], out int id) ? id : 0;

        private static string MapsUrl(double lat, double lng) =>
            $"https://www.google.com/maps/search/?api=1&query={lat.ToString(CultureInfo.InvariantCulture)},{lng.ToString(CultureInfo.InvariantCulture)}";
    }
}
