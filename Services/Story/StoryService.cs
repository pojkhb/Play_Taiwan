// 檔案路徑：System\Services\Story\StoryService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using backend.dao;
using backend.Models;
using backend.ViewModels;
using backend.utils;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;


namespace backend.Services
{
    public class StoryService
    {
        private readonly StoryDao _dao;
        private readonly Neo4jService _neo4jService;
        private readonly ValhallaService _valhallaService;
        private readonly IHttpClientFactory _httpClientFactory;


        public StoryService(StoryDao dao, Neo4jService neo4jService, ValhallaService valhallaService, IHttpClientFactory httpClientFactory)
        {
            _dao = dao;
            _neo4jService = neo4jService;
            _valhallaService = valhallaService;
            _httpClientFactory = httpClientFactory;
        }


        public StoryWheelSpinResponse WheelSpin()
        {
            return _dao.WheelSpin();
        }


        public List<StoryWheelSpinResponse> GetRegions(string mode, string cityName)
        {
            return _dao.GetRegions(mode, cityName);
        }


        public List<StoryOptionResponse> GenerateOptions(StoryGenerateRequest req)
        {
            return _dao.GenerateStories(req);
        }


        public StoryDetailResponse GetDetail(int storyId)
        {
            return _dao.GetDetail(storyId);
        }


        // === 變更重點：原本只讀不寫，現在會把該劇本標記為「正在遊玩中」 ===
        public StoryDetailResponse ConfirmStory(int auId, StoryConfirmRequest req)
        {
            if (req == null || req.story_id <= 0)
                throw new Exception("請提供 story_id");

            bool success = _dao.SetStoryPlaying(auId, req.story_id);
            if (!success)
                throw new Exception($"找不到 story_id = {req.story_id} 的劇本");

            return _dao.GetDetail(req.story_id);
        }

        // === 新增：玩家結束/退出劇本時呼叫 ===
        public bool EndStory(int auId, int storyId)
        {
            if (storyId <= 0)
                throw new Exception("請提供 story_id");

            return _dao.ClearStoryPlaying(auId, storyId);
        }

        // === 新增：查目前哪個劇本正在進行中 ===
        public CurrentPlayingStory GetCurrentPlayingStory(int auId)
        {
            return _dao.GetCurrentPlayingStory(auId);
        }


        #region GPS 定位生成劇本 相關方法


        public ScriptBlueprintData GetFullDetail(int storyId)
        {
            return _dao.GetFullDetail(storyId);
        }


        public async Task<int> SaveFullAiGeneratedStory(int auId, string cityName, string districtName, ScriptBlueprintData data)
        {
            return await _dao.SaveFullAiGeneratedStory(auId, cityName, districtName, data);
        }


        public string FindRegionIdByName(string cityName, string townName)
        {
            return _dao.FindRegionIdByName(cityName, townName);
        }


        public List<NearbyPlaceDistanceResponse> GetNearbyPlacesByDistance(double lat, double lng, double radiusKm)
        {
            return _dao.GetNearbyPlacesByDistance(lat, lng, radiusKm);
        }


        #endregion


        #region Neo4j 附近景點查詢


        /// <summary>
        /// 依使用者 GPS 座標，透過 Neo4j 查詢半徑範圍內的景點（依距離由近到遠排序，已去除重複節點）。
        /// 直線半徑版本，保留給不需要交通方式的地方使用；要依交通方式與真實時間請用 GetReachableAttractionsAsync。
        /// </summary>
        public async Task<List<NearbyAttractionNode>> GetNearbyAttractionsAsync(double lat, double lng, double radiusKm)
        {
            string cypherQuery = @"
                MATCH (a:Attraction)
                WHERE a.lat IS NOT NULL AND a.lon IS NOT NULL
                WITH a, point({latitude: $lat, longitude: $lng}) AS origin, point({latitude: a.lat, longitude: a.lon}) AS aPoint
                WITH a, point.distance(origin, aPoint) AS distance_m
                WHERE distance_m <= $radius_m
                RETURN DISTINCT a.name AS name, a.lat AS lat, a.lon AS lon, distance_m
                ORDER BY distance_m ASC
            ";


            var parameters = new { lat = lat, lng = lng, radius_m = radiusKm * 1000 };


            var result = await _neo4jService.ExecuteCypherAsync<List<NearbyAttractionNode>>(cypherQuery, parameters);
            return result ?? new List<NearbyAttractionNode>();
        }


