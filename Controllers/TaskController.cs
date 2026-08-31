using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
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

        public TaskController(ILogger<TaskController> logger, TaskService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 取得任務詳情

        /// <summary>
        /// 取得指定任務的詳細內容。
        /// </summary>
        /// <remarks>
        /// 對應「答題」頁面，顯示題目與選項內容。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Task/{task_id}
        /// </remarks>
        /// <param name="task_id">任務代號。</param>
        /// <returns>任務詳細內容，包含題目與選項。</returns>
        // API：取得任務詳情（GetTask）－回傳指定任務的題目與選項內容
        [HttpGet]
        [Route("{task_id}")]
        // GET: api/Task/{task_id}
        public IActionResult GetTask(string task_id)
        {
            try
            {
                return Ok(new ResultViewModel<TaskDetailResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetTaskDetail(task_id),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<TaskDetailResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 送出答案

        /// <summary>
        /// 送出任務答案並取得答題結果。
        /// </summary>
        /// <remarks>
        /// 對應「答對」與「答錯」頁面，依任務類型自動分發到對應的驗證邏輯
        /// （文化問答/拍照打卡/短片演繹/採訪蒐證/計數推理/跨關集結/GPS區域定位/QR Code）。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Task/Answer
        ///     {
        ///       "task_id": "task_confucius_001",
        ///       "selected_option_key": "A"
        ///     }
        /// </remarks>
        /// <param name="req">答題請求資料，依任務類型帶入對應欄位。</param>
        /// <returns>答題結果，包含是否正確與後續資訊。</returns>
        // API：送出答案（Answer）－依任務類型驗證答案並回傳結果
        [HttpPost]
        [Route("Answer")]
        // POST: api/Task/Answer
        public IActionResult Answer([FromBody] TaskAnswerRequest req)
        {
            try
            {
                return Ok(new ResultViewModel<TaskAnswerResponse>
                {
                    isSuccess = true,
                    message = "送出成功",
                    Result = _service.SubmitAnswer(req),
                });
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
        /// 後端依探員實際答錯次數自動決定提示階段。
        /// 答錯一次顯示第一階段提示；答錯兩次則顯示更明確的第二階段提示。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Task/task_confucius_001/Hint?ep_id=EP_DDBFC8C8
        /// </remarks>
        /// <param name="task_id">任務代號。</param>
        /// <param name="ep_id">探員代號，後端依此查詢該玩家實際的答錯次數。</param>
        /// <returns>依玩家答錯次數回傳對應階段的提示內容。</returns>
        // API：取得提示（GetHint）－依答錯次數回傳對應階段提示
        [HttpGet]
        [Route("{task_id}/Hint")]
        public IActionResult GetHint(string task_id, [FromQuery] string ep_id)
        {
            try
            {
                return Ok(new ResultViewModel<TaskHintResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetHint(ep_id, task_id)
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

        #region 抽取獎章

        /// <summary>
        /// 完成節點時抽取一次獎章。
        /// </summary>
        /// <remarks>
        /// 依 md_badge_pool 的 weight 權重隨機抽取，同一枚徽章不會重複發放給同一位探員。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Task/Badge/Draw?ep_id=EP001&amp;story_id=story_tainan_001
        /// </remarks>
        /// <param name="ep_id">探員代號。</param>
        /// <param name="story_id">劇本代號，用於篩選專屬獎章池，無專屬池則抽通用池。</param>
        /// <returns>抽中的徽章代號，若獎章池為空則回傳 null。</returns>
        // API：抽取獎章（DrawBadge）－完成節點時依權重隨機抽一枚徽章
        [HttpPost]
        [Route("Badge/Draw")]
        public IActionResult DrawBadge([FromQuery] string ep_id, [FromQuery] string story_id)
        {
            try
            {
                var badgeId = _service.DrawBadge(ep_id, story_id);
                return Ok(new ResultViewModel<string>
                {
                    isSuccess = true,
                    message = badgeId != null ? "抽中新徽章！" : "本次沒有可抽的徽章",
                    Result = badgeId
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<string> { isSuccess = false, message = e.Message.ToString(), Result = null });
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