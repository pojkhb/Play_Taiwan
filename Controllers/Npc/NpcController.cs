// 檔案路徑：System\Controllers\Npc\NpcController.cs
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.utils;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// NPC 語音：劇情前傳、NPC 對話的文字轉語音（外部 AI 服務產生 mp3）。
    /// AI 服務是排隊模式：送出後拿到 task_id，每隔幾秒呼叫 GET /api/Npc/Speak/{task_id} 查進度，
    /// status 變成 completed 時拿 audio_url 播放。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NpcController : ControllerBase
    {
        private readonly ILogger<NpcController> _logger;
        private readonly NpcVoiceService _service;

        public NpcController(ILogger<NpcController> logger, NpcVoiceService service)
        {
            _logger = logger;
            _service = service;
        }

        /// <summary>
        /// 劇情前傳語音：唸指定劇本的前傳（排入 AI 佇列，回傳 task_id）。
        /// </summary>
        /// <remarks>
        /// 對應「劇情前傳」畫面，前端不用自己傳文字。
        ///
        ///     POST /api/Npc/Prologue/3?npc_name=阿達力
        ///
        /// `npc_name` 可省略，預設用這個劇本的 NPC（劇本詳情的 npc.name），劇本沒有 NPC 時用薯光。
        /// 回傳的 `task_id` 拿去呼叫 GET /api/Npc/Speak/{task_id} 查進度。
        /// </remarks>
        [HttpPost]
        [Route("Prologue/{story_id:int}")]
        [ProducesResponseType(typeof(ResultViewModel<NpcSpeakResponse>), 200)]
        public async Task<IActionResult> Prologue(int story_id, [FromQuery] string npc_name)
        {
            try
            {
                User.GetAuId();
                return Ok(new ResultViewModel<NpcSpeakResponse>
                {
                    isSuccess = true,
                    message = "已排入語音生成佇列",
                    Result = await _service.SpeakPrologueAsync(story_id, npc_name)
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "產生劇情前傳語音失敗（story_id={StoryId}）", story_id);
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        /// <summary>
        /// NPC 說話：任意文字轉語音（節點對話、任務說明等），排入 AI 佇列，回傳 task_id。
        /// </summary>
        /// <remarks>
        /// ```json
        /// { "text": "探員，第一條線索就藏在公園裡的希臘神殿。", "npc_name": "墨先生" }
        /// ```
        /// `npc_name` 可省略，預設薯光。可用：薯光、珍奶奶、阿達力、墨先生、霓霓、阿吉伯。
        /// 回傳的 `task_id` 拿去呼叫 GET /api/Npc/Speak/{task_id} 查進度。
        /// </remarks>
        [HttpPost]
        [Route("Speak")]
        [ProducesResponseType(typeof(ResultViewModel<NpcSpeakResponse>), 200)]
        public async Task<IActionResult> Speak([FromBody] NpcSpeakRequest request)
        {
            try
            {
                User.GetAuId();
                return Ok(new ResultViewModel<NpcSpeakResponse>
                {
                    isSuccess = true,
                    message = "已排入語音生成佇列",
                    Result = await _service.SpeakAsync(request?.text, request?.npc_name)
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "產生 NPC 語音失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        /// <summary>
        /// 查 NPC 語音進度：等待中、生成中、完成（帶 audio_url）、失敗。
        /// </summary>
        /// <remarks>
        /// 送出語音後每隔 2～3 秒呼叫一次，直到 status 是 completed 或 failed。
        ///
        /// - waiting（等待中）：queue_position 是排在第幾位
        /// - processing（生成中）
        /// - completed（完成）：audio_url 是 mp3 完整網址，直接播放
        /// - failed（失敗）：isSuccess 為 false，message 是失敗原因。AI 服務重新啟動後任務會消失，也會回 failed，請重新送出
        /// </remarks>
        [HttpGet]
        [Route("Speak/{task_id}")]
        [ProducesResponseType(typeof(ResultViewModel<NpcSpeakJobResponse>), 200)]
        public async Task<IActionResult> SpeakStatus(string task_id)
        {
            try
            {
                User.GetAuId();
                NpcSpeakJobResponse job = await _service.GetSpeakJobAsync(task_id);
                bool failed = job.status == NpcVoiceService.FailedStatus;
                return Ok(new ResultViewModel<NpcSpeakJobResponse>
                {
                    isSuccess = !failed,
                    message = failed ? job.message : job.status_text,
                    Result = job
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢 NPC 語音進度失敗（task_id={TaskId}）", task_id);
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }
    }
}