        #endregion


        #region 交通等時圈 + 真實交通時間（Valhalla + Neo4j）


        /// <summary>
        /// 依交通方式找出可到達的景點，並算出每個景點的真實交通時間。
        /// 1. 等時圈：每種交通方式各打一次 Valhalla isochrone，得到 N 分鐘內可到達的範圍
        /// 2. 粗篩：用等時圈外框在 Neo4j 撈候選景點，再逐一判斷是否落在圈內
        /// 3. 真實時間：用 Valhalla 時間矩陣一次算出中心點到所有候選景點的實際時間與距離，
        ///    多種交通方式時每個景點取最快的那種，圈層也依真實時間決定
        /// 4. 挑選：範圍內隨機挑 8 個（各圈層輪流抽，遠近都有），每次呼叫結果都不同
        /// 5. 排順序：從中心點出發排成順路的參觀順序
        /// 這裡只做 1~3，回傳範圍內所有景點；4、5 由 GetReachableAttractionsAsync / 劇本生成各自處理。
        /// </summary>
        private async Task<ReachableAttractionsResponse> FindAllReachableAttractionsAsync(ReachableAttractionsRequest req)
        {
            // Valhalla 預設最多 4 個圈、每圈最多 120 分鐘
            List<int> minutes = (req.contour_minutes ?? new List<int>())
                .Where(m => m > 0 && m <= 120)
                .Distinct()
                .OrderBy(m => m)
                .Take(4)
                .ToList();

            if (minutes.Count == 0)
                minutes = new List<int> { 10, 20, 30 };

            var costingGroups = GroupTransports(req.transportation);

            // ── 1. 等時圈 ──
            List<IsochroneBand>[] bandLists = await Task.WhenAll(costingGroups.Select(async group =>
            {
                List<IsochroneBand> bands = await _valhallaService.GetIsochroneAsync(req.lat, req.lng, group.costing, minutes);
                bands.ForEach(b => b.transport = group.label);
                return bands;
            }));

            List<IsochroneBand> allBands = bandLists.SelectMany(b => b).ToList();

            var response = new ReachableAttractionsResponse
            {
                center_lat = req.lat,
                center_lng = req.lng,
                contour_minutes = minutes,
                attractions = new List<ReachableAttractionNode>(),
                isochrones = req.include_polygons ? allBands : null
            };

            if (allBands.Count == 0)
                return response;

            // ── 2. 用外框在 Neo4j 粗篩，再判斷是否在圈內 ──
            var (minLat, maxLat, minLon, maxLon) = ValhallaService.GetBoundingBox(allBands);

            string cypherQuery = @"
                MATCH (a:Attraction)
                WHERE a.lat IS NOT NULL AND a.lon IS NOT NULL
                  AND a.lat >= $minLat AND a.lat <= $maxLat
                  AND a.lon >= $minLon AND a.lon <= $maxLon
                WITH a, point.distance(
                        point({latitude: $lat, longitude: $lng}),
                        point({latitude: a.lat, longitude: a.lon})) AS distance_m
                RETURN DISTINCT a.uid AS uid, a.name AS name, a.lat AS lat, a.lon AS lon, distance_m
                ORDER BY distance_m ASC
            ";

            var parameters = new { lat = req.lat, lng = req.lng, minLat, maxLat, minLon, maxLon };

            List<ReachableAttractionNode> candidates =
                await _neo4jService.ExecuteCypherAsync<List<ReachableAttractionNode>>(cypherQuery, parameters)
                ?? new List<ReachableAttractionNode>();

            // Neo4j（AI service）連不上或沒資料時，改用 MySQL place 表的景點
            if (candidates.Count == 0)
                candidates = await _dao.GetPlacesInBoundsAsync(req.lat, req.lng, minLat, maxLat, minLon, maxLon);

            List<ReachableAttractionNode> inside = RemoveDuplicateAttractions(candidates)
                .Where(a => allBands.Any(b => ValhallaService.Contains(b, a.lat, a.lon)))
                .ToList();

            if (inside.Count == 0)
                return response;

            // ── 3. 真實交通時間 ──
            var targets = inside.Select(a => (lat: a.lat, lng: a.lon)).ToList();

            List<(double? seconds, double? km)>[] timeResults = await Task.WhenAll(
                costingGroups.Select(group => _valhallaService.GetTravelTimesAsync(req.lat, req.lng, targets, group.costing)));

            for (int i = 0; i < inside.Count; i++)
            {
                double? bestSeconds = null;
                double? bestKm = null;
                string bestBy = null;

                for (int k = 0; k < costingGroups.Count; k++)
                {
                    var (seconds, km) = timeResults[k][i];
                    if (seconds.HasValue && (!bestSeconds.HasValue || seconds.Value < bestSeconds.Value))
                    {
                        bestSeconds = seconds;
                        bestKm = km;
                        bestBy = costingGroups[k].label;
                    }
                }

                // 在圈內但路網上實際到不了（例如在河對岸），略過
                if (!bestSeconds.HasValue) continue;

                ReachableAttractionNode attraction = inside[i];
                attraction.travel_minutes = Math.Round(bestSeconds.Value / 60.0, 1);
                attraction.travel_distance_km = Math.Round(bestKm ?? 0, 2);
                attraction.reachable_by = bestBy;

                // 圈層依真實時間決定；稍微超過最外圈（等時圈邊界簡化造成）就算最外圈
                int ringIndex = minutes.FindIndex(m => attraction.travel_minutes <= m);
                attraction.ring = ringIndex >= 0 ? ringIndex + 1 : minutes.Count;
                attraction.reachable_minutes = minutes[attraction.ring - 1];

                response.attractions.Add(attraction);
            }

            response.total_reachable = response.attractions.Count;
            return response;
        }


