// 檔案路徑：System\Services\Vlog\VlogAiGateway.cs
// 商家 VLOG 與遊客 VLOG 共用：把照片打包成 zip、送出影片合成、查詢合成進度。
// 外部 AI 服務端點：POST /api/visitor/vlog/create_final、GET /api/check_status/{task_id}。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using backend.utils;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>外部影片合成任務的狀態</summary>
    public enum VlogTaskState { Processing, Ready, Failed }

    public class VlogAiGateway
    {
        private const string CreateFinalPath = "/api/visitor/vlog/create_final";
        private const string CheckStatusPath = "/api/check_status/";
        private const long MaxRemoteImageBytes = 30 * 1024 * 1024;

        /// <summary>可以放進 image_zip 的照片副檔名</summary>
        public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".heic"
        };

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly IHttpClientFactory _httpClientFactory;

        public VlogAiGateway(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
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
                throw new Exception($"{apiName}失敗（HTTP {(int)response.StatusCode}）：{Truncate(body)}");

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

        /// <summary>送出正式影片合成任務，回傳外部 task_id</summary>
        public async Task<VlogCreateFinalApiResponse> CreateFinalAsync(string finalScript, byte[] imageZip, byte[] bgm, string spotMetaJson)
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(finalScript), "final_script" }
            };

            var zipContent = new ByteArrayContent(imageZip);
            zipContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
            form.Add(zipContent, "image_zip", "images.zip");

            if (bgm != null)
            {
                var bgmContent = new ByteArrayContent(bgm);
                bgmContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/mpeg");
                form.Add(bgmContent, "bgm_file", "bgm.mp3");
            }

            if (!string.IsNullOrWhiteSpace(spotMetaJson))
                form.Add(new StringContent(spotMetaJson), "spot_meta_json");

            using HttpClient client = CreateClient(TimeSpan.FromMinutes(3));
            using HttpResponseMessage response = await client.PostAsync(AiServiceConfig.Url(CreateFinalPath), form);
            var result = await ReadJsonAsync<VlogCreateFinalApiResponse>(response, "送出影片合成");

            if (string.IsNullOrWhiteSpace(result?.task_id))
                throw new Exception($"AI 服務沒有回傳任務 ID（status={result?.status}，{result?.message}）");

            return result;
        }

        public async Task<VlogTaskStatusApiResponse> CheckStatusAsync(string taskId)
        {
            using HttpClient client = CreateClient(TimeSpan.FromSeconds(30));
            using HttpResponseMessage response = await client.GetAsync(AiServiceConfig.Url(CheckStatusPath + Uri.EscapeDataString(taskId)));
            return await ReadJsonAsync<VlogTaskStatusApiResponse>(response, "查詢影片合成進度");
        }

        /// <summary>
        /// AI 文件目前連不上，狀態字串採寬鬆判斷：失敗類字眼算失敗、有影片網址才算完成，其餘都當處理中。
        /// </summary>
        public static VlogTaskState ParseState(VlogTaskStatusApiResponse r)
        {
            string status = (r?.status ?? "").Trim().ToLowerInvariant();
            if (status is "failed" or "failure" or "error") return VlogTaskState.Failed;
            if (!string.IsNullOrWhiteSpace(r?.download_url)) return VlogTaskState.Ready;
            if (status is "ready" or "completed" or "done" or "success") return VlogTaskState.Failed;   // 說完成卻沒給網址
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

        public static string FailureMessage(VlogTaskStatusApiResponse r) =>
            string.IsNullOrWhiteSpace(r?.message)
                ? $"AI 影片合成失敗（status={r?.status}）"
                : $"AI 影片合成失敗：{r.message}";

        #endregion

        #region 打包照片

        /// <summary>
        /// 依序把照片放進 zip。本機 /uploads/ 底下的檔案直接讀硬碟，其他 http(s) 網址才下載。
        /// </summary>
        /// <param name="entries">zip 內檔名與照片網址</param>
        public async Task<byte[]> BuildZipAsync(IEnumerable<(string entryName, string url)> entries)
        {
            using HttpClient client = CreateClient(TimeSpan.FromMinutes(2));
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

        private static string Truncate(string s) => s == null ? "" : s.Length > 500 ? s.Substring(0, 500) + "…" : s;
    }
}
