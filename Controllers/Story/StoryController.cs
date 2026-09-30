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
    /// 提供劇本生成（現在揪出發、遊你說算）、Agent 即時推薦、劇本詳情、確認選卷與附近景點查詢等功能。
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

                // AI 找不到地點時仍回 HTTP 200，只帶 message（例如「附近沒有符合您當下心情的地點。」），不能當成功
                if (agentResult.phase_3_graph_rag == null && agentResult.phase_4_action_and_tools == null)
                    throw new Exception($"AI Agent 沒有產生推薦：{VlogAiGateway.AiErrorMessage(responseString)}");


                // 新資料庫沒有 md_agent_recommendation 表，推薦結果不存檔，直接回傳（recommendation_id 固定為 null）
                return Ok(new ResultViewModel<SpinScriptResult>
                {
                    isSuccess = true,
                    message = "推薦生成成功",
                    Result = new SpinScriptResult
                    {
                        recommendation_id = null,
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



        #region 確認選卷 / 劇本進行狀態（is_playing）
        /// <summary>
        /// 玩家確認選擇指定的劇本卷，準備進入探索地圖。會將此劇本標記為「正在遊玩中」（is_playing = 1），
        /// 並自動把其他劇本重置為未進行（假設同一時間只允許一份劇本進行中）。
        /// 只有劇本擁有者或協作隊員可以確認（403），找不到劇本 404。
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
            catch (BadRequestException e)
            {
                return BadRequest(new ResultViewModel<StoryDetailResponse> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (NotFoundException e)
            {
                return NotFound(new ResultViewModel<StoryDetailResponse> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (UnauthorizedAccessException e)
            {
                return StatusCode(403, new ResultViewModel<StoryDetailResponse> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "確認選卷失敗");
                return StatusCode(500, new ResultViewModel<StoryDetailResponse> { isSuccess = false, message = e.Message, Result = null });
            }
        }


        /// <summary>
        /// 玩家完成或退出劇本時呼叫，把自己在這份劇本的遊玩紀錄（story_session）標成完成。
        /// </summary>
        /// <remarks>
        /// 劇本擁有者、協作隊員或有遊玩紀錄的人都可以呼叫。沒按過確認開始（沒有遊玩紀錄）時一樣回成功，但不會補建完成紀錄。
        /// 劇本擁有者結束時，進行中的協作隊伍一併標成完成。
        ///
        /// **Request 範例**：
        /// ```json
        /// { "story_id": 1 }
        /// ```
        /// </remarks>
        /// <response code="200">Result = 結束的劇本代號</response>
        /// <response code="400">沒有帶 story_id</response>
        /// <response code="403">沒有參與這份劇本</response>
        /// <response code="404">找不到這份劇本</response>
        [Authorize]
        [HttpPost]
        [Route("EndStory")]
        [ProducesResponseType(typeof(ResultViewModel<string>), 200)]
        public IActionResult EndStory([FromBody] StoryConfirmRequest req)
        {
            try
            {
                _service.EndStory(User.GetAuId(), req?.story_id ?? 0);
                return Ok(new ResultViewModel<string>
                {
                    isSuccess = true,
                    message = "已結束此劇本",
                    Result = req.story_id.ToString()
                });
            }
            catch (BadRequestException e)
            {
                return BadRequest(new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (NotFoundException e)
            {
                return NotFound(new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (UnauthorizedAccessException e)
            {
                return StatusCode(403, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
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
        /// 跟 GenerateGameStory 的差異：GenerateGameStory 需要前端帶定位或城市/行政區、人數、偏好等結構化參數；
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


                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMinutes(1);   // 單次請求的逾時；整個生成最多等 TextBlueprintTimeout

                GenerateScriptBlueprintByTextResponse aiResult = await RunTextBlueprintJobAsync(client, req.user_prompt);


                if (aiResult?.data == null)
                    throw new Exception("AI 服務回傳的劇本內容為空");


                string cityName = aiResult.parsed_intent?.city_name ?? "";
                string townName = aiResult.parsed_intent?.town_name ?? "";
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
         private const string TextBlueprintPath = "/api/api/admin/generate_script_blueprint_by_text";
        private static readonly TimeSpan TextBlueprintTimeout = TimeSpan.FromMinutes(10);

        /// <summary>輪詢 AI 工作的間隔，測試時可以調短</summary>
        internal static TimeSpan TextBlueprintPollInterval = TimeSpan.FromSeconds(3);

        /// <summary>
        /// 「遊你說了算」的 AI 端點是非同步的：送出後立刻回傳 job_id，劇本在背景生成；
        /// 這裡每隔幾秒查一次 GET {path}/{job_id}，拿到 data 就回傳，status = error 就把 AI 給的原因丟出來。
        /// </summary>
        private static async Task<GenerateScriptBlueprintByTextResponse> RunTextBlueprintJobAsync(HttpClient client, string userPrompt)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var content = new StringContent(JsonSerializer.Serialize(new { user_prompt = userPrompt }), System.Text.Encoding.UTF8, "application/json");

            using HttpResponseMessage submit = await client.PostAsync(AiServiceConfig.Url(TextBlueprintPath), content);
            string body = await submit.Content.ReadAsStringAsync();
            if (!submit.IsSuccessStatusCode)
                throw new Exception($"外部 AI 服務回應錯誤 (Status: {(int)submit.StatusCode}): {VlogAiGateway.AiErrorMessage(body)}");

            GenerateScriptBlueprintByTextResponse job = JsonSerializer.Deserialize<GenerateScriptBlueprintByTextResponse>(body, options);
            if (job?.data != null) return job;   // AI 若直接回傳結果就不用輪詢
            if (string.IsNullOrWhiteSpace(job?.job_id))
                throw new Exception($"AI 服務沒有回傳 job_id：{VlogAiGateway.AiErrorMessage(body)}");

            DateTime deadline = DateTime.UtcNow + TextBlueprintTimeout;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TextBlueprintPollInterval);

                using HttpResponseMessage poll = await client.GetAsync(AiServiceConfig.Url($"{TextBlueprintPath}/{Uri.EscapeDataString(job.job_id)}"));
                body = await poll.Content.ReadAsStringAsync();
                if (!poll.IsSuccessStatusCode)
                    throw new Exception($"查詢 AI 劇本生成進度失敗 (Status: {(int)poll.StatusCode}): {VlogAiGateway.AiErrorMessage(body)}");

                job = JsonSerializer.Deserialize<GenerateScriptBlueprintByTextResponse>(body, options);
                if (job?.data != null) return job;

                string status = (job?.status ?? "").Trim().ToLowerInvariant();
                if (status is "error" or "failed" or "failure")
                    throw new Exception($"AI 劇本生成失敗（{job.stage}）：{job.error}");
            }

            throw new Exception($"AI 劇本生成超過 {TextBlueprintTimeout.TotalMinutes} 分鐘仍未完成");
        }


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
 
 
 
        #region 劇本任務一次生成（AI service /api/v1/generate）
        /// <summary>
        /// 依使用者位置與交通方式自動挑景點，由 AI 一次生成 3 份劇本（含每個景點的任務）讓使用者挑，並存入資料庫。
        /// </summary>
        /// <remarks>
        /// 中心點優先使用 lat/lng（使用者當前定位）；沒有定位時改傳 city_name/town_name，後端轉成經緯度。
        /// 流程：
        /// 1. 規劃旅遊行程：3 份劇本各自一組景點（範圍內隨機抽 8 個並排好順路順序，景點夠多時各份不重複）
        ///    與一種敘事語氣（narrative_tone）。景點已去除重複，交通圈只算步行/腳踏車/機車/汽車；
        ///    同樣的條件每次生成的景點組合都不同。
        /// 2. 判斷任務類型：每站固定出創意攝影型；最後一站加跨關集結型；餐廳類加地方美食型；
        ///    2 人以上加協作解謎型；再從景點可出的文化問答型／景點猜猜樂／e人訪談型（place_type）隨機抽一題。
        ///    景點在 place_type 標有 9（商家新增題目時自動標記）時，另外必出一題商家知識問答
        ///    （直接引用題庫，不經 AI，task 帶 question_id）。商家自建的景點也會列入行程規劃候選。
        /// 3. 打包成一包丟給 AI service，一次拿回 3 份劇本。
        ///
        /// 回傳陣列，每一份都有自己的 story_id；使用者選定後用那份的 story_id 呼叫 Confirm 開始遊玩。
        /// transportation 有選「公車」、「客運」或「台灣好行」時，會找出相鄰節點之間的直達公車，
        /// 寫入 story_node_transit，並在每個節點的 transit 回傳上下車站與中間經過的所有站牌。
        ///
        /// 會寫入 story、story_tag、story_node、task、task_option、task_clue、story_node_transit。
        /// task_id 是進入節點遊玩畫面、作答時要用的任務代號；回應不含正確答案、提示與各座位線索，
        /// 抵達節點後由 GET api/Task/Node/{node_id} 取得自己座位的線索與作答方式。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "lat": 24.1477,
        ///   "lng": 120.6736,
        ///   "party_size": 2,
        ///   "transportation": ["步行", "公車"],
        ///   "preferences": ["好山好水", "美食"],
        ///   "is_night_mode": 0
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
        #endregion
    }
}