        /// <summary>可到達的景點：範圍內各圈層輪流隨機抽 8 個，再排成順路的參觀順序</summary>
        public async Task<ReachableAttractionsResponse> GetReachableAttractionsAsync(ReachableAttractionsRequest req)
        {
            ReachableAttractionsResponse response = await FindAllReachableAttractionsAsync(req);
            response.attractions = OrderByVisit(PickRandomAcrossRings(response.attractions, AttractionCount), req.lat, req.lng);
            return response;
        }


        // 推薦景點數（一律以整天行程計算）
        private const int AttractionCount = 8;

        // 同名景點在這個距離內視為同一個；不同名但幾乎同座標（例如「臺中驛鐵道文化園區」與「臺中火車站ˍ舊站」）也視為同一個
        private const double SameNameMergeMeters = 300;
        private const double SameSpotMergeMeters = 30;


        /// <summary>
        /// 去除重複景點。Neo4j 內同一景點常有多個節點（不同 uid、座標差一點點），
        /// 候選清單已依離中心點距離排序，保留最先出現的那一筆。
        /// </summary>
        private static List<ReachableAttractionNode> RemoveDuplicateAttractions(List<ReachableAttractionNode> candidates)
        {
            var kept = new List<ReachableAttractionNode>();

            foreach (ReachableAttractionNode a in candidates)
            {
                string name = NormalizeName(a.name);

                bool duplicate = kept.Any(k =>
                {
                    double meters = DistanceMeters(k.lat, k.lon, a.lat, a.lon);
                    return meters <= SameSpotMergeMeters
                        || (meters <= SameNameMergeMeters && NormalizeName(k.name) == name);
                });

                if (!duplicate)
                    kept.Add(a);
            }

            return kept;
        }


        /// <summary>
        /// 各圈層輪流隨機抽景點，讓推薦結果近、中、遠都有，且每次生成的景點組合都不同。
        /// 不直接整體隨機，是因為外圈面積大、景點多，整體隨機幾乎都會抽到外圈。
        /// </summary>
        private static List<ReachableAttractionNode> PickRandomAcrossRings(List<ReachableAttractionNode> attractions, int maxCount)
        {
            List<Queue<ReachableAttractionNode>> rings = attractions
                .GroupBy(a => a.ring)
                .OrderBy(g => g.Key)
                .Select(g => new Queue<ReachableAttractionNode>(g.OrderBy(_ => Random.Shared.Next())))
                .ToList();

            var picked = new List<ReachableAttractionNode>();

            while (picked.Count < maxCount && rings.Any(q => q.Count > 0))
            {
                foreach (var ring in rings.Where(q => q.Count > 0))
                {
                    if (picked.Count >= maxCount) break;
                    picked.Add(ring.Dequeue());
                }
            }

            return picked;
        }


