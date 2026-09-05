using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;
using backend.dao;

namespace backend.Controllers
{
    /// <summary>
    /// 劇本產生相關 API。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class StoryController : ControllerBase
    {
        private readonly ILogger<StoryController> _logger;
        private readonly StoryService _service;
        private readonly IVlogAiClient _aiClient;
        private readonly MediaJobDao _jobDao;
        private readonly IHttpClientFactory _httpClientFactory;

        public StoryController(
            ILogger<StoryController> logger,
            StoryService service,
            IVlogAiClient aiClient,
            MediaJobDao jobDao,
            IHttpClientFactory httpClientFactory
        )
        {
            _logger = logger;
            _service = service;
            _aiClient = aiClient;
            _jobDao = jobDao;
            _httpClientFactory = httpClientFactory;
        }

        #region 來點遊意思：轉盤抽取地區

        /// <summary>
        /// 轉動命運轉盤，隨機抽取一個確定有足夠景點的地區。
        /// </summary>
        /// <remarks>
        /// 轉動命運轉盤，透過 Neo4j 圖譜服務（/api/neo4j/cypher）查詢景點數量大於等於 5 的行政區並隨機抽出一筆，若失敗則退回關聯式資料庫抽取。
        /// 
        /// Request 範例：
        /// 
        ///     POST /api/Story/Wheel/Spin
        /// 
        /// Response 範例：
        /// 
        ///     {
        ///       "isSuccess": true,
        ///       "message": "抽取成功",
        ///       "Result": {
        ///         "region_id": null,
        ///         "region": "臺南市中西區",
        ///         "city_name": "臺南市",
        ///         "district_name": "中西區"
        ///       }
        ///     }
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("Wheel/Spin")]
        public async Task<IActionResult> SpinWheel() 
        {
            try
            {
                StoryWheelSpinResponse selectedRegion = null;
                var client = _httpClientFactory.CreateClient();

                // 💡 修正：透過 substring 從 address 提取前三碼（城市）與接下來三碼（鄉鎮區），並計算景點數 >= 5
                var cypherRequest = new
                {
                    query = @"
                        MATCH (a:Attraction) 
                        WHERE a.address IS NOT NULL AND size(a.address) >= 6
                        WITH substring(a.address, 0, 3) AS city, substring(a.address, 3, 3) AS town, a 
                        WITH city, town, count(a) AS spot_count 
                        WHERE spot_count >= 5 
                        RETURN city, town
                    ",
                    parameters = new { }
                };

                var response = await client.PostAsJsonAsync("https://vlog.angelalala.com/api/neo4j/cypher", cypherRequest);
                
                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    using var document = JsonDocument.Parse(jsonString);
                    var root = document.RootElement;
                    
                    if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array && dataProp.GetArrayLength() > 0)
                    {
                        var rnd = new Random();
                        var randomIndex = rnd.Next(dataProp.GetArrayLength());
                        var selectedNode = dataProp[randomIndex];

                        string cityName = null;
                        string townName = null;

                        foreach (var prop in selectedNode.EnumerateObject())
                        {
                            string propName = prop.Name.ToLower();
                            if (propName.Contains("city"))
                            {
                                cityName = prop.Value.GetString();
                            }
                            else if (propName.Contains("town") || propName.Contains("district"))
                            {
                                townName = prop.Value.GetString();
                            }
                        }

                        if (!string.IsNullOrEmpty(cityName))
                        {
                            selectedRegion = new StoryWheelSpinResponse
                            {
                                region_id = null,
                                region = $"{cityName}{townName ?? ""}",
                                city_name = cityName,
                                district_name = townName ?? ""
                            };
                        }
                    }
                }

                // 若 Neo4j 查詢結果解析失敗或無資料，退回關聯式資料庫降級備案
                if (selectedRegion == null || string.IsNullOrEmpty(selectedRegion.city_name))
                {
                    _logger.LogWarning("[SpinWheel] Neo4j 解析無果，啟動 MySQL 降級備案");
                    selectedRegion = _service.WheelSpin();
                }

