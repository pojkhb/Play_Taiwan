// 檔案路徑：System\Controllers\Npc\NpcController.cs
using System;
using System.Collections.Generic;
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
    /// NPC 語音：劇情前傳、NPC 對話的文字轉語音（外部 AI 服務用那位 NPC 的聲音產生 wav）。
    /// </summary>
    /// <remarks>
    /// 流程：送出（Speak 或 Prologue）→ status 是 done 就直接播放 audio_url；
    /// 是 processing 就每 2 秒呼叫 GET /api/Npc/Speak/{job_id}，直到 done 或 failed。
    /// 同一位 NPC 唸同一段文字只會產生一次，之後送出會直接拿到 done。
    /// </remarks>
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
        /// NPC 下拉選單：可以唸台詞的 NPC 名單。
        /// </summary>
        /// <remarks>
        /// 選單顯示 `label`（`description` 是 NPC 的身分，可以當小字），送語音 API 時把 `value` 帶進 `npc_name`。
        /// `is_default = true` 的是薯光（Speak 不帶 npc_name 時由它唸）。
        /// 劇情前傳、回顧旁白預設由劇本的 NPC 唸：用劇本詳情的 npc.name、回顧的 narration.npc_name 對 `value` 選好預設值。
        ///
        ///     GET /api/Npc/Options
        ///
        /// **回傳範例**（節錄）：
        /// ```json
        /// [
        ///   { "value": "薯光", "label": "薯光", "description": "帶領旅人探索台灣的嚮導", "avatar_url": "https://.../images/npc/shuguang.png", "is_default": true },
        ///   { "value": "墨先生", "label": "墨先生", "description": "博學嚴謹的文史工作者，擅長解讀古地圖與歷史檔案", "avatar_url": "https://.../images/npc/mo-xian-sheng.png", "is_default": false }
        /// ]
        /// ```
        /// </remarks>
        [HttpGet]
        [Route("Options")]
        [ProducesResponseType(typeof(ResultViewModel<List<NpcOptionItem>>), 200)]
        public async Task<IActionResult> Options()
        {
            List<NpcOptionItem> options = await _service.GetOptionsAsync();
            options.ForEach(o => o.avatar_url = PublicUrl.Of(Request, o.avatar_url));
            return Ok(new ResultViewModel<List<NpcOptionItem>> { isSuccess = true, message = "查詢成功", Result = options });
        }

        /// <summary>
        /// 劇情前傳語音：唸指定劇本的前傳。
        /// </summary>
        /// <remarks>
        /// 對應「劇情前傳」畫面，前端不用自己傳文字。`npc_name` 可省略，預設由這個劇本的 NPC 唸（劇本詳情的 npc.name）。
        /// 回應跟 POST /api/Npc/Speak 一樣：done 直接播放 audio_url，processing 用 GET /api/Npc/Speak/{job_id} 查進度。
        ///
        ///     POST /api/Npc/Prologue/3?npc_name=墨先生
        ///
        /// 錯誤：劇本不存在回 404；npc_name 不在名單上或劇本沒有前傳回 400；AI 服務任務已滿回 503（請稍後再試）。
        /// </remarks>
        [HttpPost]
        [Route("Prologue/{story_id:int}")]
        [ProducesResponseType(typeof(ResultViewModel<NpcSpeakResponse>), 200)]
        public async Task<IActionResult> Prologue(int story_id, [FromQuery] string npc_name)
        {
            return await Respond(() => _service.SpeakPrologueAsync(story_id, npc_name), $"產生劇情前傳語音失敗（story_id={story_id}）");
        }

        /// <summary>
        /// NPC 說話：任意文字轉語音（節點對話、任務說明等）。
        /// </summary>
        /// <remarks>
        /// `npc_name` 是由哪位 NPC 唸：直接帶畫面上拿到的 NPC 名字（劇本詳情的 npc.name、NPC 互動與節點遊玩畫面的 npc_name），
        /// 選項由 GET /api/Npc/Options 取得；不帶時由預設的薯光唸。
        ///
        /// ```json
        /// { "text": "探員，第一條線索就藏在公園裡的希臘神殿。", "npc_name": "墨先生" }
        /// ```
        ///
        /// 回應的 `status`：
        /// - `done`：`audio_url` 是 wav，直接播放（這段話做過，或剛好做好）
        /// - `processing`：AI 排隊或生成中（`stage` 是目前進度、`queue_position` 是前面還有幾個任務），
        ///   每 2 秒用 GET /api/Npc/Speak/{job_id} 查進度
        ///
        /// 錯誤：沒有文字、超過 1000 字或 npc_name 不在名單上回 400；AI 服務任務已滿回 503（請稍後再試）。
        /// </remarks>
        [HttpPost]
        [Route("Speak")]
        [ProducesResponseType(typeof(ResultViewModel<NpcSpeakResponse>), 200)]
        public async Task<IActionResult> Speak([FromBody] NpcSpeakRequest request)
        {
            return await Respond(() => _service.SpeakAsync(request?.text, request?.npc_name), "產生 NPC 語音失敗");
        }

        /// <summary>
        /// 查詢 NPC 語音進度。
        /// </summary>
        /// <remarks>
        /// 送出語音（Speak、Prologue、回顧旁白 Recap/Narration）拿到 processing 後，每 2 秒呼叫一次：
        /// - `processing`：還在排隊或生成，繼續查
        /// - `done`：播放 `audio_url`
        /// - `failed`：`error` 是原因（AI 生成失敗、AI 重啟過或超過 10 分鐘），使用者再按一次播放就會重新送出
        ///
        /// 查不到這個 job_id（後端重啟過）回 404，一樣請重新送出。
        ///
        ///     GET /api/Npc/Speak/3f2a9c1b0d4e4b5f8a6c7d8e9f0a1b2c3d4e5f60
        /// </remarks>
        [HttpGet]
        [Route("Speak/{job_id}")]
        [ProducesResponseType(typeof(ResultViewModel<NpcSpeakResponse>), 200)]
        public async Task<IActionResult> SpeakStatus(string job_id)
        {
            return await Respond(() => _service.StatusAsync(job_id), $"查詢 NPC 語音進度失敗（job_id={job_id}）");
        }

        private async Task<IActionResult> Respond(Func<Task<NpcSpeakResponse>> action, string errorLog)
        {
            try
            {
                User.GetAuId();
                NpcSpeakResponse result = await action();
                result.audio_url = PublicUrl.Of(Request, result.audio_url);
                return Ok(new ResultViewModel<NpcSpeakResponse> { isSuccess = true, message = SpeakMessage(result), Result = result });
            }
            catch (KeyNotFoundException e)
            {
                return NotFound(new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (ArgumentException e)
            {
                return BadRequest(new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (AiBusyException e)
            {
                return StatusCode(503, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
            catch (Exception e)
            {
                // 語音由外部 AI 服務產生，失敗多半是 AI 服務的問題
                _logger.LogError(e, errorLog);
                return StatusCode(502, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        internal static string SpeakMessage(NpcSpeakResponse result) => result.status switch
        {
            NpcSpeakResponse.Done => "語音已產生",
            NpcSpeakResponse.Processing => "語音產生中，請用 job_id 查詢進度",
            _ => "語音產生失敗"
        };
    }
}
