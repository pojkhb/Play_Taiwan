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
    /// NPC 語音：劇情前傳、NPC 對話的文字轉語音（外部 AI 服務產生 mp3，前端拿 audio_url 直接播放）。
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
        /// 劇情前傳語音：唸指定劇本的前傳。
        /// </summary>
        /// <remarks>
        /// 對應「劇情前傳」畫面，前端不用自己傳文字。回傳的 `audio_url` 是 mp3，直接播放。
        ///
        ///     POST /api/Npc/Prologue/3?voice=zh-TW-YunJheNeural
        ///
        /// `voice` 可省略，預設用這個劇本 NPC 的聲線（劇本詳情的 npc.voice）。
        /// </remarks>
        [HttpPost]
        [Route("Prologue/{story_id:int}")]
        [ProducesResponseType(typeof(ResultViewModel<NpcSpeakResponse>), 200)]
        public async Task<IActionResult> Prologue(int story_id, [FromQuery] string voice)
        {
            try
            {
                User.GetAuId();
                return Ok(new ResultViewModel<NpcSpeakResponse>
                {
                    isSuccess = true,
                    message = "語音產生成功",
                    Result = await _service.SpeakPrologueAsync(story_id, voice)
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "產生劇情前傳語音失敗（story_id={StoryId}）", story_id);
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        /// <summary>
        /// NPC 說話：任意文字轉語音（節點對話、任務說明等）。
        /// </summary>
        /// <remarks>
        /// ```json
        /// { "text": "探員，第一條線索就藏在公園裡的希臘神殿。", "voice": "zh-TW-HsiaoChenNeural" }
        /// ```
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
                    message = "語音產生成功",
                    Result = await _service.SpeakAsync(request?.text, request?.voice)
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "產生 NPC 語音失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }
    }
}
