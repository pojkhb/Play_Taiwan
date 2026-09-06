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

        public StoryController(
            ILogger<StoryController> logger,
            StoryService service,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _service = service;
            _httpClientFactory = httpClientFactory;
        }

        #region 文字轉劇本 (spin)
        public class SpinScriptRequest
        {
            /// <summary>前端傳入的自訂文字或提示</summary>
            public string input_text { get; set; }
        }

        /// <summary>
        /// 接收前端傳入的文字，透過 AI 生成專屬劇本。
        /// </summary>
        /// <remarks>
        /// 對應 spin 轉盤或文字輸入功能，把玩家輸入的文字當作強烈的偏好條件餵給 AI，產生真實的劇本。
        /// 
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "input_text": "在台北101發生一場神祕的相遇"
        /// }
        /// ```
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "劇本生成成功",
        ///   "Result": {
        ///     "story_id": "AI_A86E6246",
        ///     "title": "在台北101發生一場..."
        ///   }
        /// }
        /// ```
        /// </remarks>
        /// <param name="req">包含玩家輸入自訂文字的請求物件。</param>
        /// <returns>生成成功的劇本 ID 與標題。</returns>
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
        /// 透過 Neo4j 取得指定地區的景點清單。
        /// </summary>
        /// <remarks>
        /// 前端可傳入地區名稱(例如: 臺南市 或 太平山)，後端會去打 Neo4j Cypher 進行模糊搜尋。
        /// 若不傳入 city_name，則預設隨機抓取 10 個景點。
        /// 
        /// **Request 範例**：
        /// 
        ///     GET /api/Story/Attractions?city_name=太平山
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢景點成功",
        ///   "Result": [
        ///     "太平山國家森林遊樂區"
        ///   ]
        /// }
        /// ```
        /// </remarks>
        /// <param name="city_name">欲查詢的城市或關鍵字 (選填)。</param>
        /// <returns>符合條件的景點名稱字串陣列。</returns>
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
                    // 沒輸入條件時，預設抓 10 個
                    cypherQuery = "MATCH (a:Attraction) RETURN a.name LIMIT 10";
                    parameters = new {};
                }
                else
                {
                    // 有輸入條件時，同時比對名稱、城市或地區是否包含該關鍵字
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

        #region AI 專屬劇本生成 
        /// <summary>
        /// 根據條件生成多份 AI 實境解謎劇本。
        /// </summary>
        /// <remarks>
        /// 營運人員/玩家輸入條件，AI 會自動挑選符合的景點、排定動線，並生成完整的實境解謎劇本企劃書。
        /// 支援多筆劇本生成 (透過 `story_count` 參數控制)。
        /// 
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "city_name": "臺南市",
        ///   "town_name": "中西區",
        ///   "traveler_count": 2,
        ///   "preferences": ["科幻", "歷史懸疑"],
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
        ///   "message": "專屬劇本生成完畢！共生成 3 份",
        ///   "Result": {
        ///     "status": "Completed",
        ///     "stories": [
        ///         { "story_id": "AI_A1B2C3D4", "title": "生成出的劇本標題1" },
        ///         { "story_id": "AI_E5F6G7H8", "title": "生成出的劇本標題2" },
        ///         { "story_id": "AI_I9J0K1L2", "title": "生成出的劇本標題3" }
        ///     ]
        ///   }
        /// }
        /// ```
        /// </remarks>
        /// <param name="req">生成劇本的條件與需求 (包含預期生成數量)。</param>
        /// <returns>所有生成的劇本列表。</returns>
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
                        throw new Exception($"外部 AI 服務回應錯誤 (第 {i+1} 份): {errContent}");
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
                        throw new Exception($"反序列化失敗 (第 {i+1} 份): {ex.Message}");
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

        #region 現在揪出發：地區清單
        /// <summary>
        /// 取得可選擇的地區清單。
        /// </summary>
        /// <remarks>
        /// 取得可選擇的地區清單，對應「選擇」頁面「現在揪出發」輸入地區時的可選清單。支援模糊搜尋城市名稱。
        /// 
        /// **Request 範例**：
        /// 
        ///     GET /api/Story/Regions?mode=NOW&amp;city_name=臺南市
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": [
        ///     {
        ///       "region_id": "R001",
        ///       "region": "台南安平",
        ///       "city_name": "臺南市",
        ///       "district_name": "安平區"
        ///     }
        ///   ]
        /// }
        /// ```
        /// </remarks>
        /// <param name="mode">搜尋模式，預設為 NOW。</param>
        /// <param name="city_name">欲查詢的城市名稱 (可模糊搜尋)。</param>
        /// <returns>符合條件的地區清單陣列。</returns>
        [Authorize]
        [HttpGet]
        [Route("Regions")]
        public IActionResult Regions([FromQuery] string mode = "NOW", [FromQuery] string city_name = "")
        {
            try
            {
                return Ok(new ResultViewModel<List<StoryWheelSpinResponse>> { isSuccess = true, message = "查詢成功", Result = _service.GetRegions(mode, city_name) });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢地區清單失敗");
                return StatusCode(500, new ResultViewModel<List<StoryWheelSpinResponse>> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion

        #region 劇情觀看更多 (Detail)
        /// <summary>
        /// 取得指定劇本的詳細內容 (包含 NPC 與景點資訊)。
        /// </summary>
        /// <remarks>
        /// 對應「劇情觀看更多」頁面，顯示劇本前傳、探索總覽、以及所有關聯的景點、NPC 介紹、任務提示等。
        /// 
        /// **Request 範例**：
        /// 
        ///     GET /api/Story/AI_A1B2C3D4/Detail
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": {
        ///     "story_id": "AI_A1B2C3D4",
        ///     "title": "台南的秘密花園",
        ///     "preface": "在台南中西區，傳統工藝老師傅阿吉伯邀請我們...",
        ///     "nodes": [
        ///       {
        ///         "order": 1,
        ///         "place_name": "振來發餅舖",
        ///         "task_description": "尋找隱藏的線索",
        ///         "location_codename": "鐘樓",
        ///         "opening": "我們來到了振來發，傳統餅舖的秘密將被發現...",
        ///         "success": "我們發現了傳統餅舖的秘密！...",
        ///         "npc_name": "阿吉伯"
        ///       }
        ///     ]
        ///   }
        /// }
        /// ```
        /// </remarks>
        /// <param name="story_id">欲查詢的劇本唯一識別碼 (story_id)。</param>
        /// <returns>該劇本的詳細內容、景點節點及 NPC 資訊。</returns>
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

        #region 確認選卷
        /// <summary>
        /// 確認選擇指定劇本卷。
        /// </summary>
        /// <remarks>
        /// 對應「劇情觀看更多」頁面的「確認此卷」按鈕，確認後即進入探索地圖。
        /// 
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "story_id": "AI_A1B2C3D4"
        /// }
        /// ```
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "確認選卷成功，即將進入探索地圖",
        ///   "Result": {
        ///     "story_id": "AI_A1B2C3D4",
        ///     "title": "台南的秘密花園",
        ///     "preface": "在台南中西區..."
        ///   }
        /// }
        /// ```
        /// </remarks>
        /// <param name="req">包含使用者確認選擇的 story_id 物件。</param>
        /// <returns>確認選卷的結果及劇本基本資訊。</returns>
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