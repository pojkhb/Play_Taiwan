// 檔案路徑：System\Services\Vlog\VlogAiGateway.cs
// 商家 VLOG 與遊客 VLOG 共用：把照片打包成 zip、送出影片合成、查詢合成進度。
// 外部 AI 服務端點：POST /api/visitor/vlog/create_final（排隊模式，先回 task_id）、GET /api/jobs/{task_id}（查詢進度）；
// AI 重啟後 jobs 查不到任務時，才用 GET /api/check_status/{task_id} 確認影片檔是否已經做好。
// create_final 的 spot_meta_json 是必填，每張照片剛好一筆；背景音樂由 AI 依旁白自動挑選，不用附檔。
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.Tasks;
using backend.utils;
using backend.ViewModels;
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

        /// <summary>照片副檔名（使用者上傳、歷程照片都認得）</summary>
        public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".heic"
        };

        /// <summary>
        /// AI 影片合成的 image_zip 只收這些格式。其他格式（例如 HEIC）AI 會直接略過，
        /// 跟 spot_meta_json 對不上，整個請求就會被退回，所以不能放進 zip
        /// </summary>
        public static readonly HashSet<string> ComposableExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };

        /// <summary>
        /// AI 服務 create_final 的 bgm_mood 選項（名稱照 AI 文件；AI 的預設是 auto）。
        /// 下拉選單 API、送出前的檢查、Swagger 的可選值都用這份，AI 增減選項時只要改這裡
        /// </summary>
        public static readonly IReadOnlyList<VlogOptionItem> BgmMoodOptions = new List<VlogOptionItem>
        {
            new() { value = "auto", label = "自動選曲", description = "AI 分析旁白和旅遊感想，自動挑選配樂", is_default = true },
            new() { value = "cheerful", label = "輕快" },
            new() { value = "relaxed", label = "悠閒" },
            new() { value = "warm", label = "溫馨" },
            new() { value = "nostalgic", label = "回憶" },
            new() { value = "adventurous", label = "探索" },
            new() { value = "neutral", label = "中性" },
            new() { value = "none", label = "不配樂", description = "只有旁白，沒有背景音樂" },
        };

        /// <summary>AI 服務 create_final 的 video_orientation 選項（AI 的預設是 landscape）</summary>
        public static readonly IReadOnlyList<VlogOptionItem> VideoOrientationOptions = new List<VlogOptionItem>
        {
            new() { value = "landscape", label = "橫式", description = "1920×1080", is_default = true },
            new() { value = "portrait", label = "直式", description = "1080×1920" },
        };

        /// <summary>create_final 的 user_memory_text 字數上限</summary>
        public const int MaxUserMemoryTextLength = 3000;

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        /// <summary>spot_meta_json：中文不跳脫，沒有值的選填欄位不送</summary>
        private static readonly JsonSerializerOptions MetaJsonOptions = new()
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<VlogAiGateway> _logger;

        public VlogAiGateway(IHttpClientFactory httpClientFactory, ILogger<VlogAiGateway> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
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

        /// <summary>
        /// 送出正式影片合成任務，回傳外部 task_id。
        /// 照片依 photos 的順序打包成 image_zip（檔名用 meta.file_name），spot_meta_json 也照同樣順序一張一筆，保證兩邊對得上；
        /// 照片要先用 <see cref="CanCompose"/> 篩過。options 用 <see cref="ParseOptions"/> 整理，沒有值的欄位不送（AI 用預設值）
        /// </summary>
        public async Task<VlogCreateFinalApiResponse> CreateFinalAsync(string finalScript, IReadOnlyList<(VlogSpotMeta meta, string url)> photos,
                                                                       VlogCreateOptions options = null)
        {
            if (photos == null || photos.Count == 0)
                throw new ArgumentException("沒有可以合成影片的照片", nameof(photos));

            byte[] imageZip = await BuildZipAsync(photos.Select(p => (p.meta.file_name, p.url)));
            string spotMetaJson = JsonSerializer.Serialize(photos.Select(p => p.meta), MetaJsonOptions);

            using var form = new MultipartFormDataContent
            {
                { new StringContent(finalScript), "final_script" },
                { new StringContent(spotMetaJson), "spot_meta_json" }
            };

            var zipContent = new ByteArrayContent(imageZip);
            zipContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
            form.Add(zipContent, "image_zip", "images.zip");

            if (options?.user_memory_text != null) form.Add(new StringContent(options.user_memory_text), "user_memory_text");
            if (options?.bgm_mood != null) form.Add(new StringContent(options.bgm_mood), "bgm_mood");
            if (options?.video_orientation != null) form.Add(new StringContent(options.video_orientation), "video_orientation");

            using HttpClient client = CreateClient(TimeSpan.FromMinutes(3));
            using HttpResponseMessage response = await client.PostAsync(AiServiceConfig.Url(CreateFinalPath), form);
            var result = await ReadJsonAsync<VlogCreateFinalApiResponse>(response, "送出影片合成");

            if (string.IsNullOrWhiteSpace(result?.task_id))
                throw new Exception($"AI 服務沒有回傳任務 ID（status={result?.status}，{result?.message}）");

            return result;
        }

        /// <summary>
        /// 檢查並整理前端帶來的合成選項：去頭尾空白、bgm_mood / video_orientation 轉小寫；空的維持 null（不送，AI 用預設值）。
        /// 不合規定直接丟出錯誤，要在送出合成前呼叫，才不會把 VLOG 標成合成失敗
        /// </summary>
        public static VlogCreateOptions ParseOptions(string userMemoryText, string bgmMood, string videoOrientation)
        {
            string memory = string.IsNullOrWhiteSpace(userMemoryText) ? null : userMemoryText.Trim();
            string mood = string.IsNullOrWhiteSpace(bgmMood) ? null : bgmMood.Trim().ToLowerInvariant();
            string orientation = string.IsNullOrWhiteSpace(videoOrientation) ? null : videoOrientation.Trim().ToLowerInvariant();

            // AI 用 Python 的 len 算字數（Unicode 字元數），emoji 也算一個字
            if (memory != null && memory.EnumerateRunes().Count() > MaxUserMemoryTextLength)
                throw new Exception($"旅遊感想 (user_memory_text) 最多 {MaxUserMemoryTextLength} 字");
            if (mood != null && BgmMoodOptions.All(o => o.value != mood))
                throw new Exception($"配樂風格 (bgm_mood) 只能是 {string.Join("、", BgmMoodOptions.Select(o => o.value))}");
            if (orientation != null && VideoOrientationOptions.All(o => o.value != orientation))
                throw new Exception($"影片方向 (video_orientation) 只能是 {string.Join("、", VideoOrientationOptions.Select(o => o.value))}");

            return new VlogCreateOptions { user_memory_text = memory, bgm_mood = mood, video_orientation = orientation };
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
        private async Task<byte[]> BuildZipAsync(IEnumerable<(string entryName, string url)> entries)
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
            string ext = UrlExtension(url);
            return ImageExtensions.Contains(ext) ? ext.ToLowerInvariant() : ".jpg";
        }

        /// <summary>網址看起來是照片（不是影片、錄音）</summary>
        public static bool LooksLikeImage(string url)
        {
            string ext = UrlExtension(url);
            return string.IsNullOrEmpty(ext) || ImageExtensions.Contains(ext);
        }

        /// <summary>照片可以放進影片合成：jpg/png/webp，或沒有副檔名（當作 jpg）；HEIC、影片、錄音都不行</summary>
        public static bool CanCompose(string url)
        {
            string ext = UrlExtension(url);
            return string.IsNullOrEmpty(ext) || ComposableExtensions.Contains(ext);
        }

        private static string UrlExtension(string url)
        {
            string path = Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ? uri.AbsolutePath : (url ?? "").Split('?', '#')[0];
            return Path.GetExtension(path);
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
        /// AI 服務的錯誤回應取出人看得懂的訊息：{"message": ...}、FastAPI 驗證錯誤 {"detail": [{"loc": [...], "msg": ...}]}，
        /// 或 {"detail": {"message": ..., 其他說明欄位}}（例如照片和地點資料對不上時列出的檔名）；都不是就回傳原始內容（截斷）
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
                        if (d.ValueKind == JsonValueKind.Object && d.TryGetProperty("message", out JsonElement dm) && dm.ValueKind == JsonValueKind.String)
                        {
                            string extra = string.Join("；", d.EnumerateObject()
                                .Where(p => p.Name != "message")
                                .Select(p => $"{p.Name}: {p.Value.GetRawText()}"));
                            return Truncate(extra.Length == 0 ? dm.GetString() : $"{dm.GetString()}（{extra}）");
                        }
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
