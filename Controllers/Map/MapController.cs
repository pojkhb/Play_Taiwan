using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Models;
using backend.Services;
using backend.util;
using backend.ViewModels;
using System.Threading.Tasks;

namespace backend.Controllers
{
    /// <summary>
    /// 地圖、節點導航與周邊探索相關 API。
    /// 對應頁面：地圖、周邊好去、任務。
    /// </summary>

    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class MapController : ControllerBase
    {
        private readonly ILogger<MapController> _logger;
        private readonly MapService _service;
        private readonly NearbyFoodService _nearbyFood;
        private readonly NearbyPlaceService _nearbyPlaces;
        private readonly WeatherService _weather;
        private readonly NpcReminderService _reminders;

        public MapController(
            ILogger<MapController> logger,
            MapService service,
            NearbyFoodService nearbyFood,
            NearbyPlaceService nearbyPlaces,
            WeatherService weather,
            NpcReminderService reminders)
        {
            _logger = logger;
            _service = service;
            _nearbyFood = nearbyFood;
            _nearbyPlaces = nearbyPlaces;
            _weather = weather;
            _reminders = reminders;
        }

        /// <summary>還在迷霧中的站：回傳 403</summary>
        private ObjectResult Locked<T>(NodeLockedException e) =>
            StatusCode(403, new ResultViewModel<T> { isSuccess = false, message = e.Message, Result = default });

        #region 取得地圖

        /// <summary>
        /// 取得指定劇本的地圖資訊。
        /// </summary>
        /// <remarks>
        /// 對應「地圖」頁面，顯示地點探索度、已收集節點與路線。
        /// 只有劇本擁有者或協作隊員可以查看（403）；節點座標依景點 uid 向 Neo4j 查。
        ///
        /// 地圖像藏寶圖一樣一關一關開：第一站固定開放，破完這一站（任務全部通過；沒有任務的站抵達就算）才會開下一站。
        /// 迷霧由後端處理，前端照欄位顯示即可：
        ///
        /// - 已解鎖（is_unlocked = true）：location_name 是站名、lat/lng 是精確位置；
        ///   請以它為中心把半徑 reveal_radius_m 公尺的迷霧散開
        /// - 照片 image_url：抵達過的站（is_arrived = true）才是景點照片；還沒抵達的站一律是迷霧圖，
        ///   包含已解鎖但還沒到的目前這一關（真實照片一看就知道答案）
        /// - 目前這一關（is_current = true，同一時間只有一站，也就是 current_node_id）：地圖上要亮起來的那一站
        /// - 地標名稱：還沒抵達的站用 codename（地點代號）；抵達後（is_arrived = true）才有 place_name（景點真實名稱）
        /// - 未解鎖（is_unlocked = false）：location_name 是迷霧提示（地點代號）、image_url 是迷霧圖、
        ///   lat/lng 是迷霧中心（大概位置），請以它為中心畫半徑 fog_radius_m 公尺的迷霧；真正的點在這個範圍內
        ///
        /// 未解鎖的站不能查看詳情、互動、導航或打卡，會回傳 403。
        /// 作答回傳 node_completed = true 時代表這一站破關了，請重新呼叫這支與 POST /api/Route/Plan 取得下一站。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Map/{story_id}
        /// </remarks>
        /// <param name="story_id">劇本代號。</param>
        /// <returns>地圖節點與探索進度資訊。</returns>
        // API：取得地圖（GetMap）－回傳指定劇本的地圖節點與探索進度
        [HttpGet("{story_id:int}")]
        [ProducesResponseType(typeof(ResultViewModel<MapResponse>), 200)]
        public async Task<IActionResult> GetMap(int story_id)
        {
            try
            {
                MapResponse result = await _service.GetMap(story_id, User, $"{Request.Scheme}://{Request.Host}");

                return Ok(new ResultViewModel<MapResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (UnauthorizedAccessException e)
            {
                return StatusCode(403, new ResultViewModel<MapResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "取得地圖失敗，story_id: {StoryId}", story_id);

                return BadRequest(new ResultViewModel<MapResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region GPS 確認抵達

        /// <summary>
        /// 使用 GPS 座標確認探員已抵達指定節點。
        /// </summary>
        /// <remarks>
        /// 對應「地圖」頁面點擊節點後的抵達解鎖流程，成功後解鎖該節點詳情。
        /// 只有劇本擁有者或協作隊員可以抵達（403）；只能抵達已解鎖的站（還在迷霧中回 403），且要在景點 200 公尺內（400）。
        /// 抵達後才能做這一站的任務；任務全部通過才會解鎖下一站。進度是協作隊伍全隊共用的。
        /// 抵達就散開這一站的迷霧：回傳的 image_url 是景點的真實照片，播開場劇情時直接換上，不用等任務做完
        /// （之後 GET /api/Map/{story_id} 這一站的 image_url 也是真實照片）。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Map/Node/{node_id}/Arrive?lat=25.0330&amp;lng=121.5654
        /// </remarks>
        /// <param name="node_id">節點代號。</param>
        /// <param name="lat">目前緯度。</param>
        /// <param name="lng">目前經度。</param>
        /// <returns>解鎖成功後的節點詳情。</returns>
        ///[❌]
        // API：GPS 確認抵達（Arrive）－驗證座標後解鎖指定節點
        [HttpPost("Node/{node_id:int}/Arrive")]
        [ProducesResponseType(typeof(ResultViewModel<NodeDetailResponse>), 200)]
        public async Task<IActionResult> Arrive(
            int node_id,
            [FromQuery] double lat,
            [FromQuery] double lng)
        {
            try
            {
                NodeDetailResponse result =
                    await _service.ArriveNode(node_id, lat, lng, User);
                result.npc_avatar_url = PublicUrl.Of(Request, result.npc_avatar_url);
                result.image_url = PublicUrl.Of(Request, result.image_url);

                return Ok(new ResultViewModel<NodeDetailResponse>
                {
                    isSuccess = true,
                    message = "解鎖成功",
                    Result = result
                });
            }
            catch (UnauthorizedAccessException e)
            {
                return StatusCode(403, new ResultViewModel<NodeDetailResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
            catch (NodeLockedException e)
            {
                return Locked<NodeDetailResponse>(e);
            }
            catch (InvalidOperationException e)
            {
                return BadRequest(new ResultViewModel<NodeDetailResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "節點抵達失敗，node_id: {NodeId}", node_id);

                return BadRequest(new ResultViewModel<NodeDetailResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 取得節點詳情

        /// <summary>
        /// 取得指定節點的詳細內容。
        /// </summary>
        /// <remarks>
        /// 對應「地圖」頁面節點彈窗（如臺南孔廟）的詳情內容。
        /// 還在迷霧中的站回 403。已解鎖但還沒抵達的站（目前這一關）只給站名與 NPC：
        /// intro_story 是「到了現場，這裡的故事才會揭曉。」、opening_hours 是 null，抵達後才給景點介紹與營業時間。
        /// image_url：抵達過是景點照片，還沒抵達是迷霧圖。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Map/Node/{node_id}
        /// </remarks>
        /// <param name="node_id">節點代號。</param>
        /// <returns>節點詳細內容。</returns>
        // API：取得節點詳情（GetNode）－回傳指定節點的詳細內容
        [HttpGet("Node/{node_id:int}")]
        [ProducesResponseType(typeof(ResultViewModel<NodeDetailResponse>), 200)]
        public async Task<IActionResult> GetNode(int node_id)
        {
            try
            {
                NodeDetailResponse result =
                    await _service.GetNodeDetail(node_id, User);
                result.npc_avatar_url = PublicUrl.Of(Request, result.npc_avatar_url);
                result.image_url = PublicUrl.Of(Request, result.image_url);

                return Ok(new ResultViewModel<NodeDetailResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (NodeLockedException e)
            {
                return Locked<NodeDetailResponse>(e);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "取得節點詳情失敗，node_id: {NodeId}", node_id);

                return BadRequest(new ResultViewModel<NodeDetailResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region NPC 對話（回顧劇情）

        /// <summary>
        /// 取得指定節點的 NPC 對話：這一站的背景圖、NPC 與台詞。
        /// </summary>
        /// <remarks>
        /// 對應「地圖」頁面節點彈窗中「回顧劇情」的畫面。
        /// **內容是固定的，不是隨機的**：npc_dialogue 是這一站的開場白（story_node.sn_opening_text，跟
        /// GET /api/Task/Node/{node_id} 的 node.opening_text 是同一段），同一站每次呼叫都一樣；
        /// NPC 是這份劇本的 NPC（名稱、身分、圖片），要唸出來時把 npc_dialogue 和 npc_name 帶給 POST /api/Npc/Speak 的 text 和 npc_name。
        /// 這一站任務全部通過後的完成劇情在 GET /api/Task/Node/{node_id} 的 node.success_text。
        ///
        /// 還在迷霧中的站回 403。已解鎖但還沒抵達的站（目前這一關）：scene_image_url 是迷霧圖、
        /// location_subtitle 是 null，抵達後才給景點的真實照片與副標題。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Map/Node/{node_id}/Interact
        /// </remarks>
        /// <param name="node_id">節點代號。</param>
        /// <returns>NPC 互動內容。</returns>
        // API：NPC 對話（Interact）－回傳指定節點的背景圖、NPC 與這一站固定的開場白
        [HttpGet("Node/{node_id:int}/Interact")]
        [ProducesResponseType(typeof(ResultViewModel<NpcInteractionResponse>), 200)]
        public async Task<IActionResult> Interact(int node_id)
        {
            try
            {
                NpcInteractionResponse result =
                    await _service.GetNpcInteraction(node_id, User);
                result.npc_avatar_url = PublicUrl.Of(Request, result.npc_avatar_url);
                result.scene_image_url = PublicUrl.Of(Request, result.scene_image_url);

                return Ok(new ResultViewModel<NpcInteractionResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (NodeLockedException e)
            {
                return Locked<NpcInteractionResponse>(e);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "NPC 互動失敗，node_id: {NodeId}", node_id);

                return BadRequest(new ResultViewModel<NpcInteractionResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 導航

        /// <summary>
        /// 取得前往指定節點的導航資訊。
        /// </summary>
        /// <remarks>
        /// 對應「地圖」頁面節點彈窗的「開始導航」按鈕。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Map/Navigate
        ///     {
        ///       "node_id": 1
        ///     }
        /// </remarks>
        /// <param name="req">導航請求，包含目標節點代號。</param>
        /// <returns>導航路線資訊。</returns>
        // API：導航（Navigate）－回傳前往指定節點的路線資訊
        [HttpPost("Navigate")]
        [ProducesResponseType(typeof(ResultViewModel<NavigationResponse>), 200)]
        public async Task<IActionResult> Navigate([FromBody] NavigationRequest req)
        {
            try
            {
                NavigationResponse result =
                    await _service.GetNavigation(req, User);

                return Ok(new ResultViewModel<NavigationResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (NodeLockedException e)
            {
                return Locked<NavigationResponse>(e);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "導航失敗，node_id: {NodeId}", req?.node_id);

                return BadRequest(new ResultViewModel<NavigationResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 示範模式

        /// <summary>
        /// 【示範用】直接破這一關：不檢查 GPS、不用作答。
        /// </summary>
        /// <remarks>
        /// 展示藏寶圖一關一關開的時候用，坐在電腦前就能破關。把這一站當成已抵達、任務全部通過，回傳更新後的地圖（下一站解鎖）。
        /// **只有後端設定 Demo:AllowStageSkip = true 時能用**（地圖回傳的 demo_mode 會是 true），預設關閉、正式環境回 403。
        /// 一樣只能破已解鎖的站（不能跳關），而且只有劇本擁有者或協作隊員能用。不會產生作答紀錄。
        ///
        ///     POST /api/Map/Node/{node_id}/DemoClear
        /// </remarks>
        /// <param name="node_id">要破關的節點（目前這一關）。</param>
        [HttpPost("Node/{node_id:int}/DemoClear")]
        [ProducesResponseType(typeof(ResultViewModel<MapResponse>), 200)]
        public Task<IActionResult> DemoClear(int node_id) =>
            Demo(() => _service.DemoClearNode(node_id, User, $"{Request.Scheme}://{Request.Host}"), "示範破關成功");

        /// <summary>
        /// 【示範用】這份劇本回到第一關，可以重新展示一次。
        /// </summary>
        /// <remarks>
        /// 任務改回未通過、進度回到還沒抵達任何一站；作答紀錄、明信片、勳章不動。
        /// **只有後端設定 Demo:AllowStageSkip = true 時能用**，預設關閉、正式環境回 403。
        ///
        ///     POST /api/Map/{story_id}/DemoReset
        /// </remarks>
        /// <param name="story_id">劇本代號。</param>
        [HttpPost("{story_id:int}/DemoReset")]
        [ProducesResponseType(typeof(ResultViewModel<MapResponse>), 200)]
        public Task<IActionResult> DemoReset(int story_id) =>
            Demo(() => _service.DemoResetStory(story_id, User, $"{Request.Scheme}://{Request.Host}"), "已回到第一關");

        private async Task<IActionResult> Demo(Func<Task<MapResponse>> action, string message)
        {
            try
            {
                return Ok(new ResultViewModel<MapResponse> { isSuccess = true, message = message, Result = await action() });
            }
            catch (UnauthorizedAccessException e)
            {
                return StatusCode(403, new ResultViewModel<MapResponse> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (NodeLockedException e)
            {
                return Locked<MapResponse>(e);
            }
            catch (KeyNotFoundException e)
            {
                return NotFound(new ResultViewModel<MapResponse> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "示範模式操作失敗");
                return StatusCode(500, new ResultViewModel<MapResponse> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion

        #region 周邊好去

        /// <summary>
        /// 周邊好去：依使用者目前位置推薦附近的地點。
        /// </summary>
        /// <remarks>
        /// 對應「周邊好去」頁面。推薦是跟著使用者走的：帶目前 GPS（lat、lng）才會推薦，由近到遠排列；
        /// 使用者移動後（例如走了 200 公尺，或重新打開這一頁）請帶新的位置再呼叫一次。
        /// **沒帶 lat、lng 時回傳空陣列**，不會一開始就把整個縣市的地點列出來。
        ///
        /// - category：「飲食」只要美食、「其他」只要景點等其他地點，不帶就兩種都要
        /// - radius_m：搜尋半徑（公尺），預設 800，範圍 100～3000；limit：最多幾筆，預設 20，最多 50
        /// - distance_m：跟使用者的直線距離；is_open_now：現在有沒有營業（沒有資料時為 null），美食類已知沒營業的不列出
        /// - 這份劇本自己的站不會出現在這裡（還在迷霧中的站不會被洩漏）
        /// - place_id 是 MySQL 的代號，只有 Neo4j 資料的地點為 0，列表的 key 請用 place_uid
        ///
        /// Request 範例：
        ///
        ///     GET /api/Map/{story_id}/Nearby?lat=25.0421&amp;lng=121.5150&amp;category=飲食&amp;radius_m=800
        /// </remarks>
        /// <param name="story_id">劇本代號。</param>
        /// <param name="category">分類篩選條件：飲食、其他；不帶就全部。</param>
        /// <param name="lat">使用者目前緯度。</param>
        /// <param name="lng">使用者目前經度。</param>
        /// <param name="radius_m">搜尋半徑（公尺），預設 800。</param>
        /// <param name="limit">最多回傳幾筆，預設 20。</param>
        /// <returns>附近的地點清單，由近到遠。</returns>
        // API：周邊好去（Nearby）－依使用者目前位置回傳附近的推薦地點
        [HttpGet("{story_id:int}/Nearby")]
        [ProducesResponseType(typeof(ResultViewModel<List<NearbyPlaceResponse>>), 200)]
        public async Task<IActionResult> Nearby(
            int story_id,
            [FromQuery] string category,
            [FromQuery] double? lat,
            [FromQuery] double? lng,
            [FromQuery] int? radius_m,
            [FromQuery] int? limit)
        {
            try
            {
                List<NearbyPlaceResponse> result =
                    await _nearbyPlaces.GetNearbyAsync(story_id, lat, lng, category, radius_m, limit);

                foreach (NearbyPlaceResponse place in result)
                {
                    place.photo_urls = place.photo_urls.Select(url => PublicUrl.Of(Request, url)).ToList();
                }

                return Ok(new ResultViewModel<List<NearbyPlaceResponse>>
                {
                    isSuccess = true,
                    message = !lat.HasValue || !lng.HasValue
                        ? "請帶目前位置（lat、lng），周邊好去會依使用者所在位置推薦"
                        : $"查詢成功，附近有 {result.Count} 個地點",
                    Result = result
                });
            }
            catch (BadRequestException e)
            {
                return BadRequest(new ResultViewModel<List<NearbyPlaceResponse>> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "周邊好去查詢失敗，story_id: {StoryId}", story_id);

                return BadRequest(new ResultViewModel<List<NearbyPlaceResponse>>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 用餐時間推播：附近美食

        /// <summary>
        /// 用餐時間推播：使用者目前位置附近的美食。
        /// </summary>
        /// <remarks>
        /// 推播由前端用手機本機通知排程（例如 11:30、17:30 跳出「午餐時間到了，看看附近有什麼好吃的？」），
        /// 不需要伺服器推播，iOS 也不需要 Apple 開發者帳號。使用者點開通知後，帶目前 GPS 呼叫這支。
        /// 地圖開著時 NPC 自己跳出來的用餐提醒請用 GET /api/Map/Reminders（後端判斷是不是用餐時間，並附上推薦的店家）。
        ///
        /// - meal：現在是哪一餐（台灣時間）：早餐、午餐、下午茶、晚餐、宵夜
        /// - message：可以直接顯示的一句話，例如「午餐時間到了！附近 350 公尺有「山河魯肉飯」，還有優惠券可以領」
        /// - places：有 QR Code 優惠券的店家排前面，其餘由近到遠；已知目前沒營業的不列出，
        ///   營業時間沒有資料時 is_open_now 為 null
        /// - radius_m：搜尋半徑（公尺），預設 800，範圍 100～3000；limit：最多幾間，預設 10，最多 30
        ///
        /// 餐廳來自 Neo4j 的政府開放資料（Neo4j 連不上時改用 MySQL 的美食資料）與商家自建的餐廳。
        ///
        ///     GET /api/Map/NearbyFood?lat=24.1477&amp;lng=120.6736&amp;radius_m=800
        /// </remarks>
        [HttpGet("NearbyFood")]
        [ProducesResponseType(typeof(ResultViewModel<NearbyFoodResponse>), 200)]
        public async Task<IActionResult> NearbyFood([FromQuery] double lat, [FromQuery] double lng, [FromQuery] int? radius_m, [FromQuery] int? limit)
        {
            try
            {
                NearbyFoodResponse result = await _nearbyFood.GetNearbyFoodAsync(lat, lng, radius_m, limit);
                return Ok(new ResultViewModel<NearbyFoodResponse> { isSuccess = true, message = "查詢成功", Result = result });
            }
            catch (BadRequestException e)
            {
                return BadRequest(new ResultViewModel<NearbyFoodResponse> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "附近美食查詢失敗，lat: {Lat}, lng: {Lng}", lat, lng);
                return StatusCode(500, new ResultViewModel<NearbyFoodResponse> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion

        #region 天氣提醒

        /// <summary>
        /// 天氣提醒：使用者目前位置的天氣。
        /// </summary>
        /// <remarks>
        /// 使用者點 NPC 想看現在天氣時用這支（不管天氣好壞都會回一句話）。
        /// NPC 要不要「自己跳出來」提醒下雨、太熱，請用 GET /api/Map/Reminders，由後端判斷。
        /// 帶目前 GPS 呼叫；氣象站每 10 分鐘更新一次。
        ///
        /// 資料來自中央氣象署開放資料：離使用者最近的氣象站的即時觀測（天氣現象、氣溫、濕度、風速），加上所在縣市的降雨機率。
        /// 後端要設定氣象署授權碼（Weather:CwaApiKey）；沒有設定、氣象署連不上、或附近 50 公里內沒有測站時，
        /// 會改用備援的 Open-Meteo，source 會標示實際用了哪一個。
        ///
        /// - message：可以直接顯示的一句話，例如「目前天氣：多雲，氣溫 29°C（體感 33°C），降雨機率 20%。天氣很熱，記得多喝水、做好防曬。」；
        ///   下雨時是「目前天氣：陰有雨，氣溫 22°C，降雨機率 80%。外面正在下雨，記得帶傘，路滑小心走。」
        /// - description：天氣現象（晴、多雲、陰、陰有雨…）
        /// - condition：天氣大類，換天氣圖示用：clear 晴、cloudy 多雲、overcast 陰、fog 霧、rain 雨、thunder 雷雨、snow 雪、unknown 不明
        /// - rain_probability：接下來幾個小時的降雨機率（%），沒有資料時為 null；is_raining：現在是不是正在下雨
        /// - advice：提醒，依序是打雷 → 正在下雨 → 降雨機率 50% 以上提醒帶傘 → 太熱（32°C 以上）→ 偏涼（15°C 以下）→ 適合出門
        /// - alert：值得主動提醒的狀況（thunder、raining、rain_soon、hot、cold），天氣好時為 null
        /// - station_name：觀測的氣象站；source：資料來源，畫面上請標示
        ///
        /// 沒帶 lat、lng 回 400；兩個來源都查不到時回 502。
        ///
        ///     GET /api/Map/Weather?lat=24.1477&amp;lng=120.6736
        /// </remarks>
        [HttpGet("Weather")]
        [ProducesResponseType(typeof(ResultViewModel<WeatherResponse>), 200)]
        public async Task<IActionResult> Weather([FromQuery] double lat, [FromQuery] double lng)
        {
            try
            {
                WeatherResponse result = await _weather.GetCurrentAsync(lat, lng);
                return Ok(new ResultViewModel<WeatherResponse> { isSuccess = true, message = "查詢成功", Result = result });
            }
            catch (BadRequestException e)
            {
                return BadRequest(new ResultViewModel<WeatherResponse> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (Exception e)
            {
                // 天氣由外部服務提供，失敗多半是對方的問題
                return StatusCode(502, new ResultViewModel<WeatherResponse> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion

        #region NPC 主動提醒

        /// <summary>
        /// 地圖上 NPC 的主動提醒：現在有沒有值得 NPC 自己跳出來講的事（天氣、用餐）。
        /// </summary>
        /// <remarks>
        /// 對應「地圖」頁面上的 NPC。要不要跳出來由後端判斷，前端不用自己看天氣或時間：
        /// 地圖開著時每隔 check_after_seconds 秒（5 分鐘）帶目前 GPS 問一次，
        /// **reminders 裡有還沒跳過的 key 才讓 NPC 跳出對話**，空陣列就什麼都不用做。
        ///
        /// 會提醒的狀況：
        ///
        /// - 天氣（type = weather）：thunder 打雷下雨、raining 正在下雨、rain_soon 等一下可能下雨（降雨機率 50% 以上）、
        ///   hot 太熱（氣溫或體感 32°C 以上）、cold 偏涼（15°C 以下）。天氣好就不會有提醒。
        ///   message 例如「目前天氣：多雲，氣溫 27°C，降雨機率 70%。等一下可能會下雨，出門記得帶把傘。」；weather 是目前的天氣
        /// - 用餐（type = meal）：breakfast 早餐 07:00–09:00、lunch 午餐 11:30–13:30、dinner 晚餐 17:30–19:30（台灣時間），
        ///   而且附近 800 公尺內找得到店才提醒。message 例如「午餐時間到了！附近 350 公尺有「山河魯肉飯」，還有優惠券可以領，另外還有 4 間美食」；
        ///   places 是推薦的店家（最多 5 間，有優惠券的排前面，其餘由近到遠，已知沒營業的不列），可以直接接「前往」
        ///
        /// key 是這則提醒的代號（例如 weather-20261013-12-rain_soon、meal-20261013-lunch）：同一種天氣 3 小時內、同一餐一天內都是同一個 key。
        /// 前端把跳過的 key 記下來（存在手機裡，App 重開也還在），同一個 key 不要再跳第二次，NPC 就不會一直重複講。
        ///
        /// - story_id：正在玩的劇本，有帶的話推薦的店家會排除這份劇本自己的站（還在迷霧中的站不會被洩漏）
        /// - 天氣查不到時只是少了天氣提醒，用餐提醒照常；沒帶 lat、lng 回 400
        ///
        ///     GET /api/Map/Reminders?lat=24.1477&amp;lng=120.6736&amp;story_id=12
        /// </remarks>
        [HttpGet("Reminders")]
        [ProducesResponseType(typeof(ResultViewModel<NpcReminderResponse>), 200)]
        public async Task<IActionResult> Reminders([FromQuery] double lat, [FromQuery] double lng, [FromQuery] int? story_id)
        {
            try
            {
                NpcReminderResponse result = await _reminders.GetAsync(lat, lng, story_id);

                foreach (NearbyFoodPlace place in result.reminders.SelectMany(r => r.places))
                {
                    place.image_url = PublicUrl.Of(Request, place.image_url);
                }

                return Ok(new ResultViewModel<NpcReminderResponse>
                {
                    isSuccess = true,
                    message = result.reminders.Count > 0 ? $"有 {result.reminders.Count} 則提醒" : "現在沒有要提醒的事",
                    Result = result
                });
            }
            catch (BadRequestException e)
            {
                return BadRequest(new ResultViewModel<NpcReminderResponse> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "NPC 提醒查詢失敗，lat: {Lat}, lng: {Lng}", lat, lng);
                return StatusCode(500, new ResultViewModel<NpcReminderResponse> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion

        /// <summary>
        /// 取得使用者現在所在位置
        /// </summary>
        [AllowAnonymous]
        [HttpPost("Location")]
        [ProducesResponseType(typeof(ResultViewModel<LocationResponse>), 200)]
        public async Task<IActionResult> ReportLocation([FromBody] LocationRequest req)
        {
            try
            {
                LocationResponse result =
                    await _service.ReportLocationAsync(req);

                return Ok(new ResultViewModel<LocationResponse>
                {
                    isSuccess = true,
                    message = "定位接收成功",
                    Result = result
                });
            }
            catch (ArgumentException e)
            {
                return BadRequest(new ResultViewModel<LocationResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "接收定位失敗");
                return BadRequest(new ResultViewModel<LocationResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }
    }
}