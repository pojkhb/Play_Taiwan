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
        private readonly BadgeService _badgeService;
        private readonly FogGenerationQueue _fogQueue;
        private readonly Neo4j.PlaceLookupService _placeLookup;
        private readonly TaskDifficultyService _difficulty;


        public StoryService(StoryDao dao, Neo4jService neo4jService, ValhallaService valhallaService, IHttpClientFactory httpClientFactory,
                            BadgeService badgeService, FogGenerationQueue fogQueue,
                            Neo4j.PlaceLookupService placeLookup, TaskDifficultyService difficulty)
        {
            _dao = dao;
            _neo4jService = neo4jService;
            _valhallaService = valhallaService;
            _httpClientFactory = httpClientFactory;
            _badgeService = badgeService;
            _fogQueue = fogQueue;
            _placeLookup = placeLookup;
            _difficulty = difficulty;
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
        /// <summary>確認開始：只有劇本擁有者或協作隊員可以把這份劇本設為進行中。</summary>
        public StoryDetailResponse ConfirmStory(int auId, StoryConfirmRequest req)
        {
            if (req == null || req.story_id <= 0)
                throw new BadRequestException("請提供 story_id");

            StoryDao.StoryParticipation participation = _dao.GetStoryParticipation(auId, req.story_id)
                ?? throw new NotFoundException($"找不到 story_id = {req.story_id} 的劇本");

            if (participation.owner_id != auId && !participation.is_participant)
                throw new UnauthorizedAccessException("你沒有參與這個劇本，無法開始遊玩");

            bool success = _dao.SetStoryPlaying(auId, req.story_id);
            if (!success)
                throw new Exception($"找不到 story_id = {req.story_id} 的劇本");

            return _dao.GetDetail(req.story_id);
        }

        // === 新增：玩家結束/退出劇本時呼叫 ===
        /// <summary>
        /// 結束劇本：劇本存在且登入者有參與（擁有者或協作隊員）就視為成功。
        /// 有遊玩紀錄時標成完成；沒按過確認開始就不補建完成紀錄，避免首頁與過往紀錄多出沒玩過的劇本。
        /// 擁有者結束時，進行中的協作隊伍一併標成完成。
        /// </summary>
        public void EndStory(int auId, int storyId)
        {
            if (storyId <= 0)
                throw new BadRequestException("請提供 story_id");

            StoryDao.StoryParticipation participation = _dao.GetStoryParticipation(auId, storyId)
                ?? throw new NotFoundException($"找不到 story_id = {storyId} 的劇本");

            if (participation.owner_id != auId && !participation.is_participant)
                throw new UnauthorizedAccessException("你沒有參與這個劇本");

            _dao.ClearStoryPlaying(auId, storyId);
        }

        // === 新增：查目前哪個劇本正在進行中 ===
        public CurrentPlayingStory GetCurrentPlayingStory(int auId)
        {
            return _dao.GetCurrentPlayingStory(auId);
        }


        #region 存入 AI 生成的劇本（遊你說算）


        public async Task<int> SaveFullAiGeneratedStory(int auId, string cityName, string districtName, ScriptBlueprintData data)
        {
            int storyId = await _dao.SaveFullAiGeneratedStory(auId, cityName, districtName, data);
            await _badgeService.RefreshStoryCategoriesAsync(storyId);
            _fogQueue.Enqueue(storyId, force: true);   // 在背景把每一站的照片做成迷霧圖
            return storyId;
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
            if (result != null) return result;

            // null 代表 AI service 的 Neo4j 查詢失敗（真的查無資料會是空清單），改用 MySQL place 表的景點
            double latDelta = radiusKm / 111.0;
            double lonDelta = radiusKm / (111.0 * Math.Cos(lat * Math.PI / 180));
            var places = await _dao.GetPlacesInBoundsAsync(lat, lng, lat - latDelta, lat + latDelta, lng - lonDelta, lng + lonDelta);
            return places
                .Where(p => p.distance_m <= radiusKm * 1000)
                .GroupBy(p => p.name)
                .Select(g => g.First())
                .Select(p => new NearbyAttractionNode { name = p.name, lat = p.lat, lon = p.lon, distance_m = p.distance_m })
                .ToList();
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
        internal static List<ReachableAttractionNode> RemoveDuplicateAttractions(List<ReachableAttractionNode> candidates)
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
        internal static List<ReachableAttractionNode> PickRandomAcrossRings(List<ReachableAttractionNode> attractions, int maxCount)
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
        /// 把前端交通方式分組成 Valhalla costing。
        /// 公車/捷運不列入（沒有等車、轉乘時間，算出來會偏短）；剔除後沒有任何交通方式時預設步行。
        /// </summary>
        internal static List<(string costing, string label)> GroupTransports(List<string> transportation)
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

        // 任務類型代號（type.type_id）
        private const int CrossLevelTypeId = 2;         // 跨關集結型：最後一站
        private const int CreativePhotoTypeId = 3;      // 創意攝影型：每站必出
        private const int LocalFoodTypeId = 4;          // 地方美食型：餐廳類景點
        private const int CoopTaskTypeId = 5;           // 協作解謎型，至少 2 人才出
        private const int MerchantQuizTypeId = PlaceTypeWriter.MerchantQuizTypeId;   // 商家知識問答，直接引用商家題庫，不給 AI 生成

        // 每站再從景點在 place_type 可出的這幾類隨機抽一題：文化問答型、景點猜猜樂、e人訪談型
        // （舊架構是 6~10 類；9 改由商家題庫掛入，10 不在 AI 生成規格內）
        private static readonly int[] RandomTaskTypeIds = { 6, 7, 8 };

        // 選了這些交通方式時，才規劃節點之間的公車
        private static readonly HashSet<string> BusTransports = new HashSet<string> { "公車", "客運", "台灣好行" };

        private static readonly Dictionary<int, string> FallbackTypeNames = new Dictionary<int, string>
        {
            { 1, "GPS 區域定位型" }, { 2, "跨關集結型" }, { 3, "創意攝影型" }, { 4, "地方美食型" },
            { 5, "協作解謎型" }, { 6, "文化問答型" }, { 7, "景點猜猜樂" }, { 8, "e人訪談型" }, { 9, "商家知識問答" }
        };


        // GenerateGameStory 一次生成幾份劇本讓使用者挑
        private const int StoryCount = 3;

        // narrative_tone 沒有資料時用的預設敘事語氣
        private static readonly string[] DefaultNarrativeTones = { "溫情走心", "幽默詼諧", "懸疑推理" };


        /// <summary>
        /// GenerateGameStory：依使用者位置一次生成 3 份劇本（每份 8 個景點）讓使用者挑。
        /// 呼叫前 req.lat/lng、city_name、town_name 必須已由 Controller 補齊。
        /// </summary>
        public Task<List<GameStoryResult>> GenerateGameStoryAsync(int auId, GameStoryGenerateRequest req)
        {
            return GenerateStoriesAsync(auId, new GameStoryPlan
            {
                lat = req.lat,
                lng = req.lng,
                city_name = req.city_name,
                town_name = req.town_name,
                party_size = req.party_size,
                transportation = req.transportation,
                preferences = req.preferences,
                is_night_mode = req.is_night_mode,
                story_count = StoryCount,
                places_per_story = AttractionCount
            });
        }


        /// <summary>
        /// 劇本 + 任務一次生成（GenerateGameStory 使用；依城市／行政區生成也走這裡，由 Controller 先轉成中心點）：
        /// 1. 規劃旅遊行程：交通等時圈找出可到達的景點（含商家自建景點），抽出每份劇本的景點組合並排好順路的參觀順序
        /// 2. 判斷任務類型：照舊架構規則決定每站要出哪些題型；景點在 place_type 標有 9（商家有題庫）時，
        ///    另外必出一題商家知識問答。隨機題型依玩家紀錄調整（動態難度，見 TaskDifficultyService）
        /// 3. 打包成一包丟給 AI service /api/v1/generate，一次拿回所有劇本
        /// 4. 補上 AI 不回傳的欄位、掛入商家題庫任務，全部寫入資料庫（同一個交易），回傳給前端挑選
        /// </summary>
        public async Task<List<GameStoryResult>> GenerateStoriesAsync(int auId, GameStoryPlan plan)
        {
            int partySize = plan.party_size > 0 ? plan.party_size : 2;
            int isNightMode = plan.is_night_mode == 1 ? 1 : 0;   // 白天=0、夜間=1（story.is_night_mode）
            int storyCount = plan.story_count > 0 ? plan.story_count : StoryCount;
            int placesPerStory = plan.places_per_story > 0 ? plan.places_per_story : AttractionCount;

            // ── 1. 規劃旅遊行程 ──
            List<List<ReachableAttractionNode>> placeSets =
                await PlanItinerariesAsync(plan.lat, plan.lng, plan.transportation, storyCount, placesPerStory);

            // ── 2. 判斷任務類型 ──
            List<string> placeIds = placeSets.SelectMany(s => s).Select(a => a.uid).Distinct().ToList();
            List<StoryDao.PlaceTaskTypeRow> typeRows = await _dao.GetPlaceTaskTypesAsync(placeIds);
            ILookup<string, StoryDao.MerchantQuestionRow> merchantQuestions =
                (await _dao.GetMerchantQuestionsByPlaceIdsAsync(placeIds)).ToLookup(q => q.place_id);
            Dictionary<int, string> typeNames = await _dao.GetTaskTypeNamesAsync();
            List<string> tones = await PickNarrativeTonesAsync(placeSets.Count);

            // 動態難度：玩家第一次到的鄉鎮市區不出 e人訪談型，隨機題型的權重依玩家表現調整
            TaskDifficultyService.PlayerProfile player = await _difficulty.GetPlayerProfileAsync(auId);
            HashSet<string> firstVisitPlaces =
                await _difficulty.GetFirstVisitPlaceIdsAsync(player, placeIds, plan.city_name, plan.town_name);

            string TypeName(int id) => typeNames.TryGetValue(id, out string name) && !string.IsNullOrWhiteSpace(name) ? name
                                     : FallbackTypeNames.TryGetValue(id, out string fallback) ? fallback : "";

            // 商家知識問答：place_type 有 (景點, 9) 標記、且商家題庫有題目的景點，每站必出一題
            // （從題庫隨機挑，同一景點在各份劇本用同一題）
            HashSet<string> merchantQuizPlaces = typeRows
                .Where(r => r.type_id == MerchantQuizTypeId)
                .Select(r => r.place_id)
                .ToHashSet();

            Dictionary<string, StoryDao.MerchantQuestionRow> pickedQuestions = placeIds
                .Where(id => merchantQuizPlaces.Contains(id) && merchantQuestions[id].Any())
                .ToDictionary(id => id, id => merchantQuestions[id].OrderBy(_ => Random.Shared.Next()).First());

            AiGamePlace ToAiPlace(ReachableAttractionNode a, bool isLastNode)
            {
                List<StoryDao.PlaceTaskTypeRow> rows = typeRows.Where(r => r.place_id == a.uid).ToList();
                string category = rows.Select(r => r.place_category).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "Attraction";

                return new AiGamePlace
                {
                    place_id = a.uid,
                    p_name = a.name,
                    is_hotel = category == "Lodging" || category == "Hotel" ? 1 : 2,
                    is_hidden = 2,
                    type_list = DecideTaskTypes(rows, category, isLastNode, partySize, player, firstVisitPlaces.Contains(a.uid))
                        .Select(id => new AiTaskTypeRef { type_id = id, type_name = TypeName(id) })
                        .ToList()
                };
            }

            // ── 3. 打包成一包丟給 AI（places 已排成順路的參觀順序，AI 照陣列順序排 sn_order）──
            List<AiStoryCondition> conditions = placeSets.Select((set, i) => new AiStoryCondition
            {
                story_no = i + 1,
                nt_name = tones[i],
                places = set.Select((a, index) => ToAiPlace(a, index == set.Count - 1)).ToList()
            }).ToList();

            var aiRequest = new AiGameStoryRequest
            {
                city_name = plan.city_name ?? "",
                district_name = plan.town_name ?? "",
                party_size = partySize,
                s_tag = plan.preferences ?? new List<string>(),
                is_night_mode = isNightMode,
                stories = conditions
            };

            AiGameStoryResponse aiResponse = await RequestAiStoriesAsync(aiRequest);

            // ── 4. 補上 AI 不回傳的欄位、掛入商家題庫任務 ──
            List<string> transport = (plan.transportation ?? new List<string>())
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
                    HashSet<int> plannedTypes = place.type_list.Select(t => t.type_id).ToHashSet();

                    node.sn_order = i + 1;
                    node.p_name = place.p_name;
                    node.is_hidden = place.is_hidden;
                    node.is_night_only = isNightMode == 1 ? 1 : 0;
                    node.npc_id = null;

                    // 只收規劃好的題型，AI 多給的丟掉
                    node.tasks = (node.tasks ?? new List<GameStoryTask>())
                        .Where(t => plannedTypes.Contains(t.task_type))
                        .ToList();

                    if (pickedQuestions.TryGetValue(node.place_id, out StoryDao.MerchantQuestionRow question))
                    {
                        node.tasks.Add(new GameStoryTask
                        {
                            task_type = MerchantQuizTypeId,
                            question_id = question.question_id,
                            task_describe = question.question_describe,
                            task_option = question.options,
                            task_clue = new List<GameStoryTaskClue>()
                        });
                    }

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
                story.story_badge = new List<string>();   // 寫入資料庫後依劇本內容算出可抽的勳章類別

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

            // 寫入資料庫（回填 story_id、sn_id、task_id；有選公車時一併寫入節點間的直達公車）
            await _dao.SaveGameStoriesAsync(auId, plan.preferences, results, planBusTransit);

            // ── 6. 可抽的勳章類別、景點照片（劇本檔案館的車票用）；把公車方案（含經過的站牌）掛回每個節點 ──
            Dictionary<int, string> images = await _dao.GetNodeImagesAsync(results.Select(r => r.story_id));

            foreach (GameStoryResult result in results)
            {
                foreach (GameStoryNode node in result.nodes)
                    node.image_url = images.TryGetValue(node.sn_id, out string image) ? image : null;

                result.story.story_badge = await _badgeService.RefreshStoryCategoriesAsync(result.story_id);

                List<BusTransitLeg> legs = planBusTransit
                    ? await _dao.GetStoryTransitAsync(result.story_id)
                    : new List<BusTransitLeg>();

                foreach (GameStoryNode node in result.nodes)
                {
                    node.transit = legs.Where(l => l.to_sn_id == node.sn_id).OrderBy(l => l.leg_order).ToList();
                }
            }

            foreach (GameStoryResult r in results)
                _fogQueue.Enqueue(r.story_id, force: true);   // 在背景把每一站的照片做成迷霧圖

            HideAnswers(results);
            return results;
        }


        /// <summary>
        /// 回傳給前端前清掉答案類欄位（已存進資料庫）：正確答案、提示、所有座位的線索、選項的對錯。
        /// 前端改由節點遊玩畫面（GET api/Task/Node/{node_id}）取得自己座位的線索與作答方式。
        /// </summary>
        private static void HideAnswers(List<GameStoryResult> results)
        {
            foreach (GameStoryTask task in results.SelectMany(r => r.nodes).SelectMany(n => n.tasks ?? new List<GameStoryTask>()))
            {
                task.correct_answer = null;
                task.task_hint = null;
                task.task_clue = null;

                foreach (GameStoryTaskOption option in task.task_option ?? new List<GameStoryTaskOption>())
                {
                    option.is_correct = null;
                }
            }
        }


        /// <summary>
        /// 規劃旅遊行程：用交通等時圈找出範圍內所有景點（已去重複、不含公車），
        /// 抽出 storyCount 組、每組 placesPerStory 個景點（景點夠多時各組不重複），每組各自排好順路的參觀順序。
        /// </summary>
        private async Task<List<List<ReachableAttractionNode>>> PlanItinerariesAsync(
            double lat, double lng, List<string> transportation, int storyCount, int placesPerStory)
        {
            // 要拿等時圈補商家景點，所以請核心一併回傳等時圈
            ReachableAttractionsResponse reachable = await FindAllReachableAttractionsAsync(new ReachableAttractionsRequest
            {
                lat = lat,
                lng = lng,
                transportation = transportation,
                include_polygons = true
            });

            await AddReachableMerchantPlacesAsync(reachable, transportation);

            if (reachable.attractions.Count == 0)
                throw new Exception("附近找不到可到達的景點，請換個地點或交通方式再試一次");

            return PickPlaceSets(reachable.attractions, storyCount, placesPerStory)
                .Select(set => OrderByVisit(set, lat, lng))
                .ToList();
        }


        /// <summary>
        /// 把範圍內的商家景點（現有商家的 store_uid，含自建景點與綁定的既有景點）補進行程候選。
        /// FindAllReachableAttractionsAsync 只查 :Attraction，商家自建景點（:MerchantPlace）與綁定的餐廳、旅宿都不在其中，
        /// 所以座標向 Neo4j 查（PlaceLookupService），用同一組等時圈判斷是否在範圍內、同樣用 Valhalla 算真實交通時間與圈層後再加進候選。
        /// 已刪除帳號的商家沒有 store 資料，不會被排入。
        /// 刻意不改 FindAllReachableAttractionsAsync（交通規劃核心），這裡的圈內判斷與圈層規則要跟它保持一致。
        /// </summary>
        private async Task AddReachableMerchantPlacesAsync(ReachableAttractionsResponse reachable, List<string> transportation)
        {
            List<IsochroneBand> bands = reachable.isochrones;
            if (bands == null || bands.Count == 0) return;

            double lat = reachable.center_lat;
            double lng = reachable.center_lng;
            var (minLat, maxLat, minLon, maxLon) = ValhallaService.GetBoundingBox(bands);

            HashSet<string> existingUids = reachable.attractions.Select(a => a.uid).ToHashSet();
            List<string> storeUids = await _dao.GetStoreUidsAsync();
            List<ReachableAttractionNode> merchants = (await _placeLookup.GetPlacesInBoundsAsync(storeUids, minLat, maxLat, minLon, maxLon))
                .Where(p => !string.IsNullOrWhiteSpace(p.name))
                .Select(p => new ReachableAttractionNode
                {
                    uid = p.uid,
                    name = p.name,
                    lat = p.lat,
                    lon = p.lng,
                    distance_m = Math.Round(DistanceMeters(lat, lng, p.lat, p.lng), 1)
                })
                .Where(m => !existingUids.Contains(m.uid))
                .Where(m => bands.Any(b => ValhallaService.Contains(b, m.lat, m.lon)))
                .ToList();

            // 跟既有候選同名相近、或幾乎同座標的視為同一個景點，以既有候選為準
            HashSet<ReachableAttractionNode> kept = RemoveDuplicateAttractions(reachable.attractions.Concat(merchants).ToList()).ToHashSet();
            merchants = merchants.Where(kept.Contains).ToList();
            if (merchants.Count == 0) return;

            var costingGroups = GroupTransports(transportation);
            var targets = merchants.Select(a => (lat: a.lat, lng: a.lon)).ToList();
            List<(double? seconds, double? km)>[] timeResults = await Task.WhenAll(
                costingGroups.Select(group => _valhallaService.GetTravelTimesAsync(lat, lng, targets, group.costing)));

            List<int> minutes = reachable.contour_minutes;

            for (int i = 0; i < merchants.Count; i++)
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

                // 在圈內但路網上實際到不了，略過
                if (!bestSeconds.HasValue) continue;

                ReachableAttractionNode merchant = merchants[i];
                merchant.travel_minutes = Math.Round(bestSeconds.Value / 60.0, 1);
                merchant.travel_distance_km = Math.Round(bestKm ?? 0, 2);
                merchant.reachable_by = bestBy;

                int ringIndex = minutes.FindIndex(m => merchant.travel_minutes <= m);
                merchant.ring = ringIndex >= 0 ? ringIndex + 1 : minutes.Count;
                merchant.reachable_minutes = minutes[merchant.ring - 1];

                reachable.attractions.Add(merchant);
            }

            reachable.total_reachable = reachable.attractions.Count;
        }


        /// <summary>
        /// 判斷一站要出哪些題型（沿用舊架構 TaskGenerationService 的規則）：
        /// 1. 每站固定出創意攝影型（3）
        /// 2. 最後一站加跨關集結型（2）
        /// 3. 餐廳類景點加地方美食型（4）
        /// 4. 2 人以上加協作解謎型（5）
        /// 5. 再從景點在 place_type 可出的文化問答型／景點猜猜樂／e人訪談型（6~8）依權重抽一題：
        ///    玩家第一次到這個景點的鄉鎮市區時不出 e人訪談型，權重依玩家紀錄調整（TaskDifficultyService.PickRandomType）
        /// 商家知識問答（9）不參加隨機抽取：place_type 有 (景點, 9) 標記時，另外由商家題庫掛入（每站必出）。
        /// </summary>
        private static List<int> DecideTaskTypes(List<StoryDao.PlaceTaskTypeRow> placeTypes, string category, bool isLastNode, int partySize,
            TaskDifficultyService.PlayerProfile player, bool isFirstVisit)
        {
            var types = new List<int> { CreativePhotoTypeId };

            if (isLastNode) types.Add(CrossLevelTypeId);
            if (category == "Restaurant") types.Add(LocalFoodTypeId);
            if (partySize >= 2) types.Add(CoopTaskTypeId);

            IEnumerable<int> randomPool = placeTypes
                .Select(r => r.type_id)
                .Where(id => RandomTaskTypeIds.Contains(id));

            int? picked = TaskDifficultyService.PickRandomType(randomPool, player, isFirstVisit);
            if (picked.HasValue)
                types.Add(picked.Value);

            return types.Distinct().ToList();
        }


        /// <summary>打包好的劇本條件一次送給 AI service /api/v1/generate，拿回所有劇本。</summary>
        private async Task<AiGameStoryResponse> RequestAiStoriesAsync(AiGameStoryRequest aiRequest)
        {
            HttpClient client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            var content = new StringContent(JsonSerializer.Serialize(aiRequest), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.PostAsync(AiServiceConfig.Url("/api/v1/generate"), content);
            string responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"AI 劇本任務生成服務回應錯誤 (Status: {(int)response.StatusCode}): {responseString}");

            try
            {
                return JsonSerializer.Deserialize<AiGameStoryResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                throw new Exception($"AI 劇本任務回傳格式無法解析: {ex.Message}");
            }
        }


        /// <summary>
        /// 從範圍內所有景點抽出多組景點，每組各圈層輪流隨機抽。
        /// 景點夠多時各組互不重複；不夠時才允許跟前面的組重複，讓每組都盡量湊滿。
        /// </summary>
        internal static List<List<ReachableAttractionNode>> PickPlaceSets(List<ReachableAttractionNode> all, int setCount, int perSet)
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
        internal static List<ReachableAttractionNode> OrderByVisit(List<ReachableAttractionNode> attractions, double lat, double lng)
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


        #endregion
    }
}