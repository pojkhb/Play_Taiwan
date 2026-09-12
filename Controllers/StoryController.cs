// 檔案路徑：System\Controllers\StoryController.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
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


        public StoryController(
            ILogger<StoryController> logger,
            StoryService service,
            IHttpClientFactory httpClientFactory,
            GeocodingService geocodingService)
        {
            _logger = logger;
            _service = service;
            _httpClientFactory = httpClientFactory;
            _geocodingService = geocodingService;
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
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "推薦生成成功",
        ///   "Result": {
        ///     "recommendation_id": "REC_3F2A9C1B",
        ///     "agent_result": {
        ///       "phase_3_graph_rag": { "recommended_spot": { "name": "一中商圈" } },
        ///       "phase_4_action_and_tools": { "script_blueprint": { "theme_title": "霓霓的秘密料理之謎" } }
        ///     }
        ///   }
        /// }
        /// ```
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("spin")]
        public async Task<IActionResult> SpinScript([FromBody] SpinScriptRequest req)
        {
            try
            {
                if (req == null || string.IsNullOrWhiteSpace(req.input_text))
                {
                    return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供輸入文字 (input_text)" });
                }

                string epId = User.FindFirst("ep_id")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(epId))
                    return Unauthorized(new ResultViewModel<string> { isSuccess = false, message = "無法驗證身分" });

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
                var response = await client.PostAsync("https://vlog.angelalala.com/api/agent/orchestrate", jsonContent);

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
                    epId, req.city_name, req.town_name ?? "", geoResult.lat.Value, geoResult.lng.Value, agentResult);

                return Ok(new ResultViewModel<object>
                {
                    isSuccess = true,
                    message = "推薦生成成功",
                    Result = new
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


        #region Neo4j 抓取當地景點 API
        /// <summary>
        /// 透過 Neo4j 取得指定地區的景點名稱清單（依關鍵字模糊比對名稱/城市/地區）。
        /// </summary>
        /// <remarks>
        /// 資料來源：Neo4j 開放資料（全台灣景點），不需先生成劇本，隨時可查。
        /// 不帶 city_name 時，只會隨機回傳前 10 筆景點名稱。
        ///
        /// **Request 範例**：
        ///
        ///     GET /api/Story/Attractions?city_name=台南
        /// </remarks>
        [Authorize]
        [HttpGet]
        [Route("Attractions")]
        public async Task<IActionResult> GetAttractions([FromQuery] string city_name = "")
        {
            try
            {
                var client = _httpClientFactory.CreateClient();

                string cypherQuery;
                object parameters;

                if (string.IsNullOrEmpty(city_name))
                {
                    cypherQuery = "MATCH (a:Attraction) RETURN a.name LIMIT 10";
                    parameters = new { };
                }
                else
                {
                    cypherQuery = @"
                        MATCH (a:Attraction) 
                        WHERE a.name CONTAINS $keyword 
                           OR a.city CONTAINS $keyword 
                           OR a.region CONTAINS $keyword 
                        RETURN a.name 
                        LIMIT 15;
                    ";
                    parameters = new { keyword = city_name };
                }

                var payloadToNeo4j = new
                {
                    query = cypherQuery,
                    parameters = parameters
                };

                var jsonContent = new StringContent(JsonSerializer.Serialize(payloadToNeo4j), System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://vlog.angelalala.com/api/neo4j/cypher", jsonContent);

                if (!response.IsSuccessStatusCode)
                    throw new Exception("呼叫 Neo4j API 失敗");

                string responseString = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;
                var attractionNames = new List<string>();

                if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in dataElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("a.name", out var nameElement))
                        {
                            string name = nameElement.GetString();
                            if (!string.IsNullOrEmpty(name))
                            {
                                attractionNames.Add(name);
                            }
                        }
                    }
                }

                return Ok(new ResultViewModel<List<string>>
                {
                    isSuccess = true,
                    message = "查詢景點成功",
                    Result = attractionNames
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ResultViewModel<object> { isSuccess = false, message = ex.Message, Result = null });
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

            /// <summary>前端傳入的行政區名稱，例如「中西區」</summary>
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
        public async Task<IActionResult> GenerateByLocation([FromBody] StoryGenerateByLocationRequest req)
        {
            try
            {
                string epId = User.FindFirst("ep_id")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(epId))
                    return Unauthorized(new ResultViewModel<string> { isSuccess = false, message = "無法驗證身分" });

                if (string.IsNullOrWhiteSpace(req.city_name))
                    return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供城市名稱 (city_name)" });

                string cityName = req.city_name;
                string townName = req.town_name ?? "";

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
                var allResults = new List<object>();

                for (int i = 0; i < storyCount; i++)
                {
                    var jsonContent = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                    var response = await client.PostAsync("https://vlog.angelalala.com/api/admin/generate_script_blueprint", jsonContent);

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

                    string newStoryId = await _service.SaveFullAiGeneratedStory(epId, regionId, cityName, aiResult.data);

                    allResults.Add(new
                    {
                        story_id = newStoryId,
                        status = aiResult.status,
                        data = aiResult.data
                    });
                }

                return Ok(new ResultViewModel<object>
                {
                    isSuccess = true,
                    message = $"劇本生成完畢！共生成 {allResults.Count} 份",
                    Result = new
                    {
                        lat,
                        lng,
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
        /// 依玩家 GPS 座標，查詢方圓範圍內的景點（來源：MySQL md_place），依距離由近到遠排序。
        /// </summary>
        /// <remarks>
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
        [Route("{story_id}/Detail")]
        public IActionResult Detail(string story_id)
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
        [Route("{story_id}/FullDetail")]
        public IActionResult FullDetail(string story_id)
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


        #region 確認選卷
        /// <summary>
        /// 玩家確認選擇指定的劇本卷，準備進入探索地圖。
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
        public IActionResult Confirm([FromBody] StoryConfirmRequest req)
        {
            try
            {
                StoryDetailResponse detail = _service.ConfirmStory(req);
                return Ok(new ResultViewModel<StoryDetailResponse> { isSuccess = true, message = "確認選卷成功，即將進入探索地圖", Result = detail });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "確認選卷失敗");
                return StatusCode(500, new ResultViewModel<StoryDetailResponse> { isSuccess = false, message = e.Message, Result = null });
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
public async Task<IActionResult> GenerateByText([FromBody] GenerateByTextRequest req)
{
    try
    {
        string epId = User.FindFirst("ep_id")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(epId))
            return Unauthorized(new ResultViewModel<string> { isSuccess = false, message = "無法驗證身分" });

        if (string.IsNullOrWhiteSpace(req?.user_prompt))
            return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供描述文字 (user_prompt)" });

        var payload = new { user_prompt = req.user_prompt };

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(10);

        var jsonContent = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("https://vlog.angelalala.com/api/api/admin/generate_script_blueprint_by_text", jsonContent);

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

        string newStoryId = await _service.SaveFullAiGeneratedStory(epId, regionId, cityName, aiResult.data);

        return Ok(new ResultViewModel<object>
        {
            isSuccess = true,
            message = "劇本生成成功",
            Result = new
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
    }
}