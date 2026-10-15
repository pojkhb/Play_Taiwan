// 檔案路徑：System\Services\Npc\NpcVoiceService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.utils;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>
    /// NPC 語音：把文字（劇情前傳、對話）交給外部 AI 服務轉成 mp3。
    /// AI 服務是排隊模式：送出後只拿到 task_id，再用 GET /api/jobs/{task_id} 查進度，完成時才有 mp3 網址。
    /// AI 服務的任務只放在記憶體，重啟後就查不到，一律當失敗，請使用者重新產生。
    /// </summary>
    public class NpcVoiceService
    {
        private const string SpeakPath = "/api/npc/speak";
        private const string JobsPath = "/api/jobs/";

        /// <summary>AI 語音任務的種類，查進度時只認這種，避免用別的任務 ID 查到其他功能的結果</summary>
        private const string SpeakTaskType = "npc_tts";

        /// <summary>沒指定 NPC 時用預設嚮導薯光</summary>
        public const string DefaultNpc = "薯光";
        public const int MaxTextLength = 1000;

        /// <summary>查不到任務時給使用者的訊息</summary>
        public const string LostMessage = "找不到這個任務（AI 服務可能重新啟動過），請重新產生";

        /// <summary>給前端的狀態：等待中、生成中、完成、失敗</summary>
        public const string WaitingStatus = "waiting";
        public const string ProcessingStatus = "processing";
        public const string CompletedStatus = "completed";
        public const string FailedStatus = "failed";

        /// <summary>等語音做好時，每隔多久查一次</summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly StoryDao _storyDao;

        public NpcVoiceService(IHttpClientFactory httpClientFactory, StoryDao storyDao)
        {
            _httpClientFactory = httpClientFactory;
            _storyDao = storyDao;
        }

        /// <summary>送出語音生成，回傳 task_id 與排隊狀態（不等語音做好）</summary>
        public async Task<NpcSpeakResponse> SpeakAsync(string text, string npcName)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text))
                throw new Exception("請提供要唸的文字 (text)");
            if (text.Length > MaxTextLength)
                throw new Exception($"文字最多 {MaxTextLength} 字");
            npcName = string.IsNullOrWhiteSpace(npcName) ? DefaultNpc : npcName.Trim();

            HttpClient client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = text, ["npc_name"] = npcName });
            using HttpResponseMessage response = await client.PostAsync(AiServiceConfig.Url(SpeakPath), form);
            NpcSpeakApiResponse job = await VlogAiGateway.ReadJsonAsync<NpcSpeakApiResponse>(response, "產生 NPC 語音");

            if (string.IsNullOrWhiteSpace(job?.task_id))
                throw new Exception($"AI 服務沒有回傳任務 ID（status={job?.status}）");

            NpcSpeakResponse result = ToResponse<NpcSpeakResponse>(job.task_id, job);
            result.text = text;
            result.npc_name = npcName;
            return result;
        }

        /// <summary>唸劇本的劇情前傳（story_prologue，沒有時用簡介）；沒指定 NPC 時用劇本的 NPC</summary>
        public async Task<NpcSpeakResponse> SpeakPrologueAsync(int storyId, string npcName)
        {
            StoryDetailResponse story = _storyDao.GetDetail(storyId);   // 找不到劇本會丟出錯誤
            if (string.IsNullOrWhiteSpace(story.preface))
                throw new Exception("這個劇本沒有劇情前傳");
            return await SpeakAsync(story.preface, string.IsNullOrWhiteSpace(npcName) ? story.npc?.name : npcName);
        }

        /// <summary>查語音進度；完成時帶 mp3 網址。查不到或不是語音任務都當失敗</summary>
        public async Task<NpcSpeakJobResponse> GetSpeakJobAsync(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId))
                throw new Exception("請提供 task_id");
            taskId = taskId.Trim();

            HttpClient client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            using HttpResponseMessage response = await client.GetAsync(AiServiceConfig.Url(JobsPath + Uri.EscapeDataString(taskId)));

            // 404：任務不存在（AI 服務重啟後任務會消失）
            NpcSpeakApiResponse job = response.StatusCode == HttpStatusCode.NotFound
                ? null
                : await VlogAiGateway.ReadJsonAsync<NpcSpeakApiResponse>(response, "查詢 NPC 語音進度");

            if (job != null && !string.IsNullOrWhiteSpace(job.task_type) && job.task_type != SpeakTaskType)
                job = null;

            return ToResponse<NpcSpeakJobResponse>(taskId, job);
        }

        /// <summary>
        /// 送出語音並等到做好，回傳 mp3 網址（劇本回顧的旁白用：前端呼叫一次就拿到語音）。
        /// 失敗或超過 timeout 還沒做好就丟出錯誤。
        /// </summary>
        public async Task<string> SpeakAndWaitAsync(string text, string npcName, TimeSpan timeout)
        {
            NpcSpeakResponse submitted = await SpeakAsync(text, npcName);
            DateTime deadline = DateTime.UtcNow + timeout;

            while (true)
            {
                NpcSpeakJobResponse job = await GetSpeakJobAsync(submitted.task_id);
                if (job.status == CompletedStatus) return job.audio_url;
                if (job.status == FailedStatus) throw new Exception($"NPC 語音產生失敗：{job.message}");
                if (DateTime.UtcNow + PollInterval > deadline) throw new Exception("語音還在排隊產生中，請稍後再試");

                await Task.Delay(PollInterval);
            }
        }

        #region AI 任務狀態

        /// <summary>任務狀態轉成給前端的進度；查不到任務、完成但沒有 mp3 網址都算失敗</summary>
        private static T ToResponse<T>(string taskId, NpcSpeakApiResponse job) where T : NpcSpeakJobResponse, new()
        {
            if (job == null)
                return Status<T>(taskId, null, FailedStatus, LostMessage);

            string status = ParseState(job);
            string audioUrl = ResultAudioUrl(job);

            if (status == CompletedStatus && string.IsNullOrWhiteSpace(audioUrl))
                return Status<T>(taskId, job, FailedStatus, "AI 服務沒有回傳語音檔");

            T result = Status<T>(taskId, job, status, ErrorMessage(job) ?? "AI 語音產生失敗");
            if (status == CompletedStatus)
                result.audio_url = VlogAiGateway.ResolveUrl(audioUrl);
            return result;
        }

        private static T Status<T>(string taskId, NpcSpeakApiResponse job, string status, string failMessage) where T : NpcSpeakJobResponse, new() => new()
        {
            task_id = taskId,
            status = status,
            status_text = status switch
            {
                WaitingStatus => "等待中",
                ProcessingStatus => "生成中",
                CompletedStatus => "完成",
                _ => "失敗"
            },
            stage = job?.stage,
            queue_position = status == WaitingStatus ? job?.queue_position : null,
            message = status == FailedStatus ? failMessage : null
        };

        /// <summary>
        /// AI 文件沒有列出狀態字串（目前只確認過 queued），採寬鬆判斷：
        /// 失敗類字眼或帶有 error 算失敗、完成類字眼算完成、排隊類字眼算等待中，其餘都當生成中。
        /// </summary>
        private static string ParseState(NpcSpeakApiResponse job)
        {
            string status = (job.status ?? "").Trim().ToLowerInvariant();
            if (status is "failed" or "failure" or "error" or "cancelled" or "canceled" || ErrorMessage(job) != null) return FailedStatus;
            if (status is "completed" or "complete" or "done" or "success" or "succeeded" or "finished") return CompletedStatus;
            if (status is "queued" or "pending" or "waiting") return WaitingStatus;
            return ProcessingStatus;
        }

        /// <summary>error 欄位轉成文字：字串直接用，物件取 message / detail，都沒有就用原始 JSON；沒有錯誤回傳 null</summary>
        private static string ErrorMessage(NpcSpeakApiResponse job)
        {
            if (job.error is not JsonElement e || e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
            if (e.ValueKind == JsonValueKind.String) return string.IsNullOrWhiteSpace(e.GetString()) ? null : e.GetString();
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (string name in new[] { "message", "detail" })
                    if (e.TryGetProperty(name, out JsonElement m) && m.ValueKind == JsonValueKind.String)
                        return m.GetString();
            }
            return e.GetRawText();
        }

        /// <summary>
        /// AI 文件沒寫完成時 result 的格式：result 是字串就當網址，是物件就依序找 download_url、audio_url、url、file_url
        /// </summary>
        private static string ResultAudioUrl(NpcSpeakApiResponse job)
        {
            if (job.result is not JsonElement r) return null;
            if (r.ValueKind == JsonValueKind.String) return r.GetString();
            if (r.ValueKind != JsonValueKind.Object) return null;

            return new[] { "download_url", "audio_url", "url", "file_url" }
                .Select(name => r.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                .Where(p => p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                .Select(p => p.Value.GetString())
                .FirstOrDefault();
        }

        #endregion
    }
}
