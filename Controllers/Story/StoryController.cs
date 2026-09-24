// 檔案路徑：System\Controllers\Story\StoryController.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.utils;
using backend.ViewModels;



namespace backend.Controllers
{
    /// <summary>
    /// 劇本生成與相關操作 API。
    /// 提供 Agent 即時推薦、GPS 定位生成劇本、觀看劇本詳情、Neo4j 景點查詢及確認選卷等功能。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class StoryController : ControllerBase
    {
        private readonly ILogger<StoryController> _logger;
        private readonly StoryService _service;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly GeocodingService _geocodingService;
        private readonly TaskGenerationService _taskGeneration;

        public StoryController(
            ILogger<StoryController> logger,
            StoryService service,
            IHttpClientFactory httpClientFactory,
            GeocodingService geocodingService,
            TaskGenerationService taskGeneration)
        {
            _logger = logger;
            _service = service;
            _httpClientFactory = httpClientFactory;
            _geocodingService = geocodingService;
            _taskGeneration = taskGeneration;
        }



        #region 文字轉劇本 (spin) — 改用 /api/agent/orchestrate
        public class SpinScriptRequest
        {
            /// <summary>使用者語音/文字輸入內容</summary>
            public string input_text { get; set; }


            /// <summary>情緒標籤，例如「疲憊」、「興奮」，不帶時預設為「平靜」</summary>
            public string emotion_label { get; set; }


            /// <summary>前端傳入的城市名稱，例如「臺中市」</summary>
            public string city_name { get; set; }


            /// <summary>前端傳入的行政區名稱，例如「北區」</summary>
            public string town_name { get; set; }
        }



        /// <summary>
        /// 接收前端傳入的語音文字、情緒標籤與城市/行政區，
        /// 後端先將城市/行政區轉換為經緯度，再透過 AI Agent 服務即時推薦附近地點與任務，並存入資料庫。
        /// </summary>
        /// <remarks>
        /// 跟舊版差異：舊版是「生成多節點完整劇本」；這支是「單一地點即時推薦 + 單一任務」，
        /// 回傳結構完全不同（含行事曆同步連結、社群分享連結），存進獨立的 md_agent_recommendation 表，
        /// 不寫入 md_story/md_story_node。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "input_text": "我走得好累又好熱，想找個有冷氣的地方休息一下",
        ///   "emotion_label": "疲憊",
        ///   "city_name": "臺中市",
        ///   "town_name": "北區"
        /// }
        /// ```
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("spin")]
        [ProducesResponseType(typeof(ResultViewModel<SpinScriptResult>), 200)]
        public async Task<IActionResult> SpinScript([FromBody] SpinScriptRequest req)
        {
            try
            {
                if (req == null || string.IsNullOrWhiteSpace(req.input_text))
                {
                    return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供輸入文字 (input_text)" });
                }


                int auId = User.GetAuId();


                if (string.IsNullOrWhiteSpace(req.city_name))
                    return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供城市名稱 (city_name)" });


                // 城市 + 行政區 → 經緯度（正向地理編碼，沿用既有的 GeocodingService）
                var geoResult = await _geocodingService.SearchPlaceCoordinatesAsync(req.town_name ?? "", req.city_name);
                if (!geoResult.lat.HasValue || !geoResult.lng.HasValue)
                {
                    return BadRequest(new ResultViewModel<string>
                    {
                        isSuccess = false,
                        message = $"無法將「{req.city_name}{req.town_name}」轉換為經緯度，請確認地名是否正確"
                    });
                }


                var payloadToAgent = new AgentOrchestrateRequest
                {
                    user_voice_transcript = req.input_text,
                    emotion_label = string.IsNullOrWhiteSpace(req.emotion_label) ? "平靜" : req.emotion_label,
                    user_lat = geoResult.lat.Value,
                    user_lon = geoResult.lng.Value
                };


                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMinutes(3);


                var jsonContent = new StringContent(JsonSerializer.Serialize(payloadToAgent), System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync($"{AiServiceConfig.BaseUrl}/api/agent/orchestrate", jsonContent);


                if (!response.IsSuccessStatusCode)
                {
                    string errContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"外部 AI Agent 服務回應錯誤 (Status: {response.StatusCode}): {errContent}");
                }


                string responseString = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var agentResult = JsonSerializer.Deserialize<AgentOrchestrateResponse>(responseString, options);


                if (agentResult == null)
                    throw new Exception("AI Agent 服務回傳的內容為空");


                string recommendationId = _service.SaveAgentRecommendation(
                    auId.ToString(), req.city_name, req.town_name ?? "", geoResult.lat.Value, geoResult.lng.Value, agentResult);


                return Ok(new ResultViewModel<SpinScriptResult>
                {
                    isSuccess = true,
                    message = "推薦生成成功",
                    Result = new SpinScriptResult
                    {
                        recommendation_id = recommendationId,
                        agent_result = agentResult
                    }
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "文字生成劇本 (spin) 失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion





        #region Neo4j 附近景點查詢（依 GPS 座標與半徑）
        /// <summary>
        /// 依使用者 GPS 座標，透過 Neo4j 查詢半徑範圍內的景點，依距離由近到遠排序。
        /// </summary>
        /// <remarks>
        /// 資料來源：Neo4j 開放資料（全台灣景點，跟劇本/遊戲進度無關，資料量大且座標準確）。
        /// 適合用途：地圖探索、景點推薦，這種不需要綁定特定劇本節點的通用查詢。
        /// 查無結果時代表「這附近真的沒有登錄在 Neo4j 的景點」，可視為正常情況。
        ///
        /// **Request 範例**：
        ///
        ///     GET /api/Story/NearbyAttractions?lat=22.9908&amp;lng=120.2034&amp;radiusKm=1
        /// </remarks>
        [Authorize]
        [HttpGet]
        [Route("NearbyAttractions")]
        [ProducesResponseType(typeof(ResultViewModel<List<NearbyAttractionNode>>), 200)]
        public async Task<IActionResult> GetNearbyAttractions([FromQuery] double lat, [FromQuery] double lng, [FromQuery] double radiusKm = 1.0)
        {
            try
            {
                var result = await _service.GetNearbyAttractionsAsync(lat, lng, radiusKm);
                return Ok(new ResultViewModel<List<NearbyAttractionNode>> { isSuccess = true, message = "查詢成功", Result = result });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢附近景點失敗");
                return StatusCode(500, new ResultViewModel<List<NearbyAttractionNode>> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion



        #region GPS 定位生成劇本 — 改成前端傳城市/行政區，後端自行轉經緯度
        public class StoryGenerateByLocationRequest
        {
            /// <summary>前端傳入的城市名稱，例如「臺南市」</summary>
            public string city_name { get; set; }


            /// <summary>前端傳入的行政區名稱，例如「中西區」（必填，外部 AI 服務需要明確行政區才能定位生成劇本）</summary>
            public string town_name { get; set; }


            public int traveler_count { get; set; }
            public List<string> preferences { get; set; }
            public List<string> transportation { get; set; }
            public int node_count { get; set; }
            public bool is_night { get; set; }
            public int story_count { get; set; }
        }



        /// <summary>
        /// 依前端傳入的城市/行政區名稱，後端先轉換為經緯度（供回應與未來地圖使用），
        /// 再打 AI 服務生成劇本，並完整回傳與外部 API 100% 相同結構的劇本內容。支援 story_count 一次生成多份。
        /// </summary>
        /// <remarks>
        /// 跟舊版差異：舊版是前端傳 GPS 座標、後端反向地理編碼查出城市/行政區；
        /// 現在改成前端直接傳城市/行政區、後端正向地理編碼轉出經緯度，省去一次反查、也更準確。
        /// 節點座標查詢策略維持不變：優先比對 Neo4j 真實景點資料，查無結果才退回 Nominatim。
        ///
        /// city_name、town_name 皆為必填：外部 AI 服務需要明確的行政區才能定位生成劇本，
        /// 只給城市會導致外部服務回應「找不到 地點資料」的錯誤，故在此提早擋下，回傳 400。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "city_name": "臺南市",
        ///   "town_name": "中西區",
        ///   "traveler_count": 2,
        ///   "preferences": ["解謎深入", "文學建築"],
        ///   "transportation": ["步行", "公車"],
        ///   "node_count": 4,
        ///   "is_night": false,
        ///   "story_count": 3
        /// }
        /// ```
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("GenerateByLocation")]
        [ProducesResponseType(typeof(ResultViewModel<GenerateByLocationResult>), 200)]
        public async Task<IActionResult> GenerateByLocation([FromBody] StoryGenerateByLocationRequest req)
        {
            try
            {
                int auId = User.GetAuId();


                if (string.IsNullOrWhiteSpace(req.city_name))
                    return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供城市名稱 (city_name)" });

                if (string.IsNullOrWhiteSpace(req.town_name))
                    return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供行政區名稱 (town_name)，僅有城市無法生成劇本" });


                string cityName = req.city_name;
                string townName = req.town_name;


                // 城市 + 行政區 → 經緯度（正向地理編碼，取代原本的反向地理編碼）
                var geoResult = await _geocodingService.SearchPlaceCoordinatesAsync(townName, cityName);
                double lat = geoResult.lat ?? 0;
                double lng = geoResult.lng ?? 0;


                string regionId = _service.FindRegionIdByName(cityName, townName) ?? "";


                int storyCount = req.story_count > 0 ? req.story_count : 1;


                var payloadToPython = new
                {
                    city_name = cityName,
                    town_name = townName,
                    traveler_count = req.traveler_count > 0 ? req.traveler_count : 2,
                    preferences = req.preferences ?? new List<string>(),
                    transportation = req.transportation ?? new List<string>(),
                    node_count = req.node_count > 0 ? req.node_count : 4,
                    is_night = req.is_night
                };


                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMinutes(10);


                string jsonPayload = JsonSerializer.Serialize(payloadToPython);
                var allResults = new List<GeneratedStoryItem>();
                var newStoryIds = new List<string>();


                for (int i = 0; i < storyCount; i++)
                {
                    var jsonContent = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                    var response = await client.PostAsync($"{AiServiceConfig.BaseUrl}/api/admin/generate_script_blueprint", jsonContent);


                    if (!response.IsSuccessStatusCode)
                    {
                        string errContent = await response.Content.ReadAsStringAsync();
                        throw new Exception($"外部 AI 服務回應錯誤 (第 {i + 1} 份): {errContent}");
                    }


                    string responseString = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };


                    ScriptBlueprintApiResponse aiResult;
                    try
                    {
                        aiResult = JsonSerializer.Deserialize<ScriptBlueprintApiResponse>(responseString, options);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"反序列化失敗 (第 {i + 1} 份): {ex.Message}");
                    }


                    if (aiResult?.data == null)
                    {
                        _logger.LogWarning($"第 {i + 1} 份 AI 劇本回傳內容為空，已跳過此份");
                        continue;
                    }


                    int newStoryId = await _service.SaveFullAiGeneratedStory(auId, cityName, townName, aiResult.data);
                    // TaskGenerationService 仍吃字串型別的 story_id，這裡先轉字串保持相容。
                    newStoryIds.Add(newStoryId.ToString());


                    allResults.Add(new GeneratedStoryItem
                    {
                        story_id = newStoryId,
                        status = aiResult.status,
                        data = aiResult.data
                    });
                }

                // 劇本存檔後，接著為所有節點生成任務並寫入 md_task。
                // 此方法內部已容錯，不會丟例外，任務生成失敗不影響劇本生成結果。
                int taskPlayerCount = req.traveler_count > 0 ? req.traveler_count : 2;
                await _taskGeneration.GenerateTasksForStoriesAsync(newStoryIds, taskPlayerCount);

                return Ok(new ResultViewModel<GenerateByLocationResult>
                {
                    isSuccess = true,
                    message = $"劇本生成完畢！共生成 {allResults.Count} 份",
                    Result = new GenerateByLocationResult
                    {
                        lat = lat,
                        lng = lng,
                        detected_city = cityName,
                        detected_town = townName,
                        stories = allResults
                    }
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "依城市/行政區生成劇本失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion



        #region GPS 附近地點查詢（依距離排序，MySQL md_place 版本）
        /// <summary>
        /// 【前端不用接】依玩家 GPS 座標，查詢方圓範圍內的景點（來源：MySQL md_place），依距離由近到遠排序。
        /// </summary>
        /// <remarks>
        /// **前端不用接**：資料只包含「曾經生成過劇本」的地點，查附近景點請改用 ReachableAttractions（有去除重複、實際交通時間）。
        ///
        /// 資料來源：你自己的 md_place 表，只包含「曾經透過 GenerateByLocation 生成過劇本節點」的地點，
        /// 不是全台灣景點資料庫。查無結果通常代表這附近從來沒有人生成過劇本，屬於正常情況。
        /// 如果要做「這附近有什麼景點可以玩」這種通用探索功能，請改用 NearbyAttractions。
        ///
        /// **Request 範例**：
        ///
        ///     GET /api/Story/NearbyPlaces?lat=22.9908&amp;lng=120.2034&amp;radiusKm=2
        /// </remarks>
        [Authorize]
        [HttpGet]
        [Route("NearbyPlaces")]
        [ProducesResponseType(typeof(ResultViewModel<List<NearbyPlaceDistanceResponse>>), 200)]
        public IActionResult NearbyPlaces([FromQuery] double lat, [FromQuery] double lng, [FromQuery] double radiusKm = 2.0)
        {
            try
            {
                var result = _service.GetNearbyPlacesByDistance(lat, lng, radiusKm);
                return Ok(new ResultViewModel<List<NearbyPlaceDistanceResponse>> { isSuccess = true, message = "查詢成功", Result = result });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢附近地點失敗");
                return StatusCode(500, new ResultViewModel<List<NearbyPlaceDistanceResponse>> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion



        #region 劇情觀看更多 (Detail)
        /// <summary>
        /// 取得指定劇本的詳細內容（含各節點地點名稱、任務提示、對應 NPC）。
        /// </summary>
        /// <remarks>
        /// **Request 範例**：
        ///
        ///     GET /api/Story/{story_id}/Detail
        /// </remarks>
        [Authorize]
        [HttpGet]
        [Route("{story_id:int}/Detail")]
        [ProducesResponseType(typeof(ResultViewModel<StoryDetailResponse>), 200)]
        public IActionResult Detail(int story_id)
        {
            try
            {
                return Ok(new ResultViewModel<StoryDetailResponse> { isSuccess = true, message = "查詢成功", Result = _service.GetDetail(story_id) });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢劇本詳情失敗");
                return StatusCode(500, new ResultViewModel<StoryDetailResponse> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion



        #region 完整劇本詳情（欄位對照 AI 原始格式，不遺失任何欄位）
        /// <summary>
        /// 依 story_id 取得完整劇本內容，欄位結構與 AI 原始生成格式 100% 相同（含 task_type、is_night_mode 等完整欄位）。
        /// </summary>
        /// <remarks>
        /// **Request 範例**：
        ///
        ///     GET /api/Story/{story_id}/FullDetail
        /// </remarks>
        [Authorize]
        [HttpGet]
        [Route("{story_id:int}/FullDetail")]
        [ProducesResponseType(typeof(ResultViewModel<ScriptBlueprintData>), 200)]
        public IActionResult FullDetail(int story_id)
        {
            try
            {
                var result = _service.GetFullDetail(story_id);
                return Ok(new ResultViewModel<ScriptBlueprintData> { isSuccess = true, message = "查詢成功", Result = result });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢完整劇本詳情失敗");
                return StatusCode(500, new ResultViewModel<ScriptBlueprintData> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion



        #region 確認選卷 / 劇本進行狀態（is_playing）
        /// <summary>
        /// 玩家確認選擇指定的劇本卷，準備進入探索地圖。會將此劇本標記為「正在遊玩中」（is_playing = 1），
        /// 並自動把其他劇本重置為未進行（假設同一時間只允許一份劇本進行中）。
        /// </summary>
        /// <remarks>
        /// **Request 範例**：
        /// ```json
        /// { "story_id": "AI_3F2A9C1B" }
        /// ```
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("Confirm")]
        [ProducesResponseType(typeof(ResultViewModel<StoryDetailResponse>), 200)]
        public IActionResult Confirm([FromBody] StoryConfirmRequest req)
        {
            try
            {
                StoryDetailResponse detail = _service.ConfirmStory(User.GetAuId(), req);
                return Ok(new ResultViewModel<StoryDetailResponse> { isSuccess = true, message = "確認選卷成功，即將進入探索地圖", Result = detail });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "確認選卷失敗");
                return StatusCode(500, new ResultViewModel<StoryDetailResponse> { isSuccess = false, message = e.Message, Result = null });
            }
        }


        /// <summary>
        /// 玩家完成或退出劇本時呼叫，把該劇本的進行狀態改回未進行（is_playing = 0）。
        /// </summary>
        /// <remarks>
        /// **Request 範例**：
        /// ```json
        /// { "story_id": "AI_3F2A9C1B" }
        /// ```
        /// </remarks>
        /// <response code="200">Result = 結束的劇本代號；isSuccess = false 代表找不到該劇本</response>
        [Authorize]
        [HttpPost]
        [Route("EndStory")]
        [ProducesResponseType(typeof(ResultViewModel<string>), 200)]
        public IActionResult EndStory([FromBody] StoryConfirmRequest req)
        {
            try
            {
                bool result = _service.EndStory(User.GetAuId(), req.story_id);
                return Ok(new ResultViewModel<string>
                {
                    isSuccess = result,
                    message = result ? "已結束此劇本的進行狀態" : $"找不到 story_id = {req.story_id} 的劇本",
                    Result = req.story_id.ToString()
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "結束劇本進行狀態失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }


        /// <summary>
        /// 查詢目前正在進行中的劇本是哪一個（is_playing = 1 的那筆）。
        /// </summary>
        /// <remarks>
        /// **Request 範例**：
        ///
        ///     GET /api/Story/CurrentPlaying
        /// </remarks>
        /// <response code="200">沒有進行中的劇本時 Result 為 null</response>
        [Authorize]
        [HttpGet]
        [Route("CurrentPlaying")]
        [ProducesResponseType(typeof(ResultViewModel<CurrentPlayingStory>), 200)]
        public IActionResult GetCurrentPlaying()
        {
            try
            {
                var story = _service.GetCurrentPlayingStory(User.GetAuId());
                return Ok(new ResultViewModel<CurrentPlayingStory>
                {
                    isSuccess = true,
                    message = story == null ? "目前沒有進行中的劇本" : "取得成功",
                    Result = story
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢進行中劇本失敗");
                return StatusCode(500, new ResultViewModel<object> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion



        #region 自然語言生成劇本（遊你說了算）
        public class GenerateByTextRequest
        {
            /// <summary>使用者輸入的一句話描述，例如「想來一趟美食之旅」</summary>
            public string user_prompt { get; set; }
        }


        /// <summary>
        /// 接收使用者輸入的一句自然語言描述，由外部 AI 服務自動解析出城市/行政區/人數/偏好等條件，
        /// 並直接生成完整劇本、寫入資料庫。對應前端「遊你說了算」功能。
        /// </summary>
        /// <remarks>
        /// 跟 GenerateByLocation 的差異：GenerateByLocation 需要前端明確帶城市/行政區/偏好等結構化參數；
        /// 這支只需要一句話，解析與生成都在外部 AI 服務端完成，後端只負責存檔。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "user_prompt": "我們想要兩個人去臺南安平區來趟深度文學解謎之旅，大約走四個站點"
        /// }
        /// ```
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("GenerateByText")]
        [ProducesResponseType(typeof(ResultViewModel<GenerateByTextResult>), 200)]
        public async Task<IActionResult> GenerateByText([FromBody] GenerateByTextRequest req)
        {
            try
            {
                int auId = User.GetAuId();


                if (string.IsNullOrWhiteSpace(req?.user_prompt))
                    return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供描述文字 (user_prompt)" });


                var payload = new { user_prompt = req.user_prompt };


                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMinutes(10);


                var jsonContent = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync($"{AiServiceConfig.BaseUrl}/api/api/admin/generate_script_blueprint_by_text", jsonContent);


                if (!response.IsSuccessStatusCode)
                {
                    string errContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"外部 AI 服務回應錯誤 (Status: {response.StatusCode}): {errContent}");
                }


                string responseString = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };


                GenerateScriptBlueprintByTextResponse aiResult;
                try
                {
                    aiResult = JsonSerializer.Deserialize<GenerateScriptBlueprintByTextResponse>(responseString, options);
                }
                catch (Exception ex)
                {
                    throw new Exception($"反序列化失敗: {ex.Message}");
                }


                if (aiResult?.data == null)
                    throw new Exception("AI 服務回傳的劇本內容為空");


                string cityName = aiResult.parsed_intent?.city_name ?? "";
                string townName = aiResult.parsed_intent?.town_name ?? "";
                string regionId = _service.FindRegionIdByName(cityName, townName) ?? "";


                int newStoryId = await _service.SaveFullAiGeneratedStory(auId, cityName, townName, aiResult.data);


                return Ok(new ResultViewModel<GenerateByTextResult>
                {
                    isSuccess = true,
                    message = "劇本生成成功",
                    Result = new GenerateByTextResult
                    {
                        story_id = newStoryId,
                        detected_city = cityName,
                        detected_town = townName,
                        parsed_intent = aiResult.parsed_intent,
                        data = aiResult.data
                    }
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "自然語言生成劇本失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion
         #region 交通等時圈：依交通方式查詢可到達的景點與真實交通時間（Valhalla）
        /// <summary>
        /// 依中心點與交通方式，找出可到達的景點，並回傳每個景點的真實交通時間、距離、所在圈層。
        /// </summary>
        /// <remarks>
        /// 中心點優先使用 lat/lng（使用者當前定位）；沒有定位時改傳 city_name/town_name，後端轉成經緯度。
        /// 交通方式可複選，每個景點取最快可到達的那一種；公車/捷運不列入計算。
        /// 重複的景點（同名且相近、或座標幾乎相同）會合併成一筆。
        /// 一律以整天行程計算（10/20/30 分鐘圈），範圍內各圈層輪流隨機抽 8 個景點，每次呼叫結果都不同；
        /// 抽完後依順路的參觀順序排好（從中心點出發，不走回頭路）。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "lat": 24.1477,
        ///   "lng": 120.6736,
        ///   "transportation": ["步行", "機車"],
        ///   "include_polygons": false
        /// }
        /// ```
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("ReachableAttractions")]
        [ProducesResponseType(typeof(ResultViewModel<ReachableAttractionsResponse>), 200)]
        public async Task<IActionResult> ReachableAttractions([FromBody] ReachableAttractionsRequest req)
        {
            try
            {
                if (req == null)
                    return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供查詢條件" });
 
                // 沒有 GPS 定位時，用城市/行政區轉經緯度當中心點
                if (req.lat == 0 || req.lng == 0)
                {
                    if (string.IsNullOrWhiteSpace(req.city_name))
                        return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供 lat/lng，或提供 city_name" });
 
                    var geoResult = await _geocodingService.SearchPlaceCoordinatesAsync(req.town_name ?? "", req.city_name);
                    if (!geoResult.lat.HasValue || !geoResult.lng.HasValue)
                    {
                        return BadRequest(new ResultViewModel<string>
                        {
                            isSuccess = false,
                            message = $"無法將「{req.city_name}{req.town_name}」轉換為經緯度，請確認地名是否正確"
                        });
                    }
 
                    req.lat = geoResult.lat.Value;
                    req.lng = geoResult.lng.Value;
                }
 
                ReachableAttractionsResponse result = await _service.GetReachableAttractionsAsync(req);
 
                return Ok(new ResultViewModel<ReachableAttractionsResponse>
                {
                    isSuccess = true,
                    message = $"查詢成功，推薦 {result.attractions.Count} 個景點（範圍內共 {result.total_reachable} 個）",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "交通等時圈查詢失敗");
                return StatusCode(500, new ResultViewModel<ReachableAttractionsResponse> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion
 
 
 
        #region 路線：依序經過多個點的實際路線與時間（Valhalla）
        /// <summary>
        /// 依序經過多個點，回傳每一段的交通方式、真實時間、距離與路線座標。
        /// </summary>
        /// <remarks>
        /// 第一個點是起點。交通方式可複選，每一段會各自挑最快的方式。
        /// coordinates 是 [經度, 緯度] 清單，前端依序連線就能畫出路線。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "points": [
        ///     { "lat": 24.1477, "lng": 120.6736, "name": "我的位置" },
        ///     { "lat": 24.1572, "lng": 120.6660, "name": "國立自然科學博物館" },
        ///     { "lat": 24.1411, "lng": 120.6634, "name": "國立臺灣美術館" }
        ///   ],
        ///   "transportation": ["步行", "機車"]
        /// }
        /// ```
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("TravelRoute")]
        [ProducesResponseType(typeof(ResultViewModel<TravelRouteResponse>), 200)]
        public async Task<IActionResult> TravelRoute([FromBody] TravelRouteRequest req)
        {
            try
            {
                if (req?.points == null || req.points.Count < 2)
                    return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供至少 2 個點（起點與終點）" });
 
                TravelRouteResponse result = await _service.GetTravelRouteAsync(req);
 
                return Ok(new ResultViewModel<TravelRouteResponse>
                {
                    isSuccess = true,
                    message = $"路線規劃完成，共 {result.legs.Count} 段，約 {result.total_minutes} 分鐘",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "路線規劃失敗");
                return StatusCode(500, new ResultViewModel<TravelRouteResponse> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion



        #region 劇本任務一次生成（AI service /api/v1/generate）
        /// <summary>
        /// 依使用者位置與交通方式自動挑景點，由 AI 一次生成 3 份劇本（含每個景點的任務）讓使用者挑，並存入資料庫。
        /// </summary>
        /// <remarks>
        /// 中心點優先使用 lat/lng（使用者當前定位）；沒有定位時改傳 city_name/town_name，後端轉成經緯度。
        /// 3 份劇本各自一組景點（範圍內隨機抽 8 個並排好順路順序，景點夠多時各份不重複）與一種敘事語氣（narrative_tone）。
        /// 景點已去除重複，交通圈只算步行/腳踏車/機車/汽車；同樣的條件每次生成的景點組合都不同。
        /// 每個景點可出的任務類型來自 place_type 表；查不到時預設「創意攝影型、文化問答型、協作解謎型」，
        /// 1 人時不出協作解謎型。
        ///
        /// 回傳陣列，每一份都有自己的 story_id；使用者選定後用那份的 story_id 呼叫 Confirm 開始遊玩。
        /// transportation 有選「公車」、「客運」或「台灣好行」時，會找出相鄰節點之間的直達公車，
        /// 寫入 story_node_transit，並在每個節點的 transit 回傳上下車站與中間經過的所有站牌。
        ///
        /// 會寫入 story、story_tag、story_node、task、task_option、task_clue、story_node_transit。
        /// task_db_id 是作答時要用的任務代號。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "lat": 24.1477,
        ///   "lng": 120.6736,
        ///   "party_size": 2,
        ///   "transportation": ["步行", "公車"],
        ///   "preferences": ["好山好水", "美食"],
        ///   "is_night_mode": 2
        /// }
        /// ```
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("GenerateGameStory")]
        [ProducesResponseType(typeof(ResultViewModel<List<GameStoryResult>>), 200)]
        public async Task<IActionResult> GenerateGameStory([FromBody] GameStoryGenerateRequest req)
        {
            try
            {
                if (req == null)
                    return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供查詢條件" });

                int auId = User.GetAuId();

                if (req.lat == 0 || req.lng == 0)
                {
                    // 沒有 GPS 定位時，用城市/行政區轉經緯度當中心點
                    if (string.IsNullOrWhiteSpace(req.city_name))
                        return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供 lat/lng，或提供 city_name" });

                    var geoResult = await _geocodingService.SearchPlaceCoordinatesAsync(req.town_name ?? "", req.city_name);
                    if (!geoResult.lat.HasValue || !geoResult.lng.HasValue)
                    {
                        return BadRequest(new ResultViewModel<string>
                        {
                            isSuccess = false,
                            message = $"無法將「{req.city_name}{req.town_name}」轉換為經緯度，請確認地名是否正確"
                        });
                    }

                    req.lat = geoResult.lat.Value;
                    req.lng = geoResult.lng.Value;
                }
                else if (string.IsNullOrWhiteSpace(req.city_name) || string.IsNullOrWhiteSpace(req.town_name))
                {
                    // 有定位但沒帶城市/行政區時，反查補上（AI 生成劇本需要）
                    var (cityName, districtName) = await _geocodingService.ResolveTaiwanAreaAsync(req.lat, req.lng);
                    req.city_name = string.IsNullOrWhiteSpace(req.city_name) ? cityName : req.city_name;
                    req.town_name = string.IsNullOrWhiteSpace(req.town_name) ? districtName : req.town_name;
                }

                List<GameStoryResult> result = await _service.GenerateGameStoryAsync(auId, req);

                return Ok(new ResultViewModel<List<GameStoryResult>>
                {
                    isSuccess = true,
                    message = $"劇本生成成功，共 {result.Count} 份劇本",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "劇本任務生成失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }


        /// <summary>
        /// 取得劇本節點之間的公車交通方案（搭哪一路、哪站上下車、中間經過的所有站牌）。
        /// </summary>
        /// <remarks>
        /// 只有生成劇本時選了公車/客運/台灣好行、而且找得到直達公車的路段才會有資料。
        ///
        /// **Request 範例**：
        ///
        ///     GET /api/Story/{story_id}/Transit
        /// </remarks>
        [Authorize]
        [HttpGet]
        [Route("{story_id:int}/Transit")]
        [ProducesResponseType(typeof(ResultViewModel<List<BusTransitLeg>>), 200)]
        public async Task<IActionResult> GetStoryTransit(int story_id)
        {
            try
            {
                List<BusTransitLeg> result = await _service.GetStoryTransitAsync(story_id);
                return Ok(new ResultViewModel<List<BusTransitLeg>>
                {
                    isSuccess = true,
                    message = result.Count == 0 ? "此劇本沒有公車交通方案" : $"查詢成功，共 {result.Count} 段公車",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢劇本公車交通方案失敗");
                return StatusCode(500, new ResultViewModel<List<BusTransitLeg>> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion
    }
}
