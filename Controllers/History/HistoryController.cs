using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    /// 過往紀錄相關 API。
    /// 提供前端取得探員已完成的劇本清單、觀看過往劇本詳細探索總覽等功能。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class HistoryController : ControllerBase
    {
        private readonly ILogger<HistoryController> _logger;
        private readonly HistoryService _service;
        private readonly StoryRecapService _recap;

        public HistoryController(ILogger<HistoryController> logger, HistoryService service, StoryRecapService recap)
        {
            _logger = logger;
            _service = service;
            _recap = recap;
        }

        #region 取得所有過往劇本
        /// <summary>
        /// 取得目前探員的所有過往劇本清單。
        /// </summary>
        /// <remarks>
        /// 對應「過往」頁面的收藏館列表，顯示已完成的劇本卷。
        /// 
        /// **Request 範例**：
        /// 
        ///     GET /api/History
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": [
        ///     {
        ///       "story_id": "story_tainan_001",
        ///       "title": "府城儒生失落卷",
        ///       "synopsis": "尋著百年軌跡，找回失落記憶……",
        ///       "completed_date": "2026-08-09T00:00:00",
        ///       "region": "台南永康區",
        ///       "vlog_id": "VLOG-001",
        ///       "spots": null
        ///     }
        ///   ]
        /// }
        /// ```
        /// </remarks>
        [Authorize]
        [HttpGet]
        [Route("")]
        [ProducesResponseType(typeof(ResultViewModel<List<HistoryStoryItem>>), 200)]
        public IActionResult GetHistoryList()
        {
            try
            {
                return Ok(new ResultViewModel<List<HistoryStoryItem>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetHistoryList(User.GetAuId())
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "取得過往劇本清單失敗");
                return StatusCode(500, new ResultViewModel<List<HistoryStoryItem>> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion

        #region 取得過往劇本詳情(卷)
        /// <summary>
        /// 取得單一過往劇本的詳細內容 (包含所有經歷過的景點清單)。
        /// </summary>
        /// <remarks>
        /// 對應「過往－卷」頁面，顯示完成日期、故事大綱以及探索總覽(Spots)。
        /// 
        /// **Request 範例**：
        /// 
        ///     GET /api/History/story_tainan_001
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": {
        ///     "story_id": "story_tainan_001",
        ///     "title": "府城儒生失落卷",
        ///     "synopsis": "尋著百年軌跡，找回失落記憶……",
        ///     "completed_date": "2026-08-09T00:00:00",
        ///     "region": "台南永康區",
        ///     "vlog_id": "VLOG-001",
        ///     "spots": [
        ///       "臺南孔廟",
        ///       "赤崁樓",
        ///       "神農街"
        ///     ]
        ///   }
        /// }
        /// ```
        /// </remarks>
        /// <param name="story_id">劇本代號，對應 story.s_id</param>
        [Authorize]
        [HttpGet]
        [Route("{story_id:int}")]
        [ProducesResponseType(typeof(ResultViewModel<HistoryStoryItem>), 200)]
        public IActionResult GetHistoryDetail(int story_id)
        {
            try
            {
                return Ok(new ResultViewModel<HistoryStoryItem>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetHistoryDetail(story_id, User.GetAuId())
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"取得過往劇本詳情失敗: {story_id}");
                return StatusCode(500, new ResultViewModel<HistoryStoryItem> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion

        #region 劇本回顧

        /// <summary>
        /// 劇本回顧：玩完的劇本一次拿到劇情、每一站、收穫、統計與旁白。
        /// </summary>
        /// <remarks>
        /// 對應「過往旅途」點進某個劇本的回顧頁。只能看自己玩完的劇本，還沒玩完回傳 400。
        ///
        /// - nodes：每一站的真正景點與照片、章節標題、到站與完成時的劇情、玩家在這站拍的照片、任務作答結果（玩完了迷霧都已散開）
        /// - postcards、badge、vlog：這趟拿到的明信片、抽到的勳章、做好的 Vlog
        /// - stats：站數、作答數、答對數、照片數、明信片數
        /// - narration：旁白。text 直接顯示；audio_url 是 mp3，還沒產生過時為 null，
        ///   使用者按播放時呼叫 POST /api/History/{story_id}/Recap/Narration 產生（第一次要等幾秒，之後直接回傳）
        ///
        /// 旁白來源 narration.source：vlog＝玩家確認過的 Vlog 旁白；story＝依各站劇情自動組成。
        /// </remarks>
        [Authorize]
        [HttpGet]
        [Route("{story_id:int}/Recap")]
        [ProducesResponseType(typeof(ResultViewModel<StoryRecapResponse>), 200)]
        public async Task<IActionResult> GetRecap(int story_id)
        {
            try
            {
                StoryRecapResponse result = await _recap.GetRecapAsync(User.GetAuId(), story_id, $"{Request.Scheme}://{Request.Host}");
                return Ok(new ResultViewModel<StoryRecapResponse> { isSuccess = true, message = "查詢成功", Result = result });
            }
            catch (KeyNotFoundException e)
            {
                return NotFound(new ResultViewModel<StoryRecapResponse> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (InvalidOperationException e)
            {
                return BadRequest(new ResultViewModel<StoryRecapResponse> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢劇本回顧失敗");
                return StatusCode(500, new ResultViewModel<StoryRecapResponse> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        /// <summary>
        /// 產生回顧旁白的語音（mp3），回傳 audio_url。
        /// </summary>
        /// <remarks>
        /// 使用者按下播放時呼叫。第一次會交給 AI 服務轉成語音（要等幾秒），之後同一段旁白直接回傳存好的檔案。
        /// body 可省略；voice 可換聲音，預設 zh-TW-HsiaoChenNeural。
        ///
        /// **Request 範例**：
        /// ```json
        /// { "voice": "zh-TW-YunJheNeural" }
        /// ```
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("{story_id:int}/Recap/Narration")]
        [ProducesResponseType(typeof(ResultViewModel<StoryRecapNarration>), 200)]
        public async Task<IActionResult> Narrate(
            int story_id,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] StoryRecapNarrationRequest req)
        {
            try
            {
                StoryRecapNarration result = await _recap.NarrateAsync(User.GetAuId(), story_id, req?.voice, $"{Request.Scheme}://{Request.Host}");
                return Ok(new ResultViewModel<StoryRecapNarration> { isSuccess = true, message = "旁白語音已產生", Result = result });
            }
            catch (KeyNotFoundException e)
            {
                return NotFound(new ResultViewModel<StoryRecapNarration> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (InvalidOperationException e)
            {
                return BadRequest(new ResultViewModel<StoryRecapNarration> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (Exception e)
            {
                // 語音由外部 AI 服務產生，失敗多半是 AI 服務的問題
                _logger.LogError(e, "產生回顧旁白語音失敗");
                return StatusCode(502, new ResultViewModel<StoryRecapNarration> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion
    }
}