using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.util;
using backend.utils;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 任務答題相關 API。
     /// [❌ 尚未完成]
    /// 對應頁面：答題、答對、答錯、提示、獎章、隱藏關卡。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    // 任務答題 (對應畫面: 答題, 答對, 答錯, 提示)
    public class TaskController : ControllerBase
    {
        private readonly ILogger<TaskController> _logger;
        private readonly TaskService _service;
        private readonly TaskGenerationService _taskGeneration;
        private readonly TaskDifficultyService _difficulty;

        public TaskController(
            ILogger<TaskController> logger,
            TaskService service,
            TaskGenerationService taskGeneration,
            TaskDifficultyService difficulty)
        {
            _logger = logger;
            _service = service;
            _taskGeneration = taskGeneration;
            _difficulty = difficulty;
        }

        #region 題型解鎖進度

        /// <summary>
        /// 取得登入者在目前所在鄉鎮市區的題型解鎖進度（產生劇本前顯示用）。
        /// </summary>
        /// <remarks>
        /// 需要登入。後端用座標找出所在的縣市／鄉鎮市區（Neo4j 最近景點的行政區，查不到時用反向地理編碼），
        /// 再看登入者有沒有在這個區抵達過任何一站（或在這個區作答過）。
        ///
        /// 第一次到的區，產生劇本時這個區的景點不會出 e人訪談型，locked_types 會列出並附上解鎖條件（unlock_hint）；
        /// 在這個區抵達任一站後就解鎖。查不到所在區時 city_name、district_name 為 null，並當作第一次到。
        ///
        ///     GET /api/Task/Progress?lat=24.1437&amp;lng=120.6736
        /// </remarks>
        /// <param name="lat">玩家目前緯度。</param>
        /// <param name="lng">玩家目前經度。</param>
        [Authorize]
        [HttpGet]
        [Route("Progress")]
        [ProducesResponseType(typeof(ResultViewModel<TaskProgressResponse>), 200)]
        public async Task<IActionResult> GetProgress([FromQuery] double lat, [FromQuery] double lng)
        {
            if (lat < -90 || lat > 90 || lng < -180 || lng > 180 || (lat == 0 && lng == 0))
                throw new BadRequestException("請提供正確的 lat、lng");

            return Ok(new ResultViewModel<TaskProgressResponse>
            {
                isSuccess = true,
                message = "查詢成功",
                Result = await _difficulty.GetProgressAsync(User.GetAuId(), lat, lng)
            });
        }

        #endregion

        #region 節點遊玩畫面

        /// <summary>
        /// 節點遊玩畫面：一次取得這一站的劇情、自己的座位、進度與每一題的作答方式與狀態。
        /// </summary>
        /// <remarks>
        /// 需要登入。只有劇本擁有者或協作隊員可以查看（其他人 403）；這一站要已被隊伍抵達過
        /// （先呼叫 POST api/Map/Node/{node_id}/Arrive），否則 409。查看不用帶 GPS，作答時才檢查位置。
        ///
        /// 每一題的 answer_mode 決定前端顯示哪種輸入、作答（POST api/Task/Answer）時帶哪個欄位：
        /// - choice：selected_option_key
        /// - text：text_answer
        /// - photo：photo_url（先用 api/Upload 上傳取得網址）
        /// - audio_or_video：audio_url 或 video_url
        /// - gps：不用帶作答欄位，在現場送出即可
        ///
        /// 協作解謎型的 clue_text 是自己座位的線索；hint_available 為 true 時可呼叫 GET api/Task/{task_id}/Hint 取提示；
        /// success_text 在這一站任務全部通過後才有值。
        ///
        /// npc 是這一站說開場白、出題與完成劇情的 NPC（名稱、身分、圖片、聲線）；
        /// 要把台詞唸出來時，把 npc.npc_voice 當成 POST api/Npc/Speak 的 voice。
        ///
        ///     GET /api/Task/Node/5
        /// </remarks>
        [Authorize]
        [HttpGet]
        [Route("Node/{node_id:int}")]
        [ProducesResponseType(typeof(ResultViewModel<NodePlayResponse>), 200)]
        public IActionResult GetNodePlay(int node_id)
        {
            NodePlayResponse result = _service.GetNodePlay(User.GetAuId(), node_id);
            if (result.npc != null)
                result.npc.npc_avatar_url = PublicUrl.Of(Request, result.npc.npc_avatar_url);

            return Ok(new ResultViewModel<NodePlayResponse>
            {
                isSuccess = true,
                message = "查詢成功",
                Result = result
            });
        }

        #endregion

        #region 取得任務

        /// <summary>
        /// 【前端不用接】取得節點的任務清單（已由 GET api/Task/Node/{node_id} 取代）。
        /// </summary>
        /// <remarks>
        /// **前端不用接**：請改用 GET api/Task/Node/{node_id}，一次拿到節點劇情、進度、作答方式與提示狀態，且不用每次帶 GPS。
        ///
        /// 需要登入。只有劇本擁有者或該劇本協作隊伍的成員可以查看（其他人 403），並須位於任務地點附近。
        /// 協作解謎型（type_id=5）的 clue_text 依登入者在隊伍中的座位決定（擁有者還沒組隊時當 1 號座位），
        /// 前端不需要、也不能指定座位。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Task/List
        ///     { "node_id": "5", "gps_lat": 24.1385, "gps_lon": 120.6784 }
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("List")]
        // POST: api/Task/List
        public async Task<IActionResult> GetTask([FromBody] TaskListReq req)
        {
            try
            {
                var tasks = await _service.GetTask(req, User.GetAuId());
                return Ok(new ResultViewModel<List<TaskDetailResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = tasks,
                });
            }
            catch (UnauthorizedAccessException e)
            {
                return StatusCode(403, new ResultViewModel<List<TaskDetailResponse>> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<TaskDetailResponse>> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 手動生成任務（測試用）

        /// <summary>
        /// 【前端不用接】測試用：直接對指定劇本或節點逐題呼叫 AI 生成任務。
        /// </summary>
        /// <remarks>
        /// **前端不用接**：正式流程的任務在 POST api/Story/GenerateGameStory
        /// 規劃行程時就判斷好任務類型，連同劇本一次交給 AI 生成並寫入資料庫，前端不需要另外呼叫這支。
        ///
        /// 這支只給後端測試單一節點的任務生成：node_id 有值時只生成該節點，否則生成整份劇本（story_id）的所有節點，
        /// 寫入 task / task_option / task_clue。
        /// </remarks>
        [HttpPost]
        [Route("Generate")]
        // POST: api/Task/Generate
        public async Task<IActionResult> GenerateTasks([FromBody] TaskGenerateReq req)
        {
            int playerCount = req.player_count > 0 ? req.player_count : 2;

            var tasks = !string.IsNullOrWhiteSpace(req.node_id)
                ? await _taskGeneration.GenerateTasksForNodeAsync(req.node_id, playerCount)
                : await _taskGeneration.GenerateTasksForStoryAsync(req.story_id, playerCount);

            return Ok(new ResultViewModel<List<TaskDetailResponse>>
            {
                isSuccess = true,
                message = $"共生成 {tasks.Count} 筆任務",
                Result = tasks
            });
        }

        #endregion

        #region 送出答案

        /// <summary>
        /// 送出任務答案並取得答題結果。
        /// </summary>
        /// <remarks>
        /// 只有劇本擁有者或該劇本協作隊伍的成員可以作答（403）；這一站要已被隊伍抵達過（409）；
        /// 玩家目前 GPS 位置要在任務地點 200 公尺內（400）。通過後依任務類型驗證答案，
        /// 要帶哪個作答欄位見節點遊玩畫面（GET api/Task/Node/{node_id}）每一題的 answer_mode，
        /// 沒帶時回 400 且不算答錯：text → text_answer、choice → selected_option_key、photo → photo_url、
        /// audio_or_video → audio_url 或 video_url、gps 不用帶。
        ///
        /// 每次作答都寫入 user_task_record；答對時 task.pass 設為 1（已通過，協作隊伍共用）。
        /// 回應帶回這一站的進度（node_progress）、這一站是否完成（node_completed）；
        /// 整份劇本全部通過時 story_completed = true，遊玩紀錄與協作隊伍已自動標成完成，前端可直接進結局畫面。
        /// 已通過的任務再次提交會直接回傳 pass = 1，不重複寫入紀錄。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Task/Answer
        ///     {
        ///       "task_id": 1,
        ///       "gps_lat": 22.997,
        ///       "gps_lon": 120.20239,
        ///       "selected_option_key": "A"
        ///     }
        /// </remarks>
        /// <param name="req">答題請求資料，需帶入目前 GPS 位置，並依任務類型帶入對應欄位。</param>
        /// <returns>答題結果，包含是否正確與後續資訊。</returns>
        // API：送出答案（Answer）－依任務類型驗證答案並回傳結果
        [Authorize]
        [HttpPost]
        [Route("Answer")]
        // POST: api/Task/Answer
        public async Task<IActionResult> Answer([FromBody] TaskAnswerRequest req)
        {
            try
            {
                req.au_id = User.GetAuId();

                return Ok(new ResultViewModel<TaskAnswerResponse>
                {
                    isSuccess = true,
                    message = "送出成功",
                    Result = await _service.SubmitAnswer(req),
                });
            }
            catch (UnauthorizedAccessException e)
            {
                return StatusCode(403, new ResultViewModel<TaskAnswerResponse> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (ConflictException e)
            {
                return Conflict(new ResultViewModel<TaskAnswerResponse> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (BadRequestException e)
            {
                return BadRequest(new ResultViewModel<TaskAnswerResponse> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<TaskAnswerResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 取得提示

        /// <summary>
        /// 取得指定任務的提示內容。
        /// </summary>
        /// <remarks>
        /// 後端依登入者（JWT）在此任務的實際答錯次數（user_task_record）決定是否給提示：
        /// 選擇題（文化問答、景點猜猜樂、商家知識問答、圖像地理猜謎）答錯 1 次就回傳 task.task_hint，
        /// 還沒答錯時 is_available 為 false；協作解謎與沒有對錯的題型（跨關集結、創意攝影、地方美食、e人訪談）一開始就能看。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Task/1/Hint
        /// </remarks>
        /// <param name="task_id">任務代號（task.task_id）。</param>
        /// <returns>提示內容與是否可用。</returns>
        // API：取得提示（GetHint）－依答錯次數回傳提示
        [Authorize]
        [HttpGet]
        [Route("{task_id:int}/Hint")]
        public IActionResult GetHint(int task_id)
        {
            try
            {
                TaskHintResponse hint = _service.GetHint(User.GetAuId(), task_id);
                hint.npc_avatar_url = PublicUrl.Of(Request, hint.npc_avatar_url);

                return Ok(new ResultViewModel<TaskHintResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = hint
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<TaskHintResponse>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null
                });
            }
        }

        #endregion

        #region 隱藏關卡檢查

        /// <summary>
        /// 依玩家目前 GPS 座標檢查是否觸發隱藏關卡。
        /// </summary>
        /// <remarks>
        /// 建議由 MapController 的玩家位置回報流程內部呼叫，不對前端開放主動查詢，避免玩家猜位置。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Task/HiddenLevel/Check?ep_id=EP001&amp;lat=22.99&amp;lng=120.20&amp;region_id=region_tainan_centralwest
        /// </remarks>
        /// <param name="ep_id">探員代號。</param>
        /// <param name="lat">目前緯度。</param>
        /// <param name="lng">目前經度。</param>
        /// <param name="region_id">目前所在地區代號。</param>
        /// <returns>隱藏關卡觸發結果，未觸發時 triggered 為 false。</returns>
        // API：隱藏關卡檢查（CheckHiddenLevel）－依GPS座標判斷是否觸發隱藏關卡
        [HttpPost]
        [Route("HiddenLevel/Check")]
        public IActionResult CheckHiddenLevel([FromQuery] string ep_id, [FromQuery] double lat, [FromQuery] double lng, [FromQuery] string region_id)
        {
            try
            {
                return Ok(new ResultViewModel<HiddenLevelTriggerResult>
                {
                    isSuccess = true,
                    message = "查詢完成",
                    Result = _service.CheckHiddenLevel(ep_id, lat, lng, region_id),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<HiddenLevelTriggerResult> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion
    }
}