                return Ok(new ResultViewModel<StoryWheelSpinResponse>
                {
                    isSuccess = true,
                    message = "抽取成功 (來自 Neo4j 圖譜)",
                    Result = selectedRegion
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "轉盤抽取地區發生例外，強制走降級備案");
                var fallbackRegion = _service.WheelSpin();
                return Ok(new ResultViewModel<StoryWheelSpinResponse>
                {
                    isSuccess = true,
                    message = "抽取成功 (例外降級)",
                    Result = fallbackRegion
                });
            }
        }

        #endregion

        #region 現在揪出發：地區清單

        /// <summary>
        /// 取得可選擇的地區清單。
        /// </summary>
        /// <remarks>
        /// 取得可選擇的地區清單，對應「選擇」頁面「現在揪出發」輸入地區時的可選清單。
        /// 
        /// Request 範例：
        /// 
        ///     GET /api/Story/Regions?mode=NOW&amp;city_name=臺南市
        /// 
        /// Response 範例：
        /// 
        ///     {
        ///       "isSuccess": true,
        ///       "message": "查詢成功",
        ///       "Result": [
        ///         {
        ///           "region_id": "R001",
        ///           "region": "台南安平",
        ///           "city_name": "臺南市",
        ///           "district_name": "安平區"
        ///         }
        ///       ]
        ///     }
        /// </remarks>
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

        #region 產生劇本選項
        /// <summary>
        /// 【❌ 尚未完成 / 暫不使用】分享明信片至社群平台。
        /// </summary>
        /// <remarks>
        /// 對應「明信片翻轉」頁面的「分享」按鈕。
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("Generate")]
        public IActionResult Generate([FromBody] StoryGenerateRequest req)
        {
            try
            {
                return Ok(new ResultViewModel<List<StoryOptionResponse>> { isSuccess = true, message = "產生成功", Result = _service.GenerateOptions(req) });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "產生劇本選項失敗");
                return StatusCode(500, new ResultViewModel<List<StoryOptionResponse>> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion

        #region 劇情觀看更多

        /// <summary>
        /// 取得指定劇本的詳細內容。
        /// </summary>
        /// <remarks>
        /// 取得指定劇本的詳細內容，對應「劇情觀看更多」頁面，顯示劇本前傳與探索總覽。
        /// 
        /// Request 範例：
        /// 
        ///     GET /api/Story/AI_A1B2C3D4/Detail
        /// 
        /// Response 範例：
        /// 
        ///     {
        ///       "isSuccess": true,
        ///       "message": "查詢成功",
        ///       "Result": {
        ///         "story_id": "AI_A1B2C3D4",
        ///         "title": "台南的秘密花園",
        ///         "preface": "在台南中西區，傳統工藝老師傅阿吉伯邀請我們..."
        ///       }
        ///     }
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

        #region 確認選卷

        /// <summary>
        /// 確認選擇指定劇本卷。
        /// </summary>
        /// <remarks>
        /// 確認選擇指定劇本卷，對應「劇情觀看更多」頁面的「確認此卷」按鈕，確認後即進入探索地圖。
        /// 
        /// Request 範例：
        /// 
        ///     POST /api/Story/Confirm
        ///     {
        ///       "story_id": "AI_A1B2C3D4"
        ///     }
        /// 
        /// Response 範例：
        /// 
        ///     {
        ///       "isSuccess": true,
        ///       "message": "確認選卷成功，即將進入探索地圖",
        ///       "Result": {
        ///         "story_id": "AI_A1B2C3D4",
        ///         "title": "台南的秘密花園"
        ///       }
        ///     }
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

        // =========================================================
        // AI 專屬同步生成劇本 API
        // =========================================================

        #region AI 專屬劇本生成

        /// <summary>
        /// 送出客製化劇本需求，讓 Python AI 開始生成。
        /// </summary>
        /// <remarks>
        /// 營運人員/玩家輸入條件，AI 會自動挑選符合的景點、排定動線，並生成完整的實境解謎劇本企劃書。
        /// 對應 Python API 規格書的第 9 點 (`/api/admin/generate_script_blueprint`)。
        /// 
        /// Request 範例：
        /// 
        ///     POST /api/Story/GenerateAi
        ///     {
        ///       "city_name": "臺南市",
        ///       "town_name": "中西區",
        ///       "traveler_count": 2,
        ///       "preferences": ["科幻", "歷史懸疑"],
        ///       "transportation": ["步行", "公車"],
        ///       "node_count": 4,
        ///       "is_night": false,
        ///       "story_count": 3
        ///     }
        /// 
        /// Response 範例：
        /// 
        ///     {
        ///       "isSuccess": true,
        ///       "message": "專屬劇本生成完畢！",
        ///       "Result": {
        ///         "status": "Completed",
        ///         "stories": [
        ///             { "story_id": "AI_A1B2C3D4", "title": "生成出的劇本標題1" },
        ///             { "story_id": "AI_E5F6G7H8", "title": "生成出的劇本標題2" }
        ///         ]
        ///       }
        ///     }
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("GenerateAi")]
        public async Task<IActionResult> GenerateAiStory([FromBody] StoryGenerateRequest req)
        {
            try
            {
                string epId = User.FindFirst("ep_id")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(epId)) return Unauthorized(new ResultViewModel<string> { isSuccess = false, message = "無法驗證身分" });

                // 1. 整理傳入參數 (已清理多餘欄位，並加上 story_count)
                string targetCity = !string.IsNullOrEmpty(req.city_name) ? req.city_name : "臺南市";
                string targetTown = !string.IsNullOrEmpty(req.town_name) ? req.town_name : "中西區";
                int pSize = req.traveler_count > 0 ? req.traveler_count : 2;
                int nCount = req.node_count > 0 ? req.node_count : 4;
                int sCount = req.story_count > 0 ? req.story_count : 1; // 預設生成1個，根據傳入值決定
                var prefs = req.preferences ?? new List<string>();
                var trans = req.transportation ?? new List<string>();
                string fullRegion = $"{targetCity}{targetTown}";

                // 2. 組裝要傳給 Python 的 Payload
                var payloadToPython = new
                {
                    city_name = targetCity,
                    town_name = targetTown,
                    traveler_count = pSize,
                    preferences = prefs,
                    transportation = trans,
                    node_count = nCount,
                    is_night = req.is_night,
                    story_count = sCount // 新增：傳遞要生成的劇本數量
                };

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMinutes(2); // 拉長逾時時間等待 AI 運算

                var jsonContent = new StringContent(JsonSerializer.Serialize(payloadToPython), System.Text.Encoding.UTF8, "application/json");

                // 3. 打擊 Python 服務並等待完整結果回傳
                var response = await client.PostAsync("https://vlog.angelalala.com/api/admin/generate_script_blueprint", jsonContent);

                if (!response.IsSuccessStatusCode)
                {
                    string errContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"外部 AI 服務回應錯誤 (Status: {response.StatusCode}): {errContent}");
                }

                string responseString = await response.Content.ReadAsStringAsync();
                
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                
                // 4. 直接反序列化為 AiStoryResult
                AiStoryResult aiResult = null;
                try
                {
                    aiResult = JsonSerializer.Deserialize<AiStoryResult>(responseString, options);
                }
                catch (Exception ex)
                {
                    throw new Exception($"反序列化失敗: {ex.Message}。原始回應：{responseString}");
                }

                if (aiResult == null || aiResult.Data == null)
                {
                    throw new Exception($"AI 服務回傳的劇本內容為空。原始回應：{responseString}");
                }

                // 🌟 5. 立即寫入 MySQL 資料庫 (這裡已經改成呼叫支援多筆寫入的 SaveAiGeneratedStories)
                var savedStories = _service.SaveAiGeneratedStories(epId, fullRegion, aiResult);

                // 🌟 6. 回傳成功與「所有」新生成的劇本清單給前端
                return Ok(new ResultViewModel<object>
                {
                    isSuccess = true,
                    message = "專屬劇本生成完畢！",
                    Result = new 
                    { 
                        status = "Completed", 
                        stories = savedStories // 這會是一個陣列，例如 [{story_id: "...", title: "..."}]
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
    }
}