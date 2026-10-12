// 檔案路徑：System\Services\Common\AiTrafficLogHandler.cs
// 記錄後端打到 AI Service 的每一次 request / response（掛在所有 IHttpClientFactory 建立的 HttpClient 上，只記錄 AI Service 的網址）
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using backend.utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace backend.Services
{
    /// <summary>
    /// 每次呼叫 AI Service 寫一個 JSON 檔到 logs/ai/{日期}/{時間}_{方法}_{路徑}.json，內容有 request、response、耗時與錯誤。
    /// 圖片、音訊、影片等檔案只記類型與大小；寫檔失敗不影響 AI 呼叫。
    /// </summary>
    public class AiTrafficLogHandler : DelegatingHandler
    {
        /// <summary>單一 body 最多記錄的字數，超過的截斷</summary>
        private const int MaxBodyChars = 2_000_000;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping   // 中文不轉成 \uXXXX
        };

        private static int _sequence;

        private readonly string _logRoot;
        private readonly ILogger<AiTrafficLogHandler> _logger;

        public AiTrafficLogHandler(IConfiguration configuration, IWebHostEnvironment environment, ILogger<AiTrafficLogHandler> logger)
        {
            // 預設放專案目錄的 logs/ai（測試時改到暫存資料夾）
            _logRoot = string.IsNullOrWhiteSpace(configuration["AiService:TrafficLogRoot"])
                ? Path.Combine(environment.ContentRootPath, "logs", "ai")
                : configuration["AiService:TrafficLogRoot"];
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!IsAiService(request.RequestUri))
                return await base.SendAsync(request, cancellationToken);

            DateTime startedAt = DateTime.Now;
            var log = new JsonObject
            {
                ["time"] = startedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                ["method"] = request.Method.Method,
                ["url"] = request.RequestUri.ToString(),
                ["request"] = await DescribeContentAsync(request.Content)
            };

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
                log["duration_ms"] = stopwatch.ElapsedMilliseconds;
                log["response"] = await DescribeResponseAsync(response);
                await WriteLogAsync(startedAt, request, log);
                return response;
            }
            catch (Exception ex)
            {
                // 逾時（HttpClient.Timeout）也會走到這裡
                log["duration_ms"] = stopwatch.ElapsedMilliseconds;
                log["error"] = $"{ex.GetType().Name}: {ex.Message}";
                await WriteLogAsync(startedAt, request, log);
                throw;
            }
        }

        /// <summary>網址的 host:port 跟 AiService:BaseUrl 相同才記錄（AI 回傳的下載網址也算）</summary>
        private static bool IsAiService(Uri uri)
        {
            return uri != null
                && Uri.TryCreate(AiServiceConfig.BaseUrl, UriKind.Absolute, out Uri aiBase)
                && string.Equals(uri.Authority, aiBase.Authority, StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<JsonObject> DescribeContentAsync(HttpContent content)
        {
            if (content == null) return null;

            string contentType = content.Headers.ContentType?.MediaType;
            var result = new JsonObject { ["content_type"] = contentType };

            if (content is MultipartContent multipart)
            {
                var parts = new JsonArray();
                foreach (HttpContent part in multipart)
                {
                    // 先把每一段讀進記憶體，之後實際送出時才能再讀一次（IFormFile 的 stream 只能讀一次）
                    await part.LoadIntoBufferAsync();
                    var described = new JsonObject
                    {
                        ["name"] = part.Headers.ContentDisposition?.Name?.Trim('"'),
                        ["file_name"] = part.Headers.ContentDisposition?.FileName?.Trim('"'),
                    };
                    if (described["file_name"] == null && IsText(part.Headers.ContentType?.MediaType ?? "text/plain"))
                    {
                        described["value"] = ParseBody(await part.ReadAsStringAsync());
                    }
                    else
                    {
                        described["content_type"] = part.Headers.ContentType?.MediaType;
                        described["bytes"] = (await part.ReadAsByteArrayAsync()).Length;
                    }
                    parts.Add(described);
                }
                result["parts"] = parts;
                return result;
            }

            await content.LoadIntoBufferAsync();
            if (string.Equals(contentType, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            {
                // text=%E8%96%AF... 解碼成欄位，中文才看得懂
                var fields = new JsonObject();
                foreach (string pair in (await content.ReadAsStringAsync()).Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] kv = pair.Split('=', 2);
                    fields[Decode(kv[0])] = kv.Length > 1 ? Decode(kv[1]) : "";
                }
                result["fields"] = fields;
            }
            else if (IsText(contentType))
                result["body"] = ParseBody(await content.ReadAsStringAsync());
            else
                result["bytes"] = (await content.ReadAsByteArrayAsync()).Length;
            return result;
        }

        private static async Task<JsonObject> DescribeResponseAsync(HttpResponseMessage response)
        {
            string contentType = response.Content?.Headers.ContentType?.MediaType;
            var result = new JsonObject
            {
                ["status"] = (int)response.StatusCode,
                ["content_type"] = contentType
            };

            if (response.Content == null) return result;

            if (IsText(contentType))
            {
                // 讀進記憶體，呼叫端之後還能照常讀 response
                await response.Content.LoadIntoBufferAsync();
                result["body"] = ParseBody(await response.Content.ReadAsStringAsync());
            }
            else
            {
                // 音訊、影片等檔案不讀內容，避免把大檔整個載入記憶體
                result["bytes"] = response.Content.Headers.ContentLength;
            }
            return result;
        }

        private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

        private static bool IsText(string mediaType)
        {
            if (string.IsNullOrEmpty(mediaType)) return false;
            return mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>JSON 就存成 JSON（方便閱讀），不是 JSON 就存原文</summary>
        private static JsonNode ParseBody(string text)
        {
            if (text.Length > MaxBodyChars)
                return JsonValue.Create(text.Substring(0, MaxBodyChars) + $"…（已截斷，原長度 {text.Length} 字）");
            try
            {
                return JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                return JsonValue.Create(text);
            }
        }

        private async Task WriteLogAsync(DateTime startedAt, HttpRequestMessage request, JsonObject log)
        {
            try
            {
                string dir = Path.Combine(_logRoot, startedAt.ToString("yyyy-MM-dd"));
                Directory.CreateDirectory(dir);

                string path = request.RequestUri.AbsolutePath.Trim('/').Replace('/', '-');
                path = new string(path.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
                if (path.Length > 80) path = path.Substring(0, 80);

                int sequence = Interlocked.Increment(ref _sequence) % 1000;
                string fileName = $"{startedAt:HHmmss_fff}_{sequence:D3}_{request.Method.Method}_{path}.json";

                await File.WriteAllTextAsync(Path.Combine(dir, fileName), log.ToJsonString(JsonOptions), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("AI 呼叫紀錄寫檔失敗: {Message}", ex.Message);
            }
        }
    }
}
