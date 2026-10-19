// 檔案路徑：System\Services\Story\StoryService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using backend.dao;
using backend.Models;
using backend.ViewModels;
using backend.utils;
using backend.util;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;


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
        private readonly MapDao _mapDao;
        private readonly ILogger<StoryService> _logger;


        public StoryService(StoryDao dao, Neo4jService neo4jService, ValhallaService valhallaService, IHttpClientFactory httpClientFactory,
                            BadgeService badgeService, FogGenerationQueue fogQueue,
                            Neo4j.PlaceLookupService placeLookup, TaskDifficultyService difficulty, MapDao mapDao,
                            ILogger<StoryService> logger)
        {
            _logger = logger;
            _dao = dao;
            _neo4jService = neo4jService;
            _valhallaService = valhallaService;
            _httpClientFactory = httpClientFactory;
            _badgeService = badgeService;
            _fogQueue = fogQueue;
            _placeLookup = placeLookup;
            _difficulty = difficulty;
            _mapDao = mapDao;
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


        #region 喜愛的劇本

        /// <summary>設定或取消喜愛；只能改自己的劇本</summary>
        public async Task SetFavoriteAsync(int auId, int storyId, bool isFavorite)
        {
            if (!await _dao.SetFavoriteAsync(auId, storyId, isFavorite))
                throw new KeyNotFoundException("找不到此劇本：" + storyId);
        }

        /// <summary>喜愛的劇本；封面是第一站的照片：抵達過是景點照片，還沒抵達是迷霧圖（見 StationImages）</summary>
        public async Task<List<FavoriteStoryItem>> GetFavoriteStoriesAsync(int auId)
        {
            List<FavoriteStoryItem> stories = await _dao.GetFavoriteStoriesAsync(auId);
            StationImages images = await GetStationImagesAsync(stories.Select(s => s.cover_place_id));
            Dictionary<int, int> arrivedOrders = _mapDao.GetArrivedOrders(auId, stories.Select(s => s.story_id));

            foreach (FavoriteStoryItem story in stories)
            {
                bool arrived = TeamProgress.IsReached(1, arrivedOrders[story.story_id]);
                story.cover_image_url = images.Of(story.cover_place_id, arrived);

                // 有照片但迷霧圖還沒做好（例如迷霧功能上線前就生成的劇本）：在背景補做
                if (!arrived && images.MissingFog(story.cover_place_id))
                    _fogQueue.Enqueue(story.story_id);
            }

            return stories;
        }

        /// <summary>
        /// 劇本詳情的每一站補上照片：登入者（或隊伍）抵達過的站是景點照片，還沒抵達的站是迷霧圖（見 StationImages）
        /// </summary>
        public async Task<StoryDetailResponse> WithPlacePhotosAsync(StoryDetailResponse detail, int auId)
        {
            if (detail?.nodes == null || detail.nodes.Count == 0) return detail;

            StationImages images = await GetStationImagesAsync(detail.nodes.Select(n => n.place_id));
            int arrivedOrder = _mapDao.GetArrivedOrder(auId, detail.story_id);
            bool missingFog = false;

            foreach (NodeDetail node in detail.nodes)
            {
                bool arrived = TeamProgress.IsReached(node.order, arrivedOrder);
                node.image_url = images.Of(node.place_id, arrived);
                missingFog |= !arrived && images.MissingFog(node.place_id);
            }

            // 有照片但迷霧圖還沒做好（例如迷霧功能上線前就生成的劇本）：在背景補做
            if (missingFog)
                _fogQueue.Enqueue(detail.story_id);

            return detail;
        }

        /// <summary>
        /// 每一站給玩家看的照片，跟地圖同一個規則（景點是哪裡，到了現場才知道）：
        /// 抵達過的站是景點照片（用景點 ID 向 Neo4j 拿，景點沒有照片時為 null）；
        /// 還沒抵達的站是這張照片的迷霧版，還沒做好或景點沒有照片時是通用迷霧圖。
        /// 迷霧圖是站內路徑，由 Controller 組成完整網址
        /// </summary>
        private class StationImages
        {
            /// <summary>景點 ID → 景點照片</summary>
            public Dictionary<string, string> photos { get; set; }

            /// <summary>景點照片 → 這張照片的迷霧圖（已經做好的才有）</summary>
            public Dictionary<string, string> fog_images { get; set; }

            /// <summary>通用迷霧圖</summary>
            public string default_fog { get; set; }

            public string Of(string placeId, bool arrived)
            {
                string photo = placeId != null ? photos.GetValueOrDefault(placeId) : null;
                if (arrived) return photo;

                return (photo != null ? fog_images.GetValueOrDefault(photo) : null) ?? default_fog;
            }

            /// <summary>景點有照片，但迷霧圖還沒做好</summary>
            public bool MissingFog(string placeId) =>
                placeId != null && photos.TryGetValue(placeId, out string photo) && !fog_images.ContainsKey(photo);
        }

        private async Task<StationImages> GetStationImagesAsync(IEnumerable<string> placeIds)
        {
            Dictionary<string, string> photos = await _placeLookup.GetPlaceImagesAsync(placeIds);

            return new StationImages
            {
                photos = photos,
                fog_images = _mapDao.GetFogImages(photos.Values),
                default_fog = _mapDao.GetDefaultFogImage() ?? MapService.DefaultFogImage
            };
        }

        #endregion


        #region 加入 Google 行事曆

        /// <summary>沒指定遊玩時間時，每站估 40 分鐘</summary>
        private const int MinutesPerStop = 40;

        /// <summary>
        /// 產生把劇本加入 Google 行事曆的連結。地點只放第一站（集合點），其他站還在迷霧中不會出現。
        /// </summary>
        public async Task<StoryCalendarResponse> BuildCalendarEventAsync(int storyId, StoryCalendarRequest req)
        {
            StoryDao.StoryCalendarInfo info = await _dao.GetCalendarInfoAsync(storyId)
                ?? throw new KeyNotFoundException("找不到此劇本：" + storyId);

            if (req?.duration_minutes is int requested && (requested < 30 || requested > 1440))
                throw new ArgumentException("遊玩時間請介於 30～1440 分鐘");

            bool night = info.is_night_mode == 1;
            DateTime start = req?.start_time is DateTime t ? ToTaiwanTime(t) : DefaultCalendarStart(night, DateTime.UtcNow.AddHours(8));
            int minutes = req?.duration_minutes ?? Math.Max(60, info.node_count * MinutesPerStop);
            DateTime end = start.AddMinutes(minutes);

            string area = $"{info.city_name}{info.district_name}";
            string location = string.Join(" ", new[] { info.first_place_name, info.first_place_address }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (location == "") location = area;

            string synopsis = (string.IsNullOrWhiteSpace(info.story_synopsis) ? info.story_prologue : info.story_synopsis)?.Trim() ?? "";
            if (synopsis.Length > 300) synopsis = synopsis[..300] + "…";

            var lines = new List<string>();
            if (synopsis != "") lines.AddRange(new[] { synopsis, "" });
            if (area != "") lines.Add($"地區：{area}");
            lines.Add($"共 {info.node_count} 站・{(night ? "夜間劇本" : "白天劇本")}");
            if (!string.IsNullOrWhiteSpace(info.first_place_name)) lines.Add($"集合點：第一站 {info.first_place_name}");
            lines.AddRange(new[] { "", "出發時打開 Play Taiwan，選這個劇本開始遊玩。其他站還在迷霧中，要到現場解謎才會揭曉。" });
            string details = string.Join("\n", lines);

            string title = $"Play Taiwan｜{info.story_title}";

            return new StoryCalendarResponse
            {
                google_calendar_url = GoogleCalendarLink.Build(title, start, end, details, location),
                title = title,
                start_time = start,
                end_time = end,
                location = location,
                details = details
            };
        }

        /// <summary>沒指定出發時間時：明天早上 10:00，夜間劇本晚上 19:00（台灣時間）</summary>
        internal static DateTime DefaultCalendarStart(bool night, DateTime taiwanNow) =>
            DateTime.SpecifyKind(taiwanNow.Date.AddDays(1).AddHours(night ? 19 : 10), DateTimeKind.Unspecified);

        /// <summary>前端應該傳台灣時間；如果帶了時區（Z 或 +08:00）就換算回台灣時間</summary>
        private static DateTime ToTaiwanTime(DateTime t) => DateTime.SpecifyKind(t.Kind switch
        {
            DateTimeKind.Utc => t.AddHours(8),
            DateTimeKind.Local => t.ToUniversalTime().AddHours(8),
            _ => t
        }, DateTimeKind.Unspecified);


        #endregion


        #region 遊你說算：送出 AI 生成工作、輪詢進度、完成後存檔


        private const string TextBlueprintPath = "/api/api/admin/generate_script_blueprint_by_text";

        /// <summary>送出後超過這個時間 AI 還沒做完就算失敗</summary>
        private static readonly TimeSpan TextJobTimeout = TimeSpan.FromMinutes(10);

        /// <summary>存檔中超過這個時間還沒結果（例如後端存到一半重啟），下一次輪詢可以接手重存</summary>
        private static readonly TimeSpan TextJobSavingTimeout = TimeSpan.FromMinutes(5);

        /// <summary>完成結果存進 story_text_job.result_json，中文不轉成 \uXXXX，資料庫裡看得懂</summary>
        private static readonly JsonSerializerOptions TextJobJsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };


        /// <summary>
        /// 把使用者的一句話送給 AI 生成劇本。AI 立刻回 job_id，劇本在背景生成；
        /// 這裡記下 job_id 是誰送的就回傳，前端再用 GetTextBlueprintJobAsync 輪詢到完成。
        /// </summary>
        public async Task<GenerateByTextResult> SubmitTextBlueprintJobAsync(int auId, string userPrompt)
        {
            using var content = new StringContent(JsonSerializer.Serialize(new { user_prompt = userPrompt }), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await CreateTextJobClient().PostAsync(AiServiceConfig.Url(TextBlueprintPath), content);
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new Exception($"外部 AI 服務回應錯誤 (Status: {(int)response.StatusCode}): {VlogAiGateway.AiErrorMessage(body)}");

            GenerateScriptBlueprintByTextResponse job = JsonSerializer.Deserialize<GenerateScriptBlueprintByTextResponse>(body, AiJsonOptions);
            if (string.IsNullOrWhiteSpace(job?.job_id))
                throw new Exception($"AI 服務沒有回傳 job_id：{VlogAiGateway.AiErrorMessage(body)}");

            await _dao.CreateTextJobAsync(job.job_id, auId, userPrompt);

            if (job.data != null) return await SaveTextJobAsync(auId, job.job_id, job);   // AI 若直接回傳結果就不用等輪詢
            return TextJobProcessing(job.job_id, job.stage ?? "排隊中");
        }


        /// <summary>
        /// 查遊你說算的生成進度，只查得到自己送出的。還在生成時向 AI 問一次進度；
        /// AI 做完就在這次請求存成劇本，之後再查都回傳同一個劇本，不會重複存。
        /// </summary>
        public async Task<GenerateByTextResult> GetTextBlueprintJobAsync(int auId, string jobId)
        {
            StoryDao.TextJobRow row = await _dao.GetTextJobAsync(jobId, auId)
                ?? throw new NotFoundException("找不到這個劇本生成工作");

            if (row.status == StoryDao.TextJobRow.Done) return TextJobDone(row);
            if (row.status == StoryDao.TextJobRow.Failed) return TextJobFailed(jobId, row.error_message);
            if (row.status == StoryDao.TextJobRow.Saving && row.idle_seconds < TextJobSavingTimeout.TotalSeconds)
                return TextJobProcessing(jobId, "存檔中");

            GenerateScriptBlueprintByTextResponse job;
            try
            {
                using HttpResponseMessage response = await CreateTextJobClient().GetAsync(AiServiceConfig.Url($"{TextBlueprintPath}/{Uri.EscapeDataString(jobId)}"));
                string body = await response.Content.ReadAsStringAsync();

                // AI 只把結果放在記憶體裡保留 1 小時，重啟或過期就查不到了
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return await FailTextJobAsync(jobId, "AI 服務找不到這個生成工作（可能已重啟或超過保留時間），請重新送出");
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}：{VlogAiGateway.AiErrorMessage(body)}");

                job = JsonSerializer.Deserialize<GenerateScriptBlueprintByTextResponse>(body, AiJsonOptions);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
            {
                // AI 暫時連不上就當作還在生成，下次輪詢再查；一直連不上就等逾時
                _logger.LogWarning(e, "查詢遊你說算生成進度失敗（job_id={JobId}），下次輪詢再查", jobId);
                if (row.elapsed_seconds > TextJobTimeout.TotalSeconds)
                    return await FailTextJobAsync(jobId, $"AI 劇本生成超過 {TextJobTimeout.TotalMinutes} 分鐘仍未完成，請重新送出");
                return TextJobProcessing(jobId, "暫時查不到 AI 進度，稍後再試");
            }

            if (job?.data != null) return await SaveTextJobAsync(auId, jobId, job);

            string status = (job?.status ?? "").Trim().ToLowerInvariant();
            if (status is "error" or "failed" or "failure")
                return await FailTextJobAsync(jobId, $"AI 劇本生成失敗（{job.stage}）：{job.error}");
            if (row.elapsed_seconds > TextJobTimeout.TotalSeconds)
                return await FailTextJobAsync(jobId, $"AI 劇本生成超過 {TextJobTimeout.TotalMinutes} 分鐘仍未完成，請重新送出");

            return TextJobProcessing(jobId, job?.stage);
        }


        /// <summary>
        /// AI 做完的劇本存檔。同一個工作只讓一個請求存（把狀態搶成「存檔中」的那個），
        /// 同時進來的其他請求回「存檔中」，下次輪詢就會拿到存好的劇本。
        /// </summary>
        private async Task<GenerateByTextResult> SaveTextJobAsync(int auId, string jobId, GenerateScriptBlueprintByTextResponse job)
        {
            if (!await _dao.TryStartSavingTextJobAsync(jobId, TextJobSavingTimeout))
            {
                StoryDao.TextJobRow row = await _dao.GetTextJobAsync(jobId, auId);
                return row?.status == StoryDao.TextJobRow.Done ? TextJobDone(row) : TextJobProcessing(jobId, "存檔中");
            }

            var result = new GenerateByTextResult
            {
                job_id = jobId,
                status = GenerateByTextResult.Done,
                stage = "完成",
                detected_city = job.parsed_intent?.city_name ?? "",
                detected_town = job.parsed_intent?.town_name ?? "",
                parsed_intent = job.parsed_intent,
                data = job.data
            };

            try
            {
                // spot_uuid 是 AI 依站點順序填入的 Neo4j uid：用它查正確的名稱與座標，
                // 順便把 LLM 寫壞的罕用字（例如「簃居青衖」變成「�居青衖」）換回來
                List<string> spotUuids = (job.data.nodes ?? new List<ScriptBlueprintNode>()).Select(n => n.spot_uuid).ToList();
                Dictionary<string, Neo4j.PlaceLookupService.PlaceInfo> places = await _placeLookup.GetPlacesAsync(spotUuids);
                BlueprintTextRepair.Repair(job.data, places.ToDictionary(p => p.Key, p => p.Value.name));

                // place 表要的地址、分類、介紹、照片、營業時間等，新增或補齊 place 時一起寫進去
                Dictionary<string, Neo4j.PlaceLookupService.PlaceRecord> placeRecords = await _placeLookup.GetPlaceRecordsAsync(spotUuids);

                result.story_id = await _dao.SaveFullAiGeneratedStory(auId, result.detected_city, result.detected_town,
                    job.parsed_intent?.traveler_count ?? 0, job.data, places, placeRecords,
                    jobId, JsonSerializer.Serialize(result, TextJobJsonOptions));
            }
            catch
            {
                await _dao.ResetTextJobAsync(jobId);   // 沒存成功，下次輪詢再存一次
                throw;
            }

            await _badgeService.RefreshStoryCategoriesAsync(result.story_id.Value);
            _fogQueue.Enqueue(result.story_id.Value, force: true);   // 在背景把每一站的照片做成迷霧圖
            return result;
        }


        /// <summary>
        /// 遊你說算生成完成的劇本每一站補上圖片，跟 GenerateGameStory 同一個規則：剛生成的劇本每一站都還沒抵達，一律是迷霧圖
        /// （這張照片的迷霧版；還沒做好時先給通用迷霧圖，存檔時已在背景做，之後再查就會換上）。迷霧圖是站內路徑，由 Controller 組成完整網址
        /// </summary>
        public async Task<GenerateByTextResult> WithStationImagesAsync(GenerateByTextResult result)
        {
            List<ScriptBlueprintNode> nodes = result?.data?.nodes;
            if (result?.status != GenerateByTextResult.Done || result.story_id == null || nodes == null || nodes.Count == 0) return result;

            // 景點 ID 以存進 story_node 的為準（AI 沒給 spot_uuid 時，是存檔時用景點名稱向 Neo4j 查到的）
            Dictionary<int, string> placeIds = _mapDao.GetStoryNodes(result.story_id.Value)
                .GroupBy(n => n.node_order)
                .ToDictionary(g => g.Key, g => g.First().place_id);
            StationImages images = await GetStationImagesAsync(placeIds.Values);

            foreach (ScriptBlueprintNode node in nodes)
                node.image_url = images.Of(placeIds.GetValueOrDefault(node.node_order), arrived: false);

            return result;
        }


        private async Task<GenerateByTextResult> FailTextJobAsync(string jobId, string error)
        {
            await _dao.FailTextJobAsync(jobId, error);
            return TextJobFailed(jobId, error);
        }


        private HttpClient CreateTextJobClient()
        {
            HttpClient client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(1);
            return client;
        }


        private static GenerateByTextResult TextJobProcessing(string jobId, string stage) =>
            new() { job_id = jobId, status = GenerateByTextResult.Processing, stage = stage };


        private static GenerateByTextResult TextJobFailed(string jobId, string error) =>
            new() { job_id = jobId, status = GenerateByTextResult.Failed, error = error };


        /// <summary>已存好的工作：回傳當初存下的完整結果，story_id 以資料庫為準</summary>
        private static GenerateByTextResult TextJobDone(StoryDao.TextJobRow row)
        {
            GenerateByTextResult result = string.IsNullOrEmpty(row.result_json)
                ? new GenerateByTextResult()
                : JsonSerializer.Deserialize<GenerateByTextResult>(row.result_json, TextJobJsonOptions);
            result.job_id = row.job_id;
            result.status = GenerateByTextResult.Done;
            result.stage = "完成";
            result.error = null;
            result.story_id = row.s_id;
            return result;
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


        #region 劇本任務生成（AI service /api/stories/tasks，一次只送一份劇本）

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


        /// <summary>送出後超過這個時間 AI 還沒全部做完，就把做好的幾份存起來，沒做完的算失敗</summary>
        private static readonly TimeSpan GameJobTimeout = TimeSpan.FromMinutes(10);

        /// <summary>存檔中超過這個時間還沒結果（例如後端存到一半重啟），下一次輪詢可以接手</summary>
        private static readonly TimeSpan GameJobSavingTimeout = TimeSpan.FromMinutes(5);

        /// <summary>工作內容與存好的劇本存進 story_game_job，中文不轉成 \uXXXX，資料庫裡看得懂</summary>
        private static readonly JsonSerializerOptions GameJobJsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };


        /// <summary>
        /// 劇本生成工作的內容（story_game_job.plan_json）：送給 AI 的每份劇本條件與 AI 任務代號，
        /// 以及 AI 做完後組成劇本、寫入資料庫要用的資料
        /// </summary>
        private class GameJobPlan
        {
            public int party_size { get; set; }
            public List<string> preferences { get; set; }

            /// <summary>整理過的交通方式（沒選時是步行）</summary>
            public List<string> transport { get; set; }

            /// <summary>有選公車、客運或台灣好行：存檔時一併寫入節點之間的直達公車</summary>
            public bool plan_bus_transit { get; set; }

            /// <summary>景點 → 必出的商家知識問答（從商家題庫挑好的那一題，各份劇本用同一題）</summary>
            public Dictionary<string, StoryDao.MerchantQuestionRow> merchant_questions { get; set; }

            public List<GameJobStory> stories { get; set; }
        }

        private class GameJobStory
        {
            /// <summary>第幾份（1、2、3）</summary>
            public int story_no { get; set; }

            /// <summary>AI 任務代號；送給 AI 失敗時是 null</summary>
            public string task_id { get; set; }

            /// <summary>送給 AI 失敗的原因</summary>
            public string error { get; set; }

            public AiGameStoryRequest request { get; set; }

            /// <summary>每一站預計幾點到、是不是天黑後才到（順序同 request.places）</summary>
            public List<GameJobStop> stops { get; set; }
        }

        private class GameJobStop
        {
            public string place_id { get; set; }
            public DateTime arrival { get; set; }
            public bool is_night { get; set; }
        }


        /// <summary>
        /// GenerateGameStory：依使用者位置規劃 3 份劇本（每份 8 個景點），每份各自送給 AI 生成，送出後立刻回傳 job_id，
        /// 前端再用 GetGameStoryJobAsync 輪詢到完成。AI 同一時間只做一個任務，幾份會在 AI 端排隊依序生成。
        /// 呼叫前 req.lat/lng、city_name、town_name 必須已由 Controller 補齊；範圍內景點不夠這類條件問題在這裡就丟出 BadRequestException，不會送出工作。
        /// </summary>
        public async Task<GameStoryJobResult> SubmitGameStoryJobAsync(int auId, GameStoryGenerateRequest req)
        {
            GameJobPlan plan = await PlanGameStoriesAsync(auId, new GameStoryPlan
            {
                lat = req.lat,
                lng = req.lng,
                city_name = req.city_name,
                town_name = req.town_name,
                party_size = req.party_size,
                transportation = req.transportation,
                preferences = req.preferences,
                max_minutes = req.max_minutes,
                start_time = req.start_time,
                story_count = StoryCount,
                places_per_story = AttractionCount
            });

            // 送出的 request 與 AI 的 response 由 AiTrafficLogHandler 記錄在 logs/ai/{日期}/
            using (HttpClient client = CreateGameJobClient())
            {
                foreach (GameJobStory item in plan.stories)
                {
                    try
                    {
                        using var content = new StringContent(JsonSerializer.Serialize(item.request), Encoding.UTF8, "application/json");
                        using HttpResponseMessage response = await client.PostAsync(AiServiceConfig.Url(GameStoryPath), content);
                        AiGameStoryJob job = await VlogAiGateway.ReadJsonAsync<AiGameStoryJob>(response, "送出 AI 劇本任務生成");
                        if (string.IsNullOrWhiteSpace(job?.task_id))
                            throw new Exception("AI 劇本任務生成沒有回傳 task_id");
                        item.task_id = job.task_id;
                    }
                    catch (Exception e)
                    {
                        // 這份不做，其他份照常
                        _logger.LogWarning(e, "送出第 {StoryNo} 份劇本給 AI 失敗", item.story_no);
                        item.error = e.Message;
                    }
                }
            }

            if (plan.stories.All(s => s.task_id == null))
                throw new Exception(string.Join("；", plan.stories.Select(s => s.error).Distinct()));

            string jobId = Guid.NewGuid().ToString("N");
            await _dao.CreateGameJobAsync(jobId, auId, JsonSerializer.Serialize(plan, GameJobJsonOptions));
            return GameJobProcessing(jobId, plan, 0, "排隊中");
        }


        /// <summary>
        /// 查 GenerateGameStory 的生成進度，只查得到自己送出的。還在生成時向 AI 問一次每份的進度；
        /// 全部做完（或超過 10 分鐘）就在這次請求把做好的幾份存成劇本，之後再查都回傳同樣的劇本，不會重複存。
        /// </summary>
        public async Task<GameStoryJobResult> GetGameStoryJobAsync(int auId, string jobId)
        {
            StoryDao.GameJobRow row = await _dao.GetGameJobAsync(jobId, auId)
                ?? throw new NotFoundException("找不到這個劇本生成工作");
            GameJobPlan plan = JsonSerializer.Deserialize<GameJobPlan>(row.plan_json, GameJobJsonOptions);

            if (row.status == StoryDao.GameJobRow.Done) return GameJobDone(row, plan);
            if (row.status == StoryDao.GameJobRow.Failed) return GameJobFailed(jobId, plan, row.error_message);
            if (row.status == StoryDao.GameJobRow.Saving && row.idle_seconds < GameJobSavingTimeout.TotalSeconds)
                return GameJobProcessing(jobId, plan, plan.stories.Count, "存檔中");

            // 劇本已經寫進資料庫、只差最後整理（上次存到一半中斷）：不用再問 AI
            if (row.result_json != null) return await SaveGameJobAsync(auId, row, plan, null);

            var generated = new AiGameStoryResult[plan.stories.Count];
            var errors = new List<string>();
            int pending = 0;

            using (HttpClient client = CreateGameJobClient())
            {
                for (int i = 0; i < plan.stories.Count; i++)
                {
                    GameJobStory item = plan.stories[i];
                    if (item.task_id == null)
                    {
                        errors.Add(item.error);   // 送給 AI 時就失敗了
                        continue;
                    }

                    AiGameStoryJob job = await GetAiStoryJobAsync(client, item.task_id);
                    string error = AiStoryJobError(job);

                    if (job?.result != null)
                        generated[i] = job.result;
                    else if (error == null)
                        pending++;   // 還在排隊或生成中；AI 暫時連不上也當作還在生成
                    else
                    {
                        _logger.LogWarning("第 {StoryNo} 份劇本 AI 生成失敗（task_id={TaskId}）：{Error}", item.story_no, item.task_id, error);
                        errors.Add(error);
                    }
                }
            }

            int finished = generated.Count(g => g != null);
            if (pending > 0)
            {
                if (row.elapsed_seconds <= GameJobTimeout.TotalSeconds)
                    return GameJobProcessing(jobId, plan, finished, $"劇本生成中（已完成 {finished}/{plan.stories.Count} 份）");

                _logger.LogWarning("劇本生成工作 {JobId} 超過 {Minutes} 分鐘，還有 {Pending} 份沒做完", jobId, GameJobTimeout.TotalMinutes, pending);
                errors.Add($"AI 劇本任務生成超過 {GameJobTimeout.TotalMinutes} 分鐘仍未完成");
            }

            // 全部做完（或逾時）：做好的幾份存起來，沒做好的不存
            if (finished == 0)
                return await FailGameJobAsync(jobId, plan, $"{string.Join("；", errors.Distinct())}，請重新生成");
            return await SaveGameJobAsync(auId, row, plan, generated);
        }


        /// <summary>
        /// AI 做完的劇本存檔。同一個工作只讓一個請求存（把狀態搶成「存檔中」的那個），同時進來的其他請求回「存檔中」，
        /// 下次輪詢就會拿到存好的劇本。generated 是 null 代表劇本已經寫進資料庫（row.result_json），只做最後整理。
        /// </summary>
        private async Task<GameStoryJobResult> SaveGameJobAsync(int auId, StoryDao.GameJobRow row, GameJobPlan plan, AiGameStoryResult[] generated)
        {
            string jobId = row.job_id;
            if (!await _dao.TryStartSavingGameJobAsync(jobId, GameJobSavingTimeout))
            {
                StoryDao.GameJobRow latest = await _dao.GetGameJobAsync(jobId, auId);
                return latest?.status == StoryDao.GameJobRow.Done
                    ? GameJobDone(latest, plan)
                    : GameJobProcessing(jobId, plan, plan.stories.Count, "存檔中");
            }

            List<GameStoryResult> results;
            try
            {
                if (generated == null)
                {
                    results = JsonSerializer.Deserialize<List<GameStoryResult>>(row.result_json, GameJobJsonOptions);
                }
                else
                {
                    results = await BuildGameStoriesAsync(plan, generated);
                    if (results.Count == 0)
                        return await FailGameJobAsync(jobId, plan, "AI 劇本任務回傳內容為空，請重新生成");

                    // 寫入資料庫（回填 story_id、sn_id、task_id；有選公車時一併寫入節點間的直達公車），同一個交易記下存好的劇本
                    await _dao.SaveGameStoriesAsync(auId, plan.preferences, results, plan.plan_bus_transit,
                        jobId, () => JsonSerializer.Serialize(results, GameJobJsonOptions));
                }

                await FinishGameStoriesAsync(results, plan.plan_bus_transit);
                HideAnswers(results);
                await _dao.CompleteGameJobAsync(jobId, JsonSerializer.Serialize(results, GameJobJsonOptions));
            }
            catch
            {
                await _dao.ResetGameJobAsync(jobId);   // 沒存成功，下次輪詢再試；劇本已寫進資料庫的話只會重做最後整理
                throw;
            }

            return GameJobDone(jobId, plan, results);
        }


        /// <summary>
        /// 規劃這次要生成的幾份劇本（GenerateGameStory 使用）：
        /// 1. 規劃旅遊行程：交通等時圈（玩家選最遠願意花幾分鐘）找出可到達的景點（含商家自建景點），
        ///    抽出每份劇本的景點組合、排好順路的參觀順序，再依出發時間估算每一站幾點到，把不合適的站換掉
        ///    （到的時候沒開；天黑後才到的站改挑適合晚上的景點）。日夜由抵達時間決定，前端不用選。
        /// 2. 判斷任務類型：照舊架構規則決定每站要出哪些題型；景點在 place_type 標有 9（商家有題庫）時，
        ///    另外必出一題商家知識問答。隨機題型依玩家紀錄調整（動態難度，見 TaskDifficultyService）
        /// 3. 每份劇本各自打包成 AI service /api/stories/tasks 的條件（一次只收一份）
        /// AI 做完後由 BuildGameStoriesAsync 補上 AI 不回傳的欄位、掛入商家題庫任務，再寫入資料庫。
        /// </summary>
        private async Task<GameJobPlan> PlanGameStoriesAsync(int auId, GameStoryPlan plan)
        {
            int partySize = plan.party_size > 0 ? plan.party_size : 2;
            int storyCount = plan.story_count > 0 ? plan.story_count : StoryCount;
            int placesPerStory = plan.places_per_story > 0 ? plan.places_per_story : AttractionCount;
            DateTime startTime = plan.start_time is DateTime t ? ToTaiwanTime(t) : DateTime.UtcNow.AddHours(8);

            // AI 規格的城市、行政區不可為空：行政區判斷不出來時用城市代替
            string cityName = plan.city_name?.Trim() ?? "";
            if (cityName.Length == 0)
                throw new BadRequestException("無法判斷所在的城市，請帶 city_name 再試一次");
            string districtName = string.IsNullOrWhiteSpace(plan.town_name) ? cityName : plan.town_name.Trim();

            // ── 1. 規劃旅遊行程（每一站都帶著預計抵達時間）──
            List<List<ItineraryRules.PlannedStop>> itineraries =
                await PlanItinerariesAsync(plan.lat, plan.lng, plan.transportation, storyCount, placesPerStory, plan.max_minutes, startTime);
            List<List<ReachableAttractionNode>> placeSets = itineraries.Select(stops => stops.Select(s => s.place).ToList()).ToList();

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
                        .Select(id => new AiTaskTypeRef { type_id = id, type_name = TaskTypeName(typeNames, id) })
                        .ToList()
                };
            }

            List<string> transport = (plan.transportation ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct()
                .ToList();
            if (transport.Count == 0) transport.Add("步行");

            // ── 3. 每份劇本各自打包成 AI 的條件（/api/stories/tasks 一次只收一份；places 已排成順路的參觀順序，AI 照陣列順序排 sn_order）──
            return new GameJobPlan
            {
                party_size = partySize,
                preferences = plan.preferences ?? new List<string>(),
                transport = transport,
                plan_bus_transit = transport.Any(t => BusTransports.Contains(t)),
                merchant_questions = pickedQuestions,
                stories = placeSets.Select((set, i) => new GameJobStory
                {
                    story_no = i + 1,
                    request = new AiGameStoryRequest
                    {
                        city_name = cityName,
                        district_name = districtName,
                        party_size = partySize,
                        s_tag = plan.preferences ?? new List<string>(),
                        // AI 文件定義 1＝夜間、2＝白天（跟 story.is_night_mode 的 0／1 不同）；每份看自己的站，過半數天黑後才到就當夜間
                        is_night_mode = MostlyNight(itineraries[i]) ? 1 : 2,
                        nt_name = tones[i],
                        places = set.Select((a, index) => ToAiPlace(a, index == set.Count - 1)).ToList()
                    },
                    stops = itineraries[i]
                        .Select(s => new GameJobStop { place_id = s.place.uid, arrival = s.arrival, is_night = s.is_night })
                        .ToList()
                }).ToList()
            };
        }


        /// <summary>
        /// AI 生成好的幾份劇本補上 AI 不回傳的欄位（景點名稱、預計抵達時間、日夜）、只留規劃好的題型、掛入商家題庫任務。
        /// AI 生成失敗的那份不放進來。
        /// </summary>
        private async Task<List<GameStoryResult>> BuildGameStoriesAsync(GameJobPlan plan, AiGameStoryResult[] generatedStories)
        {
            Dictionary<int, string> typeNames = await _dao.GetTaskTypeNamesAsync();
            var results = new List<GameStoryResult>();

            for (int storyIndex = 0; storyIndex < plan.stories.Count; storyIndex++)
            {
                GameJobStory item = plan.stories[storyIndex];
                AiGameStoryRequest condition = item.request;
                AiGameStoryResult generated = generatedStories[storyIndex];
                if (generated?.story == null || generated.nodes == null || generated.nodes.Count == 0)
                    continue;   // AI 生成失敗的那份就不存，其他份照常

                // 節點順序以後端送出的 places 為準（規格要求 AI 不可重排），對不到的節點丟掉
                List<string> order = condition.places.Select(p => p.place_id).ToList();
                List<GameStoryNode> nodes = generated.nodes
                    .Where(n => order.Contains(n.place_id))
                    .GroupBy(n => n.place_id)
                    .Select(g => g.First())
                    .OrderBy(n => order.IndexOf(n.place_id))
                    .ToList();

                if (nodes.Count == 0) continue;

                // 這份劇本每一站規劃好的抵達時間
                List<GameJobStop> stops = item.stops
                    .Where(s => nodes.Any(n => n.place_id == s.place_id))
                    .ToList();

                for (int i = 0; i < nodes.Count; i++)
                {
                    GameStoryNode node = nodes[i];
                    AiGamePlace place = condition.places.First(p => p.place_id == node.place_id);
                    GameJobStop stop = stops.First(s => s.place_id == node.place_id);
                    HashSet<int> plannedTypes = place.type_list.Select(t => t.type_id).ToHashSet();

                    node.sn_order = i + 1;
                    node.p_name = place.p_name;
                    node.is_hidden = place.is_hidden;
                    node.is_night_only = stop.is_night ? 1 : 0;   // 夜間站：預計天黑後才到
                    node.estimated_arrival = stop.arrival;

                    // 只收規劃好的題型，AI 多給的丟掉
                    node.tasks = (node.tasks ?? new List<GameStoryTask>())
                        .Where(t => plannedTypes.Contains(t.task_type))
                        .ToList();

                    if (plan.merchant_questions != null && plan.merchant_questions.TryGetValue(node.place_id, out StoryDao.MerchantQuestionRow question))
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
                        task.type_name = TaskTypeName(typeNames, task.task_type);

                    node.sn_task_type = string.Join(",", node.tasks.Select(t => t.type_name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());
                }

                GameStoryInfo story = generated.story;
                story.npc = new GameStoryNpc { npc_name = generated.npc?.name };   // 寫入資料庫時對應 npc 表，名單上沒有就用薯光
                story.city_name = condition.city_name;
                story.district_name = condition.district_name;
                story.party_size = plan.party_size;
                story.sd_transport = plan.transport;
                story.is_night_mode = stops.Count(s => s.is_night) * 2 > stops.Count ? 1 : 0;   // 白天=0、夜間=1（story.is_night_mode），過半數的站天黑後才到就是夜間
                story.story_postcards = nodes.Count;
                story.story_badge = new List<string>();   // 寫入資料庫後依劇本內容算出可抽的勳章類別

                results.Add(new GameStoryResult
                {
                    story_no = item.story_no,
                    nt_name = condition.nt_name,
                    story = story,
                    nodes = nodes
                });
            }

            return results;
        }


        /// <summary>
        /// 寫入資料庫後的整理：可抽的勳章類別、每一站的照片（劇本檔案館的車票用）；把公車方案（含經過的站牌）掛回每個節點，
        /// 並在背景把每一站的照片做成迷霧圖。
        /// </summary>
        private async Task FinishGameStoriesAsync(List<GameStoryResult> results, bool planBusTransit)
        {
            // 剛生成的劇本每一站都還沒抵達，照片一律是迷霧圖（真實照片抵達後才給，見 StationImages）。
            // 這張照片以前做過迷霧圖就用它，沒做過先用通用迷霧圖（下面會在背景做，之後劇本詳情、地圖就會換上）
            StationImages images = await GetStationImagesAsync(results.SelectMany(r => r.nodes).Select(n => n.place_id));

            foreach (GameStoryResult result in results)
            {
                foreach (GameStoryNode node in result.nodes)
                    node.image_url = images.Of(node.place_id, arrived: false);

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
        }


        private async Task<GameStoryJobResult> FailGameJobAsync(string jobId, GameJobPlan plan, string error)
        {
            await _dao.FailGameJobAsync(jobId, error);
            return GameJobFailed(jobId, plan, error);
        }


        private HttpClient CreateGameJobClient()
        {
            HttpClient client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(1);
            return client;
        }


        private static GameStoryJobResult GameJobProcessing(string jobId, GameJobPlan plan, int finished, string stage) =>
            new() { job_id = jobId, status = GameStoryJobResult.Processing, stage = stage, total_count = plan.stories.Count, finished_count = finished };


        private static GameStoryJobResult GameJobFailed(string jobId, GameJobPlan plan, string error) =>
            new() { job_id = jobId, status = GameStoryJobResult.Failed, error = error, total_count = plan.stories.Count };


        private static GameStoryJobResult GameJobDone(string jobId, GameJobPlan plan, List<GameStoryResult> stories) =>
            new()
            {
                job_id = jobId,
                status = GameStoryJobResult.Done,
                stage = "完成",
                total_count = plan.stories.Count,
                finished_count = stories.Count,
                stories = stories
            };


        /// <summary>已存好的工作：回傳當初存下的劇本</summary>
        private static GameStoryJobResult GameJobDone(StoryDao.GameJobRow row, GameJobPlan plan) =>
            GameJobDone(row.job_id, plan, JsonSerializer.Deserialize<List<GameStoryResult>>(row.result_json, GameJobJsonOptions));


        private static string TaskTypeName(Dictionary<int, string> typeNames, int id) =>
            typeNames.TryGetValue(id, out string name) && !string.IsNullOrWhiteSpace(name) ? name
            : FallbackTypeNames.TryGetValue(id, out string fallback) ? fallback : "";


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
        /// 規劃旅遊行程：
        /// 1. 用交通等時圈找出範圍內所有景點（已去重複、不含公車）。範圍是玩家選的「最遠願意花幾分鐘」，切成近、中、遠三圈
        /// 2. 抽出 storyCount 組、每組 placesPerStory 個景點（景點夠多時各組不重複），每組各自排好順路的參觀順序
        /// 3. 從出發時間開始估算每一站幾點到，把不合適的站換成範圍內沒被抽到的景點（ItineraryRules.ArrangeByArrival）
        /// 回傳每份劇本排好的站，每一站都帶著預計抵達時間。
        /// </summary>
        private async Task<List<List<ItineraryRules.PlannedStop>>> PlanItinerariesAsync(
            double lat, double lng, List<string> transportation, int storyCount, int placesPerStory, int? maxMinutes, DateTime startTime)
        {
            // 要拿等時圈補商家景點，所以請核心一併回傳等時圈
            ReachableAttractionsResponse reachable = await FindAllReachableAttractionsAsync(new ReachableAttractionsRequest
            {
                lat = lat,
                lng = lng,
                transportation = transportation,
                contour_minutes = ItineraryRules.ContourMinutes(maxMinutes),
                include_polygons = true
            });

            await AddReachableMerchantPlacesAsync(reachable, transportation);

            if (reachable.attractions.Count < ItineraryRules.MinReachablePlaces)
            {
                int range = reachable.contour_minutes.Last();
                throw new BadRequestException(reachable.attractions.Count == 0
                    ? $"{range} 分鐘內找不到可到達的景點，請把時間調長，或換個地點、交通方式再試一次"
                    : $"{range} 分鐘內只找到 {reachable.attractions.Count} 個景點，不夠排成一份劇本，請把時間調長，或換個地點、交通方式再試一次");
            }

            List<List<ReachableAttractionNode>> sets = PickPlaceSets(reachable.attractions, storyCount, placesPerStory);

            // 沒被抽到的景點留著備用：某一站到的時候沒開、或天黑了不適合，就從這裡挑來換
            HashSet<string> picked = sets.SelectMany(set => set).Select(a => a.uid).ToHashSet();
            List<ReachableAttractionNode> spare = reachable.attractions.Where(a => !picked.Contains(a.uid)).ToList();

            Dictionary<string, List<ItineraryRules.OpenHours>> hours =
                await _placeLookup.GetOpeningHoursAsync(reachable.attractions.Select(a => a.uid));
            double speed = ItineraryRules.AverageSpeed(reachable.attractions);

            return sets
                .Select(set => ItineraryRules.ArrangeByArrival(OrderByVisit(set, lat, lng), spare, hours, startTime, speed))
                .ToList();
        }


        /// <summary>過半數的站天黑後才到，就算夜間劇本</summary>
        internal static bool MostlyNight(IEnumerable<ItineraryRules.PlannedStop> stops)
        {
            List<ItineraryRules.PlannedStop> list = stops.ToList();
            return list.Count(s => s.is_night) * 2 > list.Count;
        }


        /// <summary>
        /// 把範圍內的商家景點（現有商家的 store_uid，含自建景點與綁定的既有景點）補進行程候選。
        /// FindAllReachableAttractionsAsync 只查 :Attraction，商家自建景點（:MerchantPlace）與綁定的餐廳、旅宿都不在其中，
        /// 所以座標向 Neo4j 查（PlaceLookupService），用同一組等時圈判斷是否在範圍內、同樣用 Valhalla 算真實交通時間與圈層後再加進候選。
        /// 已停用帳號的商家不會被排入。
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


        /// <summary>
        /// AI service 的劇本任務生成：一次只收一份劇本（一組景點、一種敘事語氣），取代舊的 /api/stories/tasks/batch。
        /// 排隊模式：POST 立刻回 202 與 task_id，劇本在 AI 背景生成，用 GET /api/jobs/{task_id} 查進度與結果
        /// （AI 只把結果放在記憶體保留 1 小時，重啟或過期就查不到）
        /// </summary>
        internal const string GameStoryPath = "/api/stories/tasks";
        private const string AiJobsPath = "/api/jobs/";

        private static readonly JsonSerializerOptions AiJsonOptions = new() { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// 查詢 AI 劇本生成任務。AI 查不到任務（404）時回傳 status = not_found；
        /// AI 連不上、逾時或回 5xx 時回傳 null，當作還在處理中、下次輪詢再查
        /// </summary>
        private async Task<AiGameStoryJob> GetAiStoryJobAsync(HttpClient client, string taskId)
        {
            try
            {
                using HttpResponseMessage response = await client.GetAsync(AiServiceConfig.Url(AiJobsPath + Uri.EscapeDataString(taskId)));

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new AiGameStoryJob { task_id = taskId, status = AiGameStoryJob.NotFound };
                if ((int)response.StatusCode >= 500)
                {
                    _logger.LogWarning("查詢劇本生成進度失敗（task_id={TaskId}，HTTP {Status}），下次輪詢再查", taskId, (int)response.StatusCode);
                    return null;
                }

                return await VlogAiGateway.ReadJsonAsync<AiGameStoryJob>(response, "查詢 AI 劇本任務生成進度");
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(e, "連不上 AI 服務查詢劇本生成進度（task_id={TaskId}），下次輪詢再查", taskId);
                return null;
            }
            catch (Exception e)
            {
                // 4xx 或回傳格式無法解析：這份當作失敗，不影響其他份
                return new AiGameStoryJob { task_id = taskId, status = "error", stage = "查詢進度", error = e.Message };
            }
        }

        /// <summary>AI 任務失敗的原因；還在排隊、生成中、已經做好，或 AI 暫時連不上（job 是 null）時回傳 null</summary>
        private static string AiStoryJobError(AiGameStoryJob job)
        {
            if (job == null || job.result != null) return null;

            return (job.status ?? "").Trim().ToLowerInvariant() switch
            {
                AiGameStoryJob.NotFound => "AI 服務找不到劇本生成任務（可能已過期或 AI 服務重啟過）",
                "error" or "failed" or "failure" or "cancelled" or "canceled" => $"AI 劇本任務生成失敗（{job.stage}）：{job.error}",
                "done" or "completed" or "success" => "AI 劇本任務生成完成，但沒有回傳劇本",
                _ => null   // queued、running
            };
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