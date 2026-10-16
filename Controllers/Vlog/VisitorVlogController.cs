// 檔案路徑：System\Controllers\Vlog\VisitorVlogController.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.util;
using backend.utils;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 遊客 VLOG（旅遊回憶影片）。
    /// 流程：遊戲結束 → Preview（story_id）拿到旁白草稿 → 玩家確認旁白（配樂風格、影片方向可選，選項由 Options 取得）
    /// → CreateFinal（story_id + 旁白），
    /// 後端自動抓這次遊玩拍的照片與景點資料打包送出 → 輪詢 Status/{story_id} 直到 status=3。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class VisitorVlogController : ControllerBase
    {
        private readonly ILogger<VisitorVlogController> _logger;
        private readonly VisitorVlogService _service;

        public VisitorVlogController(ILogger<VisitorVlogController> logger, VisitorVlogService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 0. CreateFinal 選填欄位的選項（下拉選單）

        /// <summary>
        /// 取得 CreateFinal 選填欄位的下拉選單選項：配樂風格（bgm_mood）、影片方向（video_orientation），以及旅遊感想的字數上限。
        /// </summary>
        /// <remarks>
        /// 選單顯示 `label`（`description` 有值時可以當小字說明），送 CreateFinal 時帶 `value`。
        /// `is_default = true` 的是預設選項：玩家沒選時可以不帶這個欄位，AI 會用它。
        ///
        /// **回傳範例**（節錄）：
        /// ```json
        /// {
        ///   "bgm_moods": [
        ///     { "value": "auto", "label": "自動選曲", "description": "AI 分析旁白和旅遊感想，自動挑選配樂", "is_default": true },
        ///     { "value": "warm", "label": "溫馨", "description": null, "is_default": false },
        ///     { "value": "none", "label": "不配樂", "description": "只有旁白，沒有背景音樂", "is_default": false }
        ///   ],
        ///   "video_orientations": [
        ///     { "value": "landscape", "label": "橫式", "description": "1920×1080", "is_default": true },
        ///     { "value": "portrait", "label": "直式", "description": "1080×1920", "is_default": false }
        ///   ],
        ///   "user_memory_text_max_length": 3000
        /// }
        /// ```
        /// </remarks>
        [HttpGet]
        [Route("Options")]
        [ProducesResponseType(typeof(ResultViewModel<VisitorVlogOptionsResponse>), 200)]
        public IActionResult Options()
        {
            return Ok(new ResultViewModel<VisitorVlogOptionsResponse>
            {
                isSuccess = true,
                message = "查詢成功",
                Result = _service.GetOptions()
            });
        }

        #endregion

        #region 1. 旁白草稿

        /// <summary>
        /// 遊戲結束後，依 story_id 產生 VLOG 旁白草稿。
        /// </summary>
        /// <remarks>
        /// 後端會從資料庫組出玩家走過的景點、遊玩時長、任務數，交給 AI 產生旁白。
        ///
        /// **Request 範例**：
        /// ```json
        /// { "story_id": 1 }
        /// ```
        ///
        /// 回傳的 `script` 給玩家確認或修改，改完送到 CreateFinal。
        /// `photo_count` 為 0 時無法合成影片，前端可以先提示玩家。
        ///
        /// `spots` 是走過的景點，每一站的 `images` 是會放進影片的照片網址，可以直接顯示：
        /// - 玩家在這站拍的照片（`uses_place_photo` = false）
        /// - 玩家沒拍照的站改用景點照片（`uses_place_photo` = true）；還沒抵達的站不給景點的真實照片，`images` 是空陣列
        /// - 這一站完全沒有照片時 `images` 是空陣列、`photo_count` 是 0
        /// </remarks>
        [HttpPost]
        [Route("Preview")]
        [ProducesResponseType(typeof(ResultViewModel<VisitorVlogPreviewResponse>), 200)]
        public async Task<IActionResult> Preview([FromBody] VisitorVlogPreviewRequest request)
        {
            try
            {
                var result = await _service.PreviewAsync(User.GetAuId(), request.story_id);
                foreach (VisitorVlogSpot spot in result.spots)
                {
                    spot.images = spot.images.Select(url => PublicUrl.Of(Request, url)).ToList();
                }

                return Ok(new ResultViewModel<VisitorVlogPreviewResponse>
                {
                    isSuccess = true,
                    message = "旁白草稿產生成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "產生遊客 VLOG 草稿失敗（story_id={StoryId}）", request?.story_id);
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion

        #region 2. 送出影片合成

        /// <summary>
        /// 送出確認後的旁白，後端打包照片與景點資料送去合成影片。
        /// </summary>
        /// <remarks>
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "story_id": 1,
        ///   "final_script": "確認後的旁白…",
        ///   "promo_copy": "（選填）修改後的宣傳文案",
        ///   "seo_keywords": ["台中散步", "宮原眼科"],
        ///   "user_memory_text": "（選填）和朋友一起解開所有謎題，很有成就感",
        ///   "bgm_mood": "auto",
        ///   "video_orientation": "portrait"
        /// }
        /// ```
        /// 選填的合成選項（不帶就用預設值；bgm_mood、video_orientation 的下拉選單選項由 GET /api/VisitorVlog/Options 取得）：
        /// - `user_memory_text`：玩家自己寫的旅遊感想，最多 3000 字，只給 AI 判斷配樂風格，不會出現在影片裡
        /// - `bgm_mood`：配樂風格，auto（預設，依旁白自動挑選）、cheerful 輕快、relaxed 悠閒、warm 溫馨、nostalgic 回憶、adventurous 探索、neutral 中性、none 不配樂
        /// - `video_orientation`：landscape（預設，橫式 1920×1080）、portrait（直式 1080×1920）
        ///
        /// 回傳的 `task_id` 要保存好，輪詢 Status 時帶回來。
        /// </remarks>
        [HttpPost]
        [Route("CreateFinal")]
        [ProducesResponseType(typeof(ResultViewModel<VisitorVlogTaskResponse>), 200)]
        public async Task<IActionResult> CreateFinal([FromBody] VisitorVlogCreateFinalRequest request)
        {
            try
            {
                var result = await _service.CreateFinalAsync(User.GetAuId(), request);
                return Ok(new ResultViewModel<VisitorVlogTaskResponse>
                {
                    isSuccess = true,
                    message = $"影片合成任務已建立（{result.photo_count} 張照片），請用 Status 查詢進度",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "送出遊客 VLOG 合成失敗（story_id={StoryId}）", request?.story_id);
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion

        #region 3. 查詢進度 / 結果

        /// <summary>
        /// 查詢這個劇本的 VLOG（影片、旁白、宣傳文案）。
        /// </summary>
        /// <remarks>
        /// status：1=待處理、2=處理中、3=已完成（`video_url` 有值）、4=失敗（看 `error_message`）。
        ///
        /// 只要帶劇本 ID：後端用登入的帳號 + 劇本找到這個人的 VLOG，處理中時用 CreateFinal 時記下的 task_id 向 AI 查一次進度，
        /// 建議每 5–10 秒輪詢一次。AI 回報失敗、或 AI 重啟後任務遺失時會變成 4，這時可以再呼叫 CreateFinal 重送。
        /// 處理中時 `stage` 是 AI 目前的階段（例如「等待處理」「正在生成 XTTS 旁白」）。
        ///
        ///     GET /api/VisitorVlog/Status/1
        /// </remarks>
        [HttpGet]
        [Route("Status/{story_id:int}")]
        [ProducesResponseType(typeof(ResultViewModel<VisitorVlogStatusResponse>), 200)]
        public async Task<IActionResult> Status(int story_id)
        {
            try
            {
                var result = await _service.GetStatusAsync(User.GetAuId(), story_id);
                return Ok(new ResultViewModel<VisitorVlogStatusResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢遊客 VLOG 狀態失敗（story_id={StoryId}）", story_id);
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion
    }
}
