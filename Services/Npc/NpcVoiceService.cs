// 檔案路徑：System\Services\Npc\NpcVoiceService.cs
// NPC 語音：把文字（劇情前傳、對話、回顧旁白）交給外部 AI 服務，用那位 NPC 的聲音（XTTS）轉成 wav。
// 外部 AI 服務端點：POST /api/npc/speak（表單 text、npc_name；排隊模式，先回 task_id）、GET /api/jobs/{task_id}（查詢進度）。
// 做好的語音抓回後端存在 wwwroot/audio/npc/{job_id}.wav：AI 的任務紀錄一小時就過期、AI 重啟也會消失，
// job_id 由「NPC 名字＋文字」算出來，同一位 NPC 唸同一段文字只請 AI 做一次，之後直接回傳存好的檔案。
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using backend.dao;
using backend.utils;
using backend.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace backend.Services
{
    /// <summary>AI 服務的任務已滿（AI 回 503），請使用者稍後再試</summary>
    public class AiBusyException : Exception
    {
        public AiBusyException(string message) : base(message) { }
    }

    /// <summary>
    /// 送出中、生成中、失敗的 NPC 語音任務（job_id → 任務）。整個後端共用一份，只放在記憶體：
    /// 後端重啟後查不到時，前端重新送出就好（做好的語音已經存成檔案，不受影響）
    /// </summary>
    public class NpcVoiceJobs
    {
        public class Job
        {
            public string npc_name { get; init; }
            public string text { get; init; }
            public DateTime submitted_at { get; init; } = DateTime.UtcNow;

            /// <summary>AI 的任務代號；還在送出時為 null</summary>
            public string task_id { get; set; }

            /// <summary>失敗原因；有值代表失敗，下次送出同一段文字時會重送</summary>
            public string error { get; set; }
        }

        private readonly ConcurrentDictionary<string, Job> _jobs = new();

        public Job Get(string jobId) => _jobs.TryGetValue(jobId, out Job job) ? job : null;

        /// <summary>登記要送出的任務：已經有進行中的同一個任務時回傳 false（不重複送出），失敗的任務會被取代</summary>
        public bool TryStart(string jobId, Job job)
        {
            while (true)
            {
                if (_jobs.TryAdd(jobId, job)) return true;
                if (!_jobs.TryGetValue(jobId, out Job current)) continue;
                if (current.error == null) return false;
                if (_jobs.TryUpdate(jobId, job, current)) return true;
            }
        }

        /// <summary>移除任務（只移除同一個任務物件，不會誤刪重送的新任務）</summary>
        public void Remove(string jobId, Job job) => _jobs.TryRemove(KeyValuePair.Create(jobId, job));
    }

    public class NpcVoiceService
    {
        private const string SpeakPath = "/api/npc/speak";
        private const string JobsPath = "/api/jobs/";
        private const string DownloadPath = "/api/download/";
        public const int MaxTextLength = 1000;

        /// <summary>做好的語音放在這裡（網址路徑，也是 wwwroot 下的資料夾）</summary>
        public const string AudioFolder = "/audio/npc/";

        /// <summary>AI 超過這麼久還沒做好就算失敗，讓使用者重送</summary>
        public static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(10);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly NpcDao _npcDao;
        private readonly NpcVoiceJobs _jobs;
        private readonly ILogger<NpcVoiceService> _logger;
        private readonly string _outputRoot;

        public NpcVoiceService(IHttpClientFactory httpClientFactory, NpcDao npcDao, NpcVoiceJobs jobs,
                               IWebHostEnvironment environment, IConfiguration configuration, ILogger<NpcVoiceService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _npcDao = npcDao;
            _jobs = jobs;
            _logger = logger;
            // 語音存放的根目錄，預設就是 wwwroot（測試時改到暫存資料夾）
            _outputRoot = string.IsNullOrWhiteSpace(configuration["Narration:OutputRoot"]) ? environment.WebRootPath : configuration["Narration:OutputRoot"];
        }

        #region NPC 名單

        /// <summary>NPC 下拉選單，預設選項是薯光。圖片是站內路徑，由 Controller 組成完整網址</summary>
        public async Task<List<NpcOptionItem>> GetOptionsAsync()
        {
            return (await _npcDao.GetAllAsync()).Select(n => new NpcOptionItem
            {
                value = n.npc_name,
                label = n.npc_name,
                description = n.npc_role,
                avatar_url = n.npc_avatar,
                is_default = n.is_default
            }).ToList();
        }

        /// <summary>
        /// 決定由哪位 NPC 唸：有帶就要是名單上的名字（不是就丟出 ArgumentException）；
        /// 沒帶時用 fallback（劇本的 NPC），再沒有就用預設的薯光
        /// </summary>
        public async Task<string> ResolveNpcAsync(string requested, string fallback = null)
        {
            List<NpcDao.NpcRow> npcs = await _npcDao.GetAllAsync();
            string name = requested?.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                if (npcs.Any(n => n.npc_name == name)) return name;
                throw new ArgumentException($"沒有這位 NPC：{name}（可選的 NPC 請用 GET /api/Npc/Options 查詢）");
            }

            return fallback ?? npcs.FirstOrDefault(n => n.is_default)?.npc_name ?? MapDao.DefaultNpcName;
        }

        #endregion

        #region 送出

        /// <summary>任意文字轉語音；沒指定 NPC 時用預設的薯光</summary>
        public async Task<NpcSpeakResponse> SpeakAsync(string text, string npcName)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("請提供要唸的文字 (text)");
            if (text.Length > MaxTextLength)
                throw new ArgumentException($"文字最多 {MaxTextLength} 字");

            return await RequestAsync(await ResolveNpcAsync(npcName), text);
        }

        /// <summary>唸劇本的劇情前傳（沒有前傳時用簡介）；沒指定 NPC 時用劇本的 NPC</summary>
        public async Task<NpcSpeakResponse> SpeakPrologueAsync(int storyId, string npcName)
        {
            NpcDao.StoryNpcRow story = await _npcDao.GetStoryAsync(storyId) ?? throw new KeyNotFoundException("找不到此劇本：" + storyId);
            string text = (string.IsNullOrWhiteSpace(story.story_prologue) ? story.story_synopsis : story.story_prologue)?.Trim();
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("這個劇本沒有劇情前傳");
            if (text.Length > MaxTextLength)
                text = text[..(MaxTextLength - 1)] + "…";

            return await RequestAsync(await ResolveNpcAsync(npcName, story.npc_name), text);
        }

        /// <summary>
        /// 請 NPC 唸一段文字（文字與 NPC 要先檢查過）：做好過就直接回傳；同一段正在做就回傳目前進度；
        /// 都沒有才送給 AI 排隊。AI 任務已滿時丟出 <see cref="AiBusyException"/>
        /// </summary>
        public async Task<NpcSpeakResponse> RequestAsync(string npcName, string text)
        {
            string jobId = JobId(npcName, text);
            if (File.Exists(PhysicalPath(jobId)))
                return DoneResponse(jobId);

            var job = new NpcVoiceJobs.Job { npc_name = npcName, text = text };
            if (!_jobs.TryStart(jobId, job))
                return await StatusAsync(jobId);   // 同一段話已經在做了，不重複送出

            try
            {
                using HttpClient client = CreateClient();
                using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = text, ["npc_name"] = npcName });
                using HttpResponseMessage response = await client.PostAsync(AiServiceConfig.Url(SpeakPath), form);
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                    throw new AiBusyException("AI 服務目前任務已滿，請稍後再試");

                VlogJobApiResponse result = await VlogAiGateway.ReadJsonAsync<VlogJobApiResponse>(response, "送出 NPC 語音");
                if (string.IsNullOrWhiteSpace(result?.task_id))
                    throw new Exception($"AI 服務沒有回傳任務 ID（status={result?.status}）");

                job.task_id = result.task_id;
                return ProcessingResponse(jobId, result);
            }
            catch
            {
                _jobs.Remove(jobId, job);
                throw;
            }
        }

        #endregion

        #region 查詢進度

        /// <summary>
        /// 查詢語音進度：檔案已經存好就是 done；還在 AI 排隊或生成中就是 processing；AI 回報失敗、查不到任務或超過時間是 failed。
        /// AI 暫時連不上時當作還在處理中，下次查詢再問。job_id 不認得（格式不對、後端重啟過）時丟出 KeyNotFoundException
        /// </summary>
        public async Task<NpcSpeakResponse> StatusAsync(string jobId)
        {
            jobId = jobId?.Trim().ToLowerInvariant();
            if (!IsJobId(jobId))
                throw new KeyNotFoundException("找不到這個語音任務");
            if (File.Exists(PhysicalPath(jobId)))
                return DoneResponse(jobId);

            NpcVoiceJobs.Job job = _jobs.Get(jobId)
                ?? throw new KeyNotFoundException("找不到這個語音任務（可能後端重啟過），請重新送出");
            if (job.error != null)
                return FailedResponse(jobId, job.error);
            if (job.task_id == null)
                return new NpcSpeakResponse { status = NpcSpeakResponse.Processing, job_id = jobId, stage = "送出中" };
            if (DateTime.UtcNow - job.submitted_at > JobTimeout)
                return Fail(jobId, job, $"AI 服務超過 {JobTimeout.TotalMinutes:0} 分鐘還沒做好語音，請重新播放");

            VlogJobApiResponse ai = await QueryAiAsync(job.task_id);
            if (ai == null)
                return new NpcSpeakResponse { status = NpcSpeakResponse.Processing, job_id = jobId, stage = "等待 AI 服務回應" };
            if (ai.status == VlogJobApiResponse.NotFound)
                return Fail(jobId, job, "AI 服務找不到這個語音任務（可能已過期或 AI 服務重啟過），請重新播放");

            switch (VlogAiGateway.ParseState(ai))
            {
                case VlogTaskState.Failed:
                    return Fail(jobId, job, FailureMessage(ai));
                case VlogTaskState.Ready:
                    if (!await SaveAudioAsync(jobId, ai.result))
                        return new NpcSpeakResponse { status = NpcSpeakResponse.Processing, job_id = jobId, stage = "正在下載語音" };   // 下次查詢再試
                    _jobs.Remove(jobId, job);
                    return DoneResponse(jobId);
                default:
                    return ProcessingResponse(jobId, ai);
            }
        }

        /// <summary>問 AI 任務進度；AI 回 404 時回傳 status=not_found，連不上、逾時或 5xx 時回傳 null</summary>
        private async Task<VlogJobApiResponse> QueryAiAsync(string taskId)
        {
            try
            {
                using HttpClient client = CreateClient();
                using HttpResponseMessage response = await client.GetAsync(AiServiceConfig.Url(JobsPath + Uri.EscapeDataString(taskId)));
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new VlogJobApiResponse { status = VlogJobApiResponse.NotFound, task_id = taskId };
                if ((int)response.StatusCode >= 500)
                {
                    _logger.LogWarning("查詢 NPC 語音進度失敗（task_id={TaskId}，HTTP {Status}），下次查詢再問", taskId, (int)response.StatusCode);
                    return null;
                }
                return await VlogAiGateway.ReadJsonAsync<VlogJobApiResponse>(response, "查詢 NPC 語音進度");
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(e, "連不上 AI 服務查詢 NPC 語音進度（task_id={TaskId}），下次查詢再問", taskId);
                return null;
            }
        }

        /// <summary>
        /// 把 AI 做好的語音抓回後端存檔；抓不到時回傳 false（下次查詢再試）。
        /// 有檔名時直接向 AI 服務的位址下載，不經過 AI 回傳網址的公開網域
        /// </summary>
        private async Task<bool> SaveAudioAsync(string jobId, VlogJobResult result)
        {
            string url = string.IsNullOrWhiteSpace(result.filename)
                ? VlogAiGateway.ResolveUrl(result.download_url)
                : AiServiceConfig.Url(DownloadPath + Uri.EscapeDataString(result.filename));
            try
            {
                using HttpClient client = CreateClient();
                byte[] audio = await client.GetByteArrayAsync(url);
                if (audio.Length == 0) throw new InvalidDataException("語音檔是空的");

                string physical = PhysicalPath(jobId);
                Directory.CreateDirectory(Path.GetDirectoryName(physical));
                string temp = $"{physical}.{Guid.NewGuid():N}.tmp";   // 先寫暫存檔再改名，同時有人查詢也不會讀到寫一半的檔案
                await File.WriteAllBytesAsync(temp, audio);
                File.Move(temp, physical, overwrite: true);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "NPC 語音存檔失敗，下次查詢再試：{Url}", url);
                return false;
            }
        }

        private NpcSpeakResponse Fail(string jobId, NpcVoiceJobs.Job job, string error)
        {
            job.error = error;
            return FailedResponse(jobId, error);
        }

        private static string FailureMessage(VlogJobApiResponse r)
        {
            string error = r.error switch
            {
                { ValueKind: JsonValueKind.String } e => e.GetString(),
                { ValueKind: JsonValueKind.Object or JsonValueKind.Array } e => VlogAiGateway.AiErrorMessage(e.GetRawText()),
                _ => null
            };
            return string.IsNullOrWhiteSpace(error) ? $"AI 語音生成失敗（status={r.status}）" : $"AI 語音生成失敗：{error}";
        }

        #endregion

        #region 檔案

        /// <summary>做好過的語音網址（站內路徑）；還沒做過回傳 null</summary>
        public string CachedAudioPath(string npcName, string text)
        {
            string jobId = JobId(npcName, text?.Trim() ?? "");
            return File.Exists(PhysicalPath(jobId)) ? AudioPath(jobId) : null;
        }

        /// <summary>NPC 名字 + 文字的 SHA-1，當作 job_id 與語音檔名</summary>
        internal static string JobId(string npcName, string text) =>
            Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(npcName + "\n" + text))).ToLowerInvariant();

        /// <summary>job_id 只會是 40 個小寫十六進位字元，其他的一律不認（也避免被拿來組出任意路徑）</summary>
        private static bool IsJobId(string jobId) =>
            jobId?.Length == 40 && jobId.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

        internal static string AudioPath(string jobId) => $"{AudioFolder}{jobId}.wav";

        private string PhysicalPath(string jobId) => Path.Combine(_outputRoot, "audio", "npc", jobId + ".wav");

        #endregion

        private HttpClient CreateClient()
        {
            HttpClient client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        }

        /// <summary>audio_url 回傳站內路徑，由 Controller 組成完整網址</summary>
        private static NpcSpeakResponse DoneResponse(string jobId) =>
            new() { status = NpcSpeakResponse.Done, job_id = jobId, audio_url = AudioPath(jobId) };

        private static NpcSpeakResponse ProcessingResponse(string jobId, VlogJobApiResponse ai) =>
            new() { status = NpcSpeakResponse.Processing, job_id = jobId, stage = ai.stage, queue_position = ai.queue_position };

        private static NpcSpeakResponse FailedResponse(string jobId, string error) =>
            new() { status = NpcSpeakResponse.Failed, job_id = jobId, error = error };
    }
}
