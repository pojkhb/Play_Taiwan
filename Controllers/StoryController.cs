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
    /// 提供前端取得地區、生成劇本、觀看劇本詳情、Neo4j景點查詢及確認選卷等功能。
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


        #region 文字轉劇本 (spin)
        public class SpinScriptRequest
        {
            /// <summary>前端傳入的自訂文字或提示</summary>
            public string input_text { get; set; }
        }


        /// <summary>
        /// 接收前端傳入的一段文字或關鍵字，透過外部 AI 服務生成一份專屬劇本，並寫入資料庫。
        /// 目前固定使用「臺南市中西區」作為生成地區。
        /// </summary>
        /// <remarks>
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "input_text": "我想要一場浪漫又帶點懸疑的約會路線"
        /// }
        /// ```
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "劇本生成成功",
        ///   "Result": {
        ///     "story_id": "AI_3F2A9C1B",
        ///     "title": "月光下的府城密語"
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


                var payloadToPython = new
                {
                    city_name = "臺南市",
                    town_name = "中西區",
                    traveler_count = 2,
                    preferences = new List<string> { req.input_text, "隨機驚喜" },
                    transportation = new List<string> { "步行" },
                    node_count = 3,
                    is_night = false
                };


                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMinutes(3);


                var jsonContent = new StringContent(JsonSerializer.Serialize(payloadToPython), System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://vlog.angelalala.com/api/admin/generate_script_blueprint", jsonContent);


                if (!response.IsSuccessStatusCode)
                {
                    string errContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"外部 AI 服務回應錯誤 (Status: {response.StatusCode}): {errContent}");
                }


                string responseString = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var aiResult = JsonSerializer.Deserialize<AiStoryResult>(responseString, options);


                if (aiResult == null || aiResult.Data == null)
                    throw new Exception("AI 服務回傳的劇本內容為空");


                var savedStories = _service.SaveAiGeneratedStories(epId, "臺南市中西區", aiResult);


                return Ok(new ResultViewModel<object>
                {
                    isSuccess = true,
                    message = "劇本生成成功",
                    Result = new
                    {
                        story_id = savedStories[0]["story_id"],
                        title = savedStories[0]["title"]
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
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢景點成功",
        ///   "Result": ["臺南孔子廟", "赤崁樓", "安平古堡"]
        /// }
        /// ```
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
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": [
        ///     { "name": "台南武德殿", "lat": 22.99067, "lon": 120.20344, "distance_m": 15.04 },
        ///     { "name": "臺南孔子廟", "lat": 22.99055, "lon": 120.20409, "distance_m": 75.98 }
        ///   ]
        /// }
        /// ```
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


        #region AI 專屬劇本生成 
        /// <summary>
        /// 根據指定城市/鄉鎮與偏好條件，透過外部 AI 服務生成多份實境解謎劇本，並寫入資料庫。
        /// </summary>
        /// <remarks>
        /// 未帶 city_name/town_name 時，預設使用「臺南市中西區」。story_count 可一次生成多份不同劇本。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "city_name": "臺南市",
        ///   "town_name": "中西區",
        ///   "traveler_count": 2,
        ///   "preferences": ["文化古蹟", "美食"],
        ///   "transportation": ["步行", "公車"],
        ///   "node_count": 4,
        ///   "is_night": false,
        ///   "story_count": 2
        /// }
        /// ```
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "專屬劇本生成完畢！共生成 2 份",
        ///   "Result": {
        ///     "status": "Completed",
        ///     "stories": [
        ///       { "story_id": "AI_1A2B3C4D", "title": "府城慢遊記" },
        ///       { "story_id": "AI_5E6F7A8B", "title": "巷弄裡的老靈魂" }
        ///     ]
        ///   }
        /// }
        /// ```
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("GenerateAi")]
        public async Task<IActionResult> GenerateAiStory([FromBody] StoryGenerateRequest req)
        {
            try
            {
                string epId = User.FindFirst("ep_id")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(epId))
                    return Unauthorized(new ResultViewModel<string> { isSuccess = false, message = "無法驗證身分" });


                string targetCity = !string.IsNullOrEmpty(req.city_name) ? req.city_name : "臺南市";
                string targetTown = !string.IsNullOrEmpty(req.town_name) ? req.town_name : "中西區";
                int pSize = req.traveler_count > 0 ? req.traveler_count : 2;
                int nCount = req.node_count > 0 ? req.node_count : 4;
                int sCount = req.story_count > 0 ? req.story_count : 1;


                var prefs = req.preferences ?? new List<string>();
                var trans = req.transportation ?? new List<string>();
                string fullRegion = $"{targetCity}{targetTown}";


                var payloadToPython = new
                {
                    city_name = targetCity,
                    town_name = targetTown,
                    traveler_count = pSize,
                    preferences = prefs,
                    transportation = trans,
                    node_count = nCount,
                    is_night = req.is_night
                };


                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMinutes(10);


                string jsonPayload = JsonSerializer.Serialize(payloadToPython);
                var allSavedStories = new List<Dictionary<string, string>>();


                for (int i = 0; i < sCount; i++)
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


                    AiStoryResult aiResult;
                    try
                    {
                        aiResult = JsonSerializer.Deserialize<AiStoryResult>(responseString, options);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"反序列化失敗 (第 {i + 1} 份): {ex.Message}");
                    }


                    if (aiResult != null && aiResult.Data != null)
                    {
                        var savedStories = _service.SaveAiGeneratedStories(epId, fullRegion, aiResult);
                        allSavedStories.AddRange(savedStories);
                    }
                }


                return Ok(new ResultViewModel<object>
                {
                    isSuccess = true,
                    message = $"專屬劇本生成完畢！共生成 {allSavedStories.Count} 份",
                    Result = new
                    {
                        status = "Completed",
                        stories = allSavedStories
                    }
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "同步生成 AI 劇本失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion


        #region GPS 定位生成劇本（完整流程：定位 → 反向地理編碼 → 打 AI → 完整回傳，支援一次生成多份）
        /// <summary>
        /// 依玩家目前 GPS 座標，透過反向地理編碼查出所在縣市/鄉鎮，打 AI 服務生成劇本，
        /// 並完整回傳與外部 API 100% 相同結構的劇本內容（不遺失任何欄位）。支援 story_count 一次生成多份。
        /// </summary>
        /// <remarks>
        /// 流程：1. 前端傳入 GPS 座標 → 2. 後端呼叫共用 GeocodingService 反向地理編碼
        /// 取得縣市/鄉鎮（只反查一次，多份劇本共用同一個地區結果，不會重複打地理編碼 API）
        /// → 3. 依 story_count 迴圈打 /api/admin/generate_script_blueprint → 4. 完整存入資料庫並原樣回傳全部結果。
        /// 節點座標查詢策略：優先比對 Neo4j 真實景點資料（準確），查無結果才退回 Nominatim（可能不準）。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "lat": 22.9908,
        ///   "lng": 120.2034,
        ///   "traveler_count": 2,
        ///   "preferences": ["解謎深入", "文學建築"],
        ///   "transportation": ["步行", "公車"],
        ///   "node_count": 4,
        ///   "is_night": false,
        ///   "story_count": 3
        /// }
        /// ```
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "劇本生成完畢！共生成 3 份",
        ///   "Result": {
        ///     "detected_city": "臺南市",
        ///     "detected_town": "中西區",
        ///     "stories": [
        ///       { "story_id": "AI_A86E6246", "status": "success", "data": { ... } }
        ///     ]
        ///   }
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


                // 反向地理編碼只做一次，多份劇本共用同一個地區結果
                var (cityName, townName) = await _geocodingService.ResolveTaiwanAreaAsync(req.lat, req.lng);
                if (string.IsNullOrEmpty(cityName))
                    return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "無法從 GPS 座標判斷所在地區，請確認座標是否正確" });


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
                        lat = req.lat,
                        lng = req.lng,
                        detected_city = cityName,
                        detected_town = townName,
                        stories = allResults
                    }
                });


            }
            catch (Exception e)
            {
                _logger.LogError(e, "依 GPS 定位生成劇本失敗");
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
        /// 不是全台灣景點資料庫，範圍遠比 Neo4j 小。
        /// 適合用途：判斷玩家是否接近「劇本裡指定的節點」，屬於遊戲進度綁定的查詢，不是通用景點搜尋。
        /// 查無結果通常代表：這附近從來沒有人生成過劇本、或該筆資料座標有誤已被清除，這是正常情況，不代表 API 壞了。
        /// 如果要做「這附近有什麼景點可以玩」這種通用探索功能，請改用 NearbyAttractions。
        ///
        /// **Request 範例**：
        ///
        ///     GET /api/Story/NearbyPlaces?lat=22.9908&amp;lng=120.2034&amp;radiusKm=2
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": [
        ///     { "place_id": "P_A1B2C3D4", "place_name": "臺南孔子廟", "location_codename": "", "distance_km": 0.32 }
        ///   ]
        /// }
        /// ```
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
        ///     GET /api/Story/AI_3F2A9C1B/Detail
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": {
        ///     "story_id": "AI_3F2A9C1B",
        ///     "title": "月光下的府城密語",
        ///     "subtitle": "AI 智能生成劇本",
        ///     "preface": "夜色降臨，府城的巷弄開始說起古老的故事...",
        ///     "nodes": [
        ///       { "order": 1, "place_name": "臺南孔子廟", "task_description": "尋找刻在石碑上的謎語", "npc_name": "導覽嚮導" }
        ///     ]
        ///   }
        /// }
        /// ```
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
        /// 跟 Detail 的差異：Detail 只回傳精簡的前端顯示用欄位；FullDetail 回傳與外部 AI 服務原始藍圖一致的完整結構，
        /// 適合需要重新渲染劇本細節（例如任務類型判斷、夜間模式判斷）的情境使用。
        ///
        /// **Request 範例**：
        ///
        ///     GET /api/Story/AI_3F2A9C1B/FullDetail
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": {
        ///     "title": "月光下的府城密語",
        ///     "is_night_mode": true,
        ///     "nodes": [
        ///       { "node_order": 1, "place_name": "臺南孔子廟", "task_type": "解謎", "node_title": "石碑之謎" }
        ///     ]
        ///   }
        /// }
        /// ```
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
        /// {
        ///   "story_id": "AI_3F2A9C1B"
        /// }
        /// ```
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "確認選卷成功，即將進入探索地圖",
        ///   "Result": {
        ///     "story_id": "AI_3F2A9C1B",
        ///     "title": "月光下的府城密語"
        ///   }
        /// }
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
    }
}