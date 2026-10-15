// 檔案路徑：System\Services\Vlog\VlogAiGateway.cs
// 商家 VLOG 與遊客 VLOG 共用：把照片打包成 zip、送出影片合成、查詢合成進度。
// 外部 AI 服務端點：POST /api/visitor/vlog/create_final（排隊模式，先回 task_id）、GET /api/jobs/{task_id}（查詢進度）；
// AI 重啟後 jobs 查不到任務時，才用 GET /api/check_status/{task_id} 確認影片檔是否已經做好。
// create_final 的 bgm_file 是必填，預設用 wwwroot/bgm/default_bgm.mp3（可用 VlogSettings:BgmPath 換）。
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using backend.utils;
using backend.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace backend.Services
{
    /// <summary>外部影片合成任務的狀態</summary>
    public enum VlogTaskState { Processing, Ready, Failed }

    public class VlogAiGateway
    {
        private const string CreateFinalPath = "/api/visitor/vlog/create_final";
        private const string JobsPath = "/api/jobs/";
        private const string CheckStatusPath = "/api/check_status/";
        private const long MaxRemoteImageBytes = 30 * 1024 * 1024;

        /// <summary>可以放進 image_zip 的照片副檔名</summary>
        public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".heic"
        };

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<VlogAiGateway> _logger;

        public VlogAiGateway(IHttpClientFactory httpClientFactory, IConfiguration configuration, IWebHostEnvironment environment,
                             ILogger<VlogAiGateway> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        /// <summary>
        /// 影片合成用的背景音樂（AI 服務的 bgm_file 是必填）。
        /// 預設 wwwroot/bgm/default_bgm.mp3；VlogSettings:BgmPath 可以換成其他檔案（相對路徑以專案根目錄為準）。
        /// 找不到就丟出錯誤，呼叫端要在送出合成前先呼叫，避免把專案標成合成失敗。
        /// </summary>
        public byte[] LoadBgm()
        {
            string configured = _configuration["VlogSettings:BgmPath"];
            string path = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(_environment.WebRootPath, "bgm", "default_bgm.mp3")
                : Path.IsPathRooted(configured) ? configured : Path.Combine(_environment.ContentRootPath, configured);

            if (!File.Exists(path))
                throw new Exception($"找不到影片背景音樂檔：{path}（請放一個 mp3，或設定 VlogSettings:BgmPath）");
            return File.ReadAllBytes(path);
        }

        public HttpClient CreateClient(TimeSpan timeout)
        {
            HttpClient client = _httpClientFactory.CreateClient();
            client.Timeout = timeout;
            return client;
        }

        /// <summary>讀出 AI 服務回應；非 2xx 或 JSON 解析失敗都丟出帶原始內容的錯誤</summary>
        public static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, string apiName)
        {
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new Exception($"{apiName}失敗（HTTP {(int)response.StatusCode}）：{AiErrorMessage(body)}");

            try
            {
                return JsonSerializer.Deserialize<T>(body, JsonOptions);
            }
            catch (JsonException)
            {
                throw new Exception($"{apiName}回傳格式無法解析：{Truncate(body)}");
            }
        }

        #region 送出影片合成 / 查詢進度

        /// <summary>送出正式影片合成任務，回傳外部 task_id。bgm 用 <see cref="LoadBgm"/> 取得</summary>
        public async Task<VlogCreateFinalApiResponse> CreateFinalAsync(string finalScript, byte[] imageZip, byte[] bgm, string spotMetaJson)
        {
            if (bgm == null || bgm.Length == 0)
                throw new ArgumentException("缺少背景音樂（AI 服務的 bgm_file 是必填）", nameof(bgm));

            using var form = new MultipartFormDataContent
            {
                { new StringContent(finalScript), "final_script" }
            };

            var zipContent = new ByteArrayContent(imageZip);
            zipContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
            form.Add(zipContent, "image_zip", "images.zip");

            var bgmContent = new ByteArrayContent(bgm);
            bgmContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/mpeg");
            form.Add(bgmContent, "bgm_file", "bgm.mp3");

            if (!string.IsNullOrWhiteSpace(spotMetaJson))
                form.Add(new StringContent(spotMetaJson), "spot_meta_json");

            using HttpClient client = CreateClient(TimeSpan.FromMinutes(3));
            using HttpResponseMessage response = await client.PostAsync(AiServiceConfig.Url(CreateFinalPath), form);
            var result = await ReadJsonAsync<VlogCreateFinalApiResponse>(response, "送出影片合成");

            if (string.IsNullOrWhiteSpace(result?.task_id))
                throw new Exception($"AI 服務沒有回傳任務 ID（status={result?.status}，{result?.message}）");

            return result;
        }

        /// <summary>
        /// 向 AI 查詢排隊中的合成任務。
        /// AI 查不到任務（404：已過期或 AI 服務重啟過，任務紀錄只放在記憶體）時，再看影片檔還在不在：
        /// 還在就當作完成，不在就回傳 status=not_found，呼叫端要標成失敗讓使用者重送；
        /// AI 連不上、逾時或回 5xx 時回傳 null，當作還在處理中、下次輪詢再查，不讓前端的輪詢直接失敗。
        /// </summary>
        public async Task<VlogJobApiResponse> CheckStatusAsync(string taskId)
        {
            try
            {
                using HttpClient client = CreateClient(TimeSpan.FromSeconds(30));
                using HttpResponseMessage response = await client.GetAsync(AiServiceConfig.Url(JobsPath + Uri.EscapeDataString(taskId)));

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return await FindFinishedVideoAsync(client, taskId)
                           ?? new VlogJobApiResponse { status = VlogJobApiResponse.NotFound, task_id = taskId };
                if ((int)response.StatusCode >= 500)
                {
                    _logger.LogWarning("查詢影片合成進度失敗（task_id={TaskId}，HTTP {Status}），下次輪詢再查", taskId, (int)response.StatusCode);
                    return null;
                }

                return await ReadJsonAsync<VlogJobApiResponse>(response, "查詢影片合成進度");
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(e, "連不上 AI 服務查詢影片合成進度（task_id={TaskId}），下次輪詢再查", taskId);
                return null;
            }
        }

        /// <summary>
        /// AI 重啟後 /api/jobs 會忘記做完的任務，但影片檔還在：用 /api/check_status 看檔案，有下載網址就回傳完成，沒有回傳 null。
        /// （check_status 對不認得的任務一律回 processing，所以只拿來確認影片檔）
        /// </summary>
        private static async Task<VlogJobApiResponse> FindFinishedVideoAsync(HttpClient client, string taskId)
        {
            using HttpResponseMessage response = await client.GetAsync(AiServiceConfig.Url(CheckStatusPath + Uri.EscapeDataString(taskId)));
            if (!response.IsSuccessStatusCode) return null;

            VlogCheckStatusApiResponse file;
            try
            {
                file = JsonSerializer.Deserialize<VlogCheckStatusApiResponse>(await response.Content.ReadAsStringAsync(), JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(file?.download_url)) return null;

            return new VlogJobApiResponse
            {
                status = "done",
                task_id = taskId,
                result = new VlogJobResult { filename = file.filename, download_url = file.download_url }
            };
        }

        /// <summary>
        /// /api/jobs 的狀態：queued、running 是處理中；done 要有影片網址才算完成；failed 或查不到任務算失敗。
        /// 沒有回應（AI 暫時連不上）當作處理中。
        /// </summary>
        public static VlogTaskState ParseState(VlogJobApiResponse r)
        {
            if (r == null) return VlogTaskState.Processing;

            string status = (r.status ?? "").Trim().ToLowerInvariant();
            if (status is VlogJobApiResponse.NotFound or "failed" or "failure" or "error" or "cancelled" or "canceled")
                return VlogTaskState.Failed;
            if (!string.IsNullOrWhiteSpace(r.result?.download_url)) return VlogTaskState.Ready;
            if (status is "done" or "completed" or "success" or "succeeded" or "ready") return VlogTaskState.Failed;   // 說完成卻沒給網址
            return VlogTaskState.Processing;
        }

        /// <summary>AI 回傳的影片網址若是相對路徑，補上 AI 服務根網址</summary>
        public static string ResolveUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            return Uri.TryCreate(url, UriKind.Absolute, out Uri abs) && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps)
                ? url
                : AiServiceConfig.Url(url);
        }

        public static string FailureMessage(VlogJobApiResponse r)
        {
            if (r?.status == VlogJobApiResponse.NotFound)
                return "AI 服務找不到這個合成任務（可能已過期或 AI 服務重啟過），請重新送出";

            string error = r?.error switch
            {
                { ValueKind: JsonValueKind.String } e => e.GetString(),
                { ValueKind: JsonValueKind.Object or JsonValueKind.Array } e => AiErrorMessage(e.GetRawText()),
                _ => null
            };
            return string.IsNullOrWhiteSpace(error)
                ? $"AI 影片合成失敗（status={r?.status}）"
                : $"AI 影片合成失敗：{error}";
        }

        /// <summary>正在送出合成的影片（key 由呼叫端組，例如 merchant:12）</summary>
        private static readonly ConcurrentDictionary<string, byte> Submitting = new();

        /// <summary>
        /// 前端逾時重送時，上一個請求可能還在打包照片、等 AI 回 task_id：同一支影片同時只讓一個請求送出合成，
        /// 其他的直接擋下，避免 AI 排進重複的任務。用 using 包住，結束時自動放開。
        /// </summary>
        public static IDisposable BeginSubmit(string key)
        {
            if (!Submitting.TryAdd(key, 0))
                throw new Exception("影片正在送出合成，請稍候再用 Status 查詢進度");
            return new SubmitLock(key);
        }

        private sealed class SubmitLock : IDisposable
        {
            private readonly string _key;
            public SubmitLock(string key) => _key = key;
            public void Dispose() => Submitting.TryRemove(_key, out _);
        }

        #endregion

        #region 打包照片

        /// <summary>
        /// 依序把照片放進 zip。本機 /uploads/ 底下的檔案直接讀硬碟，其他 http(s) 網址才下載。
        /// </summary>
        /// <param name="entries">zip 內檔名與照片網址</param>
        public async Task<byte[]> BuildZipAsync(IEnumerable<(string entryName, string url)> entries)
        {
            using HttpClient client = CreateClient(TimeSpan.FromMinutes(2));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PlayTaiwan/1.0 (vlog)");   // Wikimedia 等圖庫會擋沒有 User-Agent 的下載
            using var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
            {
                foreach (var (entryName, url) in entries)
                {
                    byte[] bytes = await ReadImageAsync(client, url);
                    ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                    using Stream entryStream = entry.Open();
                    await entryStream.WriteAsync(bytes);
                }
            }
            return zipStream.ToArray();
        }

        private static async Task<byte[]> ReadImageAsync(HttpClient client, string url)
        {
            string localPath = LocalUploadPath(url);
            if (localPath != null) return await File.ReadAllBytesAsync(localPath);

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new Exception($"照片網址無效或檔案不存在：{url}");

            using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"下載照片失敗（HTTP {(int)response.StatusCode}）：{url}");
            if (response.Content.Headers.ContentLength > MaxRemoteImageBytes)
                throw new Exception($"照片超過 {MaxRemoteImageBytes / 1024 / 1024}MB：{url}");

            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <summary>網址指向本機 wwwroot/uploads 底下的既有檔案時，回傳實體路徑；否則回傳 null</summary>
        public static string LocalUploadPath(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            string path = url.StartsWith("/")
                ? url.Split('?', '#')[0]
                : Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ? uri.AbsolutePath : null;
            if (path == null) return null;

            path = Uri.UnescapeDataString(path);
            if (!path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) return null;

            string uploadsRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads"));
            string fullPath = Path.GetFullPath(Path.Combine(uploadsRoot, path.Substring("/uploads/".Length)));

            // 擋掉 ../ 跳出 uploads 資料夾
            if (!fullPath.StartsWith(uploadsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            return File.Exists(fullPath) ? fullPath : null;
        }

        /// <summary>網址的照片副檔名；沒有或不認得時一律當 .jpg</summary>
        public static string ImageExtension(string url)
        {
            string path = Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ? uri.AbsolutePath : (url ?? "").Split('?', '#')[0];
            string ext = Path.GetExtension(path);
            return ImageExtensions.Contains(ext) ? ext.ToLowerInvariant() : ".jpg";
        }

        /// <summary>網址看起來是照片（影片、錄音不能放進 image_zip）</summary>
        public static bool LooksLikeImage(string url)
        {
            string path = Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ? uri.AbsolutePath : (url ?? "").Split('?', '#')[0];
            string ext = Path.GetExtension(path);
            return string.IsNullOrEmpty(ext) || ImageExtensions.Contains(ext);
        }

        #endregion

        /// <summary>SEO 關鍵字 → 去重、補 # 的 TAG 清單</summary>
        public static List<string> ToHashtags(IEnumerable<string> keywords) =>
            (keywords ?? Enumerable.Empty<string>())
                .Select(k => (k ?? "").Trim().TrimStart('#').Replace(" ", ""))
                .Where(k => k.Length > 0)
                .Select(k => "#" + k)
                .Distinct()
                .ToList();

        public static List<string> SplitList(string joined) =>
            string.IsNullOrWhiteSpace(joined)
                ? new List<string>()
                : joined.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        public static string StatusText(int status) => status switch
        {
            1 => "草稿",
            2 => "處理中",
            3 => "已完成",
            4 => "失敗",
            _ => "未知"
        };

        /// <summary>
        /// AI 服務的錯誤回應取出人看得懂的訊息：{"message": ...}，或 FastAPI 驗證錯誤 {"detail": [{"loc": [...], "msg": ...}]}；
        /// 都不是就回傳原始內容（截斷）
        /// </summary>
        public static string AiErrorMessage(string body)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String)
                        return m.GetString();
                    if (root.TryGetProperty("detail", out JsonElement d))
                    {
                        if (d.ValueKind == JsonValueKind.String) return d.GetString();
                        if (d.ValueKind == JsonValueKind.Array)
                            return string.Join("；", d.EnumerateArray().Select(e =>
                            {
                                string field = e.TryGetProperty("loc", out JsonElement loc) && loc.ValueKind == JsonValueKind.Array
                                    ? loc.EnumerateArray().LastOrDefault().ToString() : "";
                                string msg = e.TryGetProperty("msg", out JsonElement mm) ? mm.GetString() : e.ToString();
                                return string.IsNullOrEmpty(field) ? msg : $"{field}: {msg}";
                            }));
                    }
                }
            }
            catch (JsonException)
            {
                // 不是 JSON，回傳原文
            }
            return Truncate(body);
        }

        private static string Truncate(string s) => s == null ? "" : s.Length > 500 ? s.Substring(0, 500) + "…" : s;
    }
}