        private static string NormalizeName(string name)
        {
            return (name ?? "").Replace(" ", "").Replace("台", "臺").Trim().ToLowerInvariant();
        }


        /// <summary>兩點球面距離（公尺）</summary>
        private static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000;
            double dLat = (lat2 - lat1) * Math.PI / 180;
            double dLon = (lon2 - lon1) * Math.PI / 180;
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                     * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * R * Math.Asin(Math.Sqrt(h));
        }


        /// <summary>
        /// 依序經過多個點的實際路線。多種交通方式時，每一段各自挑最快的那一種。
        /// </summary>
        public async Task<TravelRouteResponse> GetTravelRouteAsync(TravelRouteRequest req)
        {
            List<TravelRoutePoint> points = (req?.points ?? new List<TravelRoutePoint>())
                .Where(p => p != null && p.lat != 0 && p.lng != 0)
                .ToList();

            if (points.Count < 2)
                throw new Exception("路線至少需要 2 個點（起點與終點）");

            var coords = points.Select(p => (lat: p.lat, lng: p.lng)).ToList();
            var costingGroups = GroupTransports(req.transportation);

            // 每種交通方式各算一次整條路線；某種方式找不到路（例如公車到不了）就略過
            var attempts = await Task.WhenAll(costingGroups.Select(async group =>
            {
                try
                {
                    ValhallaRoute route = await _valhallaService.GetRouteAsync(coords, group.costing);
                    return (label: group.label, route: route, error: (string)null);
                }
                catch (Exception ex)
                {
                    return (label: group.label, route: (ValhallaRoute)null, error: ex.Message);
                }
            }));

            var routes = attempts.Where(a => a.route != null).ToList();

            if (routes.Count == 0)
                throw new Exception("找不到可行的路線，請確認起點與終點附近有道路。" + attempts.First().error);

            var response = new TravelRouteResponse { legs = new List<TravelRouteLeg>() };

            for (int i = 0; i < points.Count - 1; i++)
            {
                var best = routes
                    .Where(r => r.route.legs.Count > i)
                    .OrderBy(r => r.route.legs[i].seconds)
                    .FirstOrDefault();

                if (best.route == null) continue;

                ValhallaRouteLeg leg = best.route.legs[i];

                response.legs.Add(new TravelRouteLeg
                {
                    leg_order = i + 1,
                    from_name = points[i].name,
                    to_name = points[i + 1].name,
                    transport = best.label,
                    travel_minutes = Math.Round(leg.seconds / 60.0, 1),
                    distance_km = Math.Round(leg.km, 2),
                    coordinates = leg.coordinates
                });
            }

            response.total_minutes = Math.Round(response.legs.Sum(l => l.travel_minutes), 1);
            response.total_distance_km = Math.Round(response.legs.Sum(l => l.distance_km), 2);

            return response;
        }


        /// <summary>
        /// 把前端交通方式分組成 Valhalla costing。
        /// 公車/捷運不列入（沒有等車、轉乘時間，算出來會偏短）；剔除後沒有任何交通方式時預設步行。
        /// </summary>
        private static List<(string costing, string label)> GroupTransports(List<string> transportation)
        {
            List<string> transports = (transportation ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Where(t => !ExcludedTransports.Contains(t))
                .Distinct()
                .ToList();

            if (transports.Count == 0)
                transports.Add("步行");

            return transports
                .GroupBy(ValhallaService.ToCosting)
                .Select(g => (costing: g.Key, label: string.Join("/", g)))
                .ToList();
        }


        private static readonly HashSet<string> ExcludedTransports = new HashSet<string> { "公車", "捷運" };


        #endregion


        #region 劇本任務一次生成（AI service /api/v1/generate）

        private const int CoopTaskTypeId = 5;           // 協作解謎型，至少 2 人才出
        private const int MerchantQuizTypeId = 9;       // 商家知識問答，商家後台維護，不給 AI 生成

        // 選了這些交通方式時，才規劃節點之間的公車
        private static readonly HashSet<string> BusTransports = new HashSet<string> { "公車", "客運", "台灣好行" };

        // place_type 查不到任何設定時的預設任務類型：創意攝影型、文化問答型、協作解謎型
        private static readonly int[] DefaultTaskTypeIds = { 3, 6, 5 };
        private static readonly Dictionary<int, string> FallbackTypeNames = new Dictionary<int, string>
        {
            { 1, "GPS 區域定位型" }, { 2, "跨關集結型" }, { 3, "創意攝影型" }, { 4, "地方美食型" },
            { 5, "協作解謎型" }, { 6, "文化問答型" }, { 7, "景點猜猜樂" }, { 8, "e人訪談型" }
        };


        // 一次生成幾份劇本讓使用者挑
        private const int StoryCount = 3;

        // narrative_tone 沒有資料時用的預設敘事語氣
        private static readonly string[] DefaultNarrativeTones = { "溫情走心", "幽默詼諧", "懸疑推理" };


        /// <summary>
        /// 劇本 + 任務一次生成（一次 3 份讓使用者挑）：
        /// 1. 用交通等時圈找出範圍內所有景點（已去重複、不含公車），隨機抽 3 組、每組 8 個，
        ///    景點夠多時各組不重複，每組各自排好順路的參觀順序
        /// 2. 每組配一個敘事語氣；每個景點從 place_type 查可出的任務類型組成 type_list
        /// 3. 打 AI service /api/v1/generate，一次拿回 3 份劇本
        /// 4. 後端補上 AI 不回傳的欄位，全部寫入 story、story_tag、story_node、task、task_option、task_clue（同一個交易）
        /// 呼叫前 req.lat/lng、city_name、town_name 必須已由 Controller 補齊。
        /// </summary>
        public async Task<List<GameStoryResult>> GenerateGameStoryAsync(int auId, GameStoryGenerateRequest req)
        {
            int partySize = req.party_size > 0 ? req.party_size : 2;
            int isNightMode = req.is_night_mode == 1 ? 1 : 2;

            // ── 1. 挑 3 組景點 ──
            ReachableAttractionsResponse reachable = await FindAllReachableAttractionsAsync(new ReachableAttractionsRequest
            {
                lat = req.lat,
                lng = req.lng,
                transportation = req.transportation
            });

            if (reachable.attractions.Count == 0)
                throw new Exception("附近找不到可到達的景點，請換個地點或交通方式再試一次");

            List<List<ReachableAttractionNode>> placeSets = PickPlaceSets(reachable.attractions, StoryCount, AttractionCount)
                .Select(set => OrderByVisit(set, req.lat, req.lng))
                .ToList();

            // ── 2. 敘事語氣、每個景點的任務類型 ──
            List<string> tones = await PickNarrativeTonesAsync(placeSets.Count);

            List<string> placeIds = placeSets.SelectMany(s => s).Select(a => a.uid).Distinct().ToList();
            List<StoryDao.PlaceTaskTypeRow> typeRows = await _dao.GetPlaceTaskTypesAsync(placeIds);
            Dictionary<int, string> typeNames = await _dao.GetTaskTypeNamesAsync();

            string TypeName(int id) => typeNames.TryGetValue(id, out string name) ? name
                                     : FallbackTypeNames.TryGetValue(id, out string fallback) ? fallback : "";

            AiGamePlace ToAiPlace(ReachableAttractionNode a)
            {
                List<StoryDao.PlaceTaskTypeRow> rows = typeRows.Where(r => r.place_id == a.uid).ToList();
                string category = rows.Select(r => r.place_category).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "Attraction";

                IEnumerable<int> typeIds = rows.Count > 0 ? rows.Select(r => r.type_id) : DefaultTaskTypeIds;

                return new AiGamePlace
                {
                    place_id = a.uid,
                    p_name = a.name,
                    is_hotel = category == "Lodging" || category == "Hotel" ? 1 : 2,
                    is_hidden = 2,
                    type_list = typeIds
                        .Distinct()
                        .Where(id => id != MerchantQuizTypeId)
                        .Where(id => id != CoopTaskTypeId || partySize >= 2)
                        .Select(id => new AiTaskTypeRef { type_id = id, type_name = TypeName(id) })
                        .ToList()
                };
            }

            // places 已排成順路的參觀順序，AI 照陣列順序排 sn_order
            List<AiStoryCondition> conditions = placeSets.Select((set, i) => new AiStoryCondition
            {
                story_no = i + 1,
                nt_name = tones[i],
                places = set.Select(ToAiPlace).ToList()
            }).ToList();

            var aiRequest = new AiGameStoryRequest
            {
                city_name = req.city_name ?? "",
                district_name = req.town_name ?? "",
                party_size = partySize,
                s_tag = req.preferences ?? new List<string>(),
                is_night_mode = isNightMode,
                stories = conditions
            };

            // ── 3. 打 AI service ──
            HttpClient client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            var content = new StringContent(JsonSerializer.Serialize(aiRequest), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.PostAsync(AiServiceConfig.Url("/api/v1/generate"), content);
            string responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"AI 劇本任務生成服務回應錯誤 (Status: {(int)response.StatusCode}): {responseString}");

            AiGameStoryResponse aiResponse;
            try
            {
                aiResponse = JsonSerializer.Deserialize<AiGameStoryResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                throw new Exception($"AI 劇本任務回傳格式無法解析: {ex.Message}");
            }

            // ── 4. 補上 AI 不回傳的欄位 ──
            List<string> transport = (req.transportation ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct()
                .ToList();
            if (transport.Count == 0) transport.Add("步行");

            bool planBusTransit = transport.Any(t => BusTransports.Contains(t));

            var results = new List<GameStoryResult>();

            foreach (AiStoryCondition condition in conditions)
            {
                AiGeneratedStory generated = aiResponse?.stories?.FirstOrDefault(s => s.story_no == condition.story_no);
                if (generated?.story == null || generated.nodes == null || generated.nodes.Count == 0)
                    continue;   // AI 漏掉的那份就不存，其他份照常

                // 節點順序以後端送出的 places 為準（規格要求 AI 不可重排），對不到的節點丟掉
                List<string> order = condition.places.Select(p => p.place_id).ToList();
                List<GameStoryNode> nodes = generated.nodes
                    .Where(n => order.Contains(n.place_id))
                    .GroupBy(n => n.place_id)
                    .Select(g => g.First())
                    .OrderBy(n => order.IndexOf(n.place_id))
                    .ToList();

                if (nodes.Count == 0) continue;

                for (int i = 0; i < nodes.Count; i++)
                {
                    GameStoryNode node = nodes[i];
                    AiGamePlace place = condition.places.First(p => p.place_id == node.place_id);

                    node.sn_order = i + 1;
                    node.p_name = place.p_name;
                    node.is_hidden = place.is_hidden;
                    node.is_night_only = isNightMode == 1 ? 1 : 0;
                    node.npc_id = null;
                    node.tasks = node.tasks ?? new List<GameStoryTask>();

                    foreach (GameStoryTask task in node.tasks)
                        task.type_name = TypeName(task.task_type);

                    node.sn_task_type = string.Join(",", node.tasks.Select(t => t.type_name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());
                }

                GameStoryInfo story = generated.story;
                story.city_name = aiRequest.city_name;
                story.district_name = aiRequest.district_name;
                story.party_size = partySize;
                story.sd_transport = transport;
                story.is_night_mode = isNightMode;
                story.story_postcards = nodes.Count;
                story.story_badge = story.story_badge ?? new List<string>();

                results.Add(new GameStoryResult
                {
                    story_no = condition.story_no,
                    nt_name = condition.nt_name,
                    story = story,
                    nodes = nodes
                });
            }

            if (results.Count == 0)
                throw new Exception("AI 劇本任務回傳內容為空");

            // ── 5. 寫入資料庫（回填 story_id、sn_id、task_db_id；有選公車時一併寫入節點間的直達公車）──
            await _dao.SaveGameStoriesAsync(auId, req.preferences, results, planBusTransit);

            // ── 6. 把公車方案（含經過的站牌）掛回每個節點 ──
            foreach (GameStoryResult result in results)
            {
                List<BusTransitLeg> legs = planBusTransit
                    ? await _dao.GetStoryTransitAsync(result.story_id)
                    : new List<BusTransitLeg>();

                foreach (GameStoryNode node in result.nodes)
                {
                    node.transit = legs.Where(l => l.to_sn_id == node.sn_id).OrderBy(l => l.leg_order).ToList();
                }
            }

            return results;
        }


        /// <summary>
        /// 從範圍內所有景點抽出多組景點，每組各圈層輪流隨機抽。
        /// 景點夠多時各組互不重複；不夠時才允許跟前面的組重複，讓每組都盡量湊滿。
        /// </summary>
        private static List<List<ReachableAttractionNode>> PickPlaceSets(List<ReachableAttractionNode> all, int setCount, int perSet)
        {
            var sets = new List<List<ReachableAttractionNode>>();
            var used = new HashSet<string>();

            for (int i = 0; i < setCount; i++)
            {
                List<ReachableAttractionNode> unused = all.Where(a => !used.Contains(a.uid)).ToList();

                List<ReachableAttractionNode> set = PickRandomAcrossRings(unused, perSet);
                if (set.Count < perSet)
                {
                    List<ReachableAttractionNode> reused = all.Where(a => !set.Contains(a)).ToList();
                    set.AddRange(PickRandomAcrossRings(reused, perSet - set.Count));
                }

                set.ForEach(a => used.Add(a.uid));
                sets.Add(set);
            }

            return sets;
        }


        /// <summary>隨機挑出不重複的敘事語氣；narrative_tone 不夠時用預設語氣補，還不夠就循環使用</summary>
        private async Task<List<string>> PickNarrativeTonesAsync(int count)
        {
            List<string> tones = (await _dao.GetNarrativeTonesAsync())
                .OrderBy(_ => Random.Shared.Next())
                .ToList();

            tones.AddRange(DefaultNarrativeTones.Where(t => !tones.Contains(t)).OrderBy(_ => Random.Shared.Next()));

            return Enumerable.Range(0, count).Select(i => tones[i % tones.Count]).ToList();
        }


        /// <summary>
        /// 排出參觀順序（從中心點出發、不回原點）：
        /// 1. 最近鄰居法：每次走到離目前位置最近、還沒去過的景點
        /// 2. 2-opt：路線有交叉時把中間那段反轉，直到總距離無法再縮短，避免來回繞路
        /// </summary>
        private static List<ReachableAttractionNode> OrderByVisit(List<ReachableAttractionNode> attractions, double lat, double lng)
        {
            var remaining = attractions.ToList();
            var ordered = new List<ReachableAttractionNode>();
            double curLat = lat, curLng = lng;

            while (remaining.Count > 0)
            {
                ReachableAttractionNode next = remaining.OrderBy(a => DistanceMeters(curLat, curLng, a.lat, a.lon)).First();
                ordered.Add(next);
                remaining.Remove(next);
                curLat = next.lat;
                curLng = next.lon;
            }

            // 第 i 個點的座標，-1 代表中心點
            (double lat, double lon) Point(int i) => i < 0 ? (lat, lng) : (ordered[i].lat, ordered[i].lon);
            double Dist(int a, int b) => DistanceMeters(Point(a).lat, Point(a).lon, Point(b).lat, Point(b).lon);

            bool improved = true;
            while (improved)
            {
                improved = false;
                for (int i = 0; i < ordered.Count - 1; i++)
                {
                    for (int j = i + 1; j < ordered.Count; j++)
                    {
                        // 反轉 ordered[i..j]：原本 (i-1→i) + (j→j+1)，改成 (i-1→j) + (i→j+1)；最後一段沒有 j+1
                        double before = Dist(i - 1, i) + (j + 1 < ordered.Count ? Dist(j, j + 1) : 0);
                        double after = Dist(i - 1, j) + (j + 1 < ordered.Count ? Dist(i, j + 1) : 0);

                        if (after < before - 1)
                        {
                            ordered.Reverse(i, j - i + 1);
                            improved = true;
                        }
                    }
                }
            }

            return ordered;
        }


        /// <summary>劇本節點之間的公車交通方案（含上下車站之間經過的所有站牌）</summary>
        public Task<List<BusTransitLeg>> GetStoryTransitAsync(int storyId)
        {
            return _dao.GetStoryTransitAsync(storyId);
        }


        #endregion


        #region Agent 即時推薦（/spin 用）


        /// <summary>
        /// 依城市/行政區名稱，將城市/行政區轉為經緯度，並將 Agent 推薦結果存進 md_agent_recommendation。
        /// </summary>
        public string SaveAgentRecommendation(string epId, string cityName, string townName, double lat, double lng, AgentOrchestrateResponse result)
        {
            return _dao.SaveAgentRecommendation(epId, cityName, townName, lat, lng, result);
        }


        #endregion
    }
}