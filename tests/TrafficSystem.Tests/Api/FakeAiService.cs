// 模擬外部 AI 服務（aiservice.angelalala.com）：
// 攔下後端所有往外的 HTTP 請求，依路徑回應固定內容，並記下收到的表單欄位與檔案，讓測試檢查後端送了什麼。
// 路徑與欄位格式跟 VlogAiGateway / MerchantVlogService 呼叫的正式端點相同。
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TrafficSystem.Tests.Api;

public class FakeAiService : HttpMessageHandler
{
    /// <summary>收到的一次請求：路徑 + multipart 各欄位的內容（文字欄位存字串、檔案欄位存 bytes）</summary>
    public record Received(string Method, string Path, Dictionary<string, string> Fields, Dictionary<string, byte[]> Files);

    public ConcurrentQueue<Received> Requests { get; } = new();

    /// <summary>模擬外部圖片網址（景點照片）：完整網址 → 圖片 bytes</summary>
    public ConcurrentDictionary<string, byte[]> Images { get; } = new();

    /// <summary>影片合成任務查詢（/api/jobs/{TaskId}）目前要回的內容，測試可以改；null 代表 AI 查不到任務（回 404，例如 AI 服務重啟過）</summary>
    public object JobResponse { get; set; } = QueuedJob;

    public const string TaskId = "fake-task-001";

    /// <summary>/api/check_status/{task_id} 要回的內容：跟正式 AI 一樣，影片檔不在時一律回 processing</summary>
    public object CheckStatusResponse { get; set; } = new { status = "processing", task_id = TaskId };

    /// <summary>排隊中的影片合成任務（跟正式 AI 的 /api/jobs 格式相同）</summary>
    public static readonly object QueuedJob = new
    {
        task_id = TaskId, task_type = "vlog", status = "queued", stage = "等待處理",
        result = (object)null, error = (object)null, check_url = "/api/jobs/" + TaskId, queue_position = 1
    };

    /// <summary>做完的影片合成任務，影片網址放在 result.download_url</summary>
    public static object DoneJob(string downloadUrl) => new
    {
        task_id = TaskId, task_type = "vlog", status = "done", stage = "完成",
        result = new { filename = $"vlog_visitor_{TaskId}.mp4", video_orientation = "landscape", download_url = downloadUrl },
        error = (object)null, check_url = "/api/jobs/" + TaskId, queue_position = (int?)null
    };

    /// <summary>失敗的影片合成任務</summary>
    public static object FailedJob(object error) => new
    {
        task_id = TaskId, task_type = "vlog", status = "failed", stage = "失敗",
        result = (object)null, error, check_url = "/api/jobs/" + TaskId, queue_position = (int?)null
    };

    /// <summary>「遊你說了算」背景工作代號</summary>
    public const string TextJobId = "fake-text-job";

    /// <summary>遊你說了算：每次查詢進度依序回傳的內容；用完後一直回最後一個</summary>
    public ConcurrentQueue<object> TextJobPolls { get; } = new();
    private object _lastTextPoll = new { job_id = TextJobId, status = "processing", stage = "排隊中" };

    /// <summary>遊你說了算：模擬 AI 重啟後查不到工作（查詢進度回 404）</summary>
    public bool TextJobLost { get; set; }

    /// <summary>
    /// 現在揪出發的劇本任務（/api/stories/tasks 送出的任務）：AI 任務代號 → GET /api/jobs/{task_id} 要回的內容。
    /// 沒有的代號回 404（AI 查不到任務，例如 AI 服務重啟過）
    /// </summary>
    public ConcurrentDictionary<string, object> StoryJobs { get; } = new();

    /// <summary>劇本任務的查詢結果（跟正式 AI 的 /api/jobs 格式相同）：queued、running、done（result 是一份劇本）、error</summary>
    public static object StoryJob(string taskId, string status, object result = null, string stage = null, string error = null) => new
    {
        task_id = taskId, task_type = "stories_single", status,
        stage = stage ?? status switch { "queued" => "等待處理", "running" => "正在生成一套劇本與節點任務", "done" => "完成", _ => "處理失敗" },
        result, error, check_url = "/api/jobs/" + taskId, queue_position = status == "queued" ? 1 : (int?)null
    };

    /// <summary>
    /// NPC 語音任務（/api/npc/speak 送出的任務）：AI 任務代號 → GET /api/jobs/{task_id} 要回的內容。
    /// 送出時先放排隊中的任務；沒有的代號回 404（AI 查不到任務，例如 AI 服務重啟過）
    /// </summary>
    public ConcurrentDictionary<string, object> NpcJobs { get; } = new();

    /// <summary>最近一次送出的 NPC 語音任務代號</summary>
    public string LastNpcTaskId { get; private set; }

    /// <summary>模擬 AI 任務已滿：/api/npc/speak 回 503</summary>
    public bool NpcSpeakBusy { get; set; }

    private int _npcTaskCount;

    /// <summary>NPC 語音任務的查詢結果（跟正式 AI 的 /api/jobs 格式相同）：queued、running、done（result 是語音檔）、error</summary>
    public static object NpcJob(string taskId, string status, string error = null) => new
    {
        task_id = taskId, task_type = "npc_tts", status,
        stage = status switch { "queued" => "等待處理", "running" => "正在生成 XTTS 語音", "done" => "完成", _ => "處理失敗" },
        result = status == "done"
            ? new { filename = $"tts_{taskId}.wav", download_url = $"https://aiservice.angelalala.com/api/download/tts_{taskId}.wav" }
            : null,
        error, check_url = "/api/jobs/" + taskId, queue_position = status == "queued" ? 2 : (int?)null
    };

    /// <summary>讓 NPC 語音任務做完：查詢回 done，語音檔可以從 AI 服務下載（內容是 audio）</summary>
    public void CompleteNpcJob(string taskId, byte[] audio)
    {
        NpcJobs[taskId] = NpcJob(taskId, "done");
        Images[backend.utils.AiServiceConfig.Url($"/api/download/tts_{taskId}.wav")] = audio;
    }

    public object MerchantPreviewResponse { get; } = new
    {
        status = "success",
        draft_preview = new
        {
            tw_script = "夏天就是要來一碗阿嬤的芒果冰！",
            en_video_prompt = "mango shaved ice",
            promo_copy = "限定芒果冰買一送一，快揪朋友來！",
            seo_keywords = new[] { "台中美食", "#芒果冰", " 芒果 冰 " },
            target_audience = new[] { "學生", "觀光客" }
        }
    };

    /// <summary>遊客 VLOG 旁白草稿（/api/visitor/vlog/preview）要回的內容</summary>
    public object VisitorPreviewResponse { get; } = new
    {
        status = "success",
        draft_preview = new
        {
            itinerary = new[] { new { spot_name = "宮原眼科", location_codename = "紅磚眼科", visit_time = "2026-10-12 21:48", db_description = "日治時期的眼科診所" } },
            suggested_script = "今天跟著鐘聲走了一趟舊城。",
            seo_keywords = new[] { "台中散步" },
            promo_copy = "一起來舊城尋寶！"
        }
    };

    /// <summary>AI Agent 找不到地點時的回應：HTTP 200，只有 message</summary>
    public static readonly object AgentNoPlace = new { message = "附近沒有符合您當下心情的地點。" };

    /// <summary>/api/agent/orchestrate（spin）要回的內容</summary>
    public object AgentOrchestrateResponse { get; set; } = AgentNoPlace;

    /// <summary>生成明信片（/api/postcard/create_ai）要回的內容；null 代表連不上（回 404）</summary>
    public object PostcardResponse { get; set; }

    /// <summary>備援天氣服務（Open-Meteo 的 /v1/forecast）要回的內容；null 代表連不上（回 404）</summary>
    public object WeatherResponse { get; set; }

    /// <summary>中央氣象署的授權碼：請求要帶這一組才會回資料（跟正式服務一樣，沒帶或帶錯就查不到）</summary>
    public const string CwaKey = "test-cwa-key";

    /// <summary>中央氣象署測站即時觀測（O-A0003-001）要回的內容；null 代表連不上（回 404）</summary>
    public object CwaObservationResponse { get; set; }

    /// <summary>中央氣象署縣市天氣預報（F-C0032-001）要回的內容；null 代表連不上（回 404）</summary>
    public object CwaForecastResponse { get; set; }

    /// <summary>Valhalla /route 的主路線（trip）；null 代表 Valhalla 沒開（回 404，後端改用直線估算）</summary>
    public object ValhallaTrip { get; set; }

    /// <summary>Valhalla /route 的替代路線（每一筆是一個 trip）：請求有要替代路線（alternates 大於 0）才會回，最多回要求的條數</summary>
    public List<object> ValhallaAlternates { get; } = new();

    /// <summary>embedding 服務（tools/embedding_service 的 /embed）有沒有開；false 時回 404</summary>
    public bool EmbeddingAvailable { get; set; } = true;

    /// <summary>embedding 服務回的向量：每段文字都是同一個 1024 維、長度 1 的向量</summary>
    public static readonly double[] FakeEmbedding = Enumerable.Repeat(1.0 / 32, 1024).ToArray();

    /// <summary>FastAPI 景點清單假資料</summary>
    public object VisitorAnalysisAttractionsResponse { get; set; } = new
    {
        total = 1,
        page = 1,
        page_size = 20,
        items = new[]
        {
            new
            {
                name = "勤美誠品綠園道",
                address = "臺中市西區",
                uid = "attr-001",
                uids = new[] { "attr-001", "attr-001b" },
                has_analysis = true
            }
        }
    };

    /// <summary>FastAPI 策略生成假資料（預設模擬 not_ready，符合正式未啟用 LLM 行為）</summary>
    public object VisitorAnalysisStrategyResponse { get; set; } = new
    {
        status = "not_ready",
        message = "Ollama / Llama 尚未啟用；Qdrant RAG 尚未啟用。",
        audience_summary = new { has_domestic = true, has_inbound = false },
        strategies = Array.Empty<object>(),
        sources = Array.Empty<object>(),
        rag_status = "not_ready",
        llm_status = "not_ready"
    };

    public void Reset()
    {
        Requests.Clear();
        TextJobPolls.Clear();
        _lastTextPoll = new { job_id = TextJobId, status = "processing", stage = "排隊中" };
        TextJobLost = false;
        StoryJobs.Clear();
        NpcJobs.Clear();
        LastNpcTaskId = null;
        NpcSpeakBusy = false;
        JobResponse = QueuedJob;
        CheckStatusResponse = new { status = "processing", task_id = TaskId };
        AgentOrchestrateResponse = AgentNoPlace;
        PostcardResponse = null;
        WeatherResponse = null;
        CwaObservationResponse = null;
        CwaForecastResponse = null;
        ValhallaTrip = null;
        ValhallaAlternates.Clear();
        VisitorAnalysisAttractionsResponse = new
        {
            total = 1,
            page = 1,
            page_size = 20,
            items = new[]
            {
                new
                {
                    name = "勤美誠品綠園道",
                    address = "臺中市西區",
                    uid = "attr-001",
                    uids = new[] { "attr-001", "attr-001b" },
                    has_analysis = true
                }
            }
        };
        VisitorAnalysisStrategyResponse = new
        {
            status = "not_ready",
            message = "Ollama / Llama 尚未啟用；Qdrant RAG 尚未啟用。",
            audience_summary = new { has_domestic = true, has_inbound = false },
            strategies = Array.Empty<object>(),
            sources = Array.Empty<object>(),
            rag_status = "not_ready",
            llm_status = "not_ready"
        };
    }

    public Received Last(string path) => Requests.LastOrDefault(r => r.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        var fields = new Dictionary<string, string>();
        var files = new Dictionary<string, byte[]>();

        if (request.Content is MultipartFormDataContent form)
        {
            foreach (HttpContent part in form)
            {
                string name = part.Headers.ContentDisposition?.Name?.Trim('"');
                if (part.Headers.ContentDisposition?.FileName != null)
                    files[name] = await part.ReadAsByteArrayAsync(cancellationToken);
                else
                    fields[name] = await part.ReadAsStringAsync(cancellationToken);
            }
        }
        else if (request.Content is FormUrlEncodedContent urlEncoded)
        {
            foreach (string pair in (await urlEncoded.ReadAsStringAsync(cancellationToken)).Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = pair.Split('=', 2);
                fields[Uri.UnescapeDataString(kv[0].Replace('+', ' '))] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
            }
        }
        else if (request.Content != null)
        {
            fields["body"] = await request.Content.ReadAsStringAsync(cancellationToken);   // JSON 請求
        }

        Requests.Enqueue(new Received(request.Method.Method, path, fields, files));

        if (request.Method == HttpMethod.Get && Images.TryGetValue(request.RequestUri.AbsoluteUri, out byte[] image))
        {
            var content = new ByteArrayContent(image);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        if (path == "/api/npc/speak")
            return NpcSpeak(fields);

        if (path == "/api/visitor/vlog/create_final" && CreateFinalError(fields, files) is { } detail)
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { detail }), Encoding.UTF8, "application/json")
            };

        object body = path switch
        {
            "/api/merchant/vlog/preview" => MerchantPreviewResponse,
            "/api/visitor/vlog/preview" => VisitorPreviewResponse,
            "/api/visitor/vlog/create_final" => QueuedJob,   // 排隊模式：先回 task_id（HTTP 202）
            _ when path == "/api/jobs/" + TaskId => JobResponse,
            _ when path.StartsWith("/api/jobs/") && StoryJobs.TryGetValue(path["/api/jobs/".Length..], out object storyJob) => storyJob,
            _ when path.StartsWith("/api/jobs/") && NpcJobs.TryGetValue(path["/api/jobs/".Length..], out object npcJob) => npcJob,
            _ when path == "/api/check_status/" + TaskId => CheckStatusResponse,
            "/upload" => new { pincode = "PT123456", qrcode_base64 = "iVBORw0KGgo=" },   // ibon 列印服務
            "/api/api/admin/generate_script_blueprint_by_text" => new { status = "processing", job_id = TextJobId, check_url = "fake" },
            _ when path == "/api/api/admin/generate_script_blueprint_by_text/" + TextJobId && !TextJobLost => NextTextPoll(),
            "/api/agent/orchestrate" => AgentOrchestrateResponse,
            "/api/postcard/create_ai" => PostcardResponse,
            "/search" => new[] { new { lat = "25.0324", lon = "121.5199" } },   // Nominatim：地名 → 座標
            "/v1/forecast" => WeatherResponse,                                   // Open-Meteo：目前位置的天氣（備援）
            "/api/v1/rest/datastore/O-A0003-001" when HasCwaKey(request) => CwaObservationResponse,   // 中央氣象署：測站即時觀測
            "/api/v1/rest/datastore/F-C0032-001" when HasCwaKey(request) => CwaForecastResponse,      // 中央氣象署：縣市天氣預報
            "/route" when ValhallaTrip != null => ValhallaRoute(fields),          // Valhalla：路線（含替代路線）
            "/embed" when EmbeddingAvailable => Embeddings(fields),               // embedding 服務：商家景點介紹轉向量
            // 客群分析 FastAPI（VisitorAnalysisAi）
            "/internal/visitor-analysis/attractions" => VisitorAnalysisAttractionsResponse,
            "/internal/visitor-analysis/strategy/generate" => VisitorAnalysisStrategyResponse,
            _ => null
        };

        if (body == null)
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("fake ai: no route " + path) };

        return new HttpResponseMessage(path == "/api/visitor/vlog/create_final" ? HttpStatusCode.Accepted : HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
    }

    /// <summary>
    /// 跟正式 AI 一樣：text 不能空白、最多 1000 字，npc_name 只能是有聲音檔的 NPC（不對回 400 / 422）；
    /// 通過就排進隊伍，回 HTTP 202 與排隊中的任務
    /// </summary>
    private HttpResponseMessage NpcSpeak(Dictionary<string, string> fields)
    {
        static HttpResponseMessage Json(HttpStatusCode code, object body) =>
            new(code) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

        string text = fields.GetValueOrDefault("text", "").Trim();
        if (text.Length == 0 || text.EnumerateRunes().Count() > 1000)
            return Json(HttpStatusCode.BadRequest, new { detail = "台詞不可為空白或超過 1000 字" });
        if (!NpcVoices.Contains(fields.GetValueOrDefault("npc_name", "珍奶奶")))
            return Json(HttpStatusCode.UnprocessableEntity, new { detail = "npc_name 無效" });
        if (NpcSpeakBusy)
            return Json(HttpStatusCode.ServiceUnavailable, new { detail = "目前任務已滿，請稍後重試" });

        string taskId = $"npc-task-{Interlocked.Increment(ref _npcTaskCount)}";
        object job = NpcJob(taskId, "queued");
        NpcJobs[taskId] = job;
        LastNpcTaskId = taskId;
        return Json(HttpStatusCode.Accepted, job);
    }

    /// <summary>正式 AI 有聲音檔的 NPC（npc_audio/*.wav）</summary>
    private static readonly HashSet<string> NpcVoices = new() { "薯光", "珍奶奶", "阿達力", "墨先生", "霓霓", "阿吉伯" };

    /// <summary>跟真的 embedding 服務一樣：送幾段文字就回幾個向量</summary>
    private static object Embeddings(Dictionary<string, string> fields)
    {
        using JsonDocument doc = JsonDocument.Parse(fields["body"]);
        int count = doc.RootElement.GetProperty("texts").GetArrayLength();
        return new { model = "intfloat/multilingual-e5-large", dimension = 1024, embeddings = Enumerable.Repeat(FakeEmbedding, count) };
    }

    /// <summary>正式 AI 的 image_zip 只認得這些格式，其他檔案直接略過</summary>
    private static readonly HashSet<string> ZipImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp" };

    private static readonly HashSet<string> BgmMoods = new() { "auto", "cheerful", "relaxed", "warm", "nostalgic", "adventurous", "neutral", "none" };

    /// <summary>
    /// 跟正式 AI 一樣檢查 create_final：spot_meta_json 必填、每筆都要有 file_name 和 spot_name，
    /// 而且要跟 image_zip 裡認得的照片一對一。有問題回傳 detail（正式 AI 回 400 的內容），沒問題回傳 null
    /// </summary>
    private static object CreateFinalError(Dictionary<string, string> fields, Dictionary<string, byte[]> files)
    {
        if (!fields.TryGetValue("spot_meta_json", out string json) || !files.TryGetValue("image_zip", out byte[] zipBytes))
            return "缺少 spot_meta_json 或 image_zip";

        // 選填欄位：正式 AI 用 enum 檢查，值不對會被退回
        if (fields.TryGetValue("bgm_mood", out string mood) && !BgmMoods.Contains(mood)) return $"bgm_mood 無效：{mood}";
        if (fields.TryGetValue("video_orientation", out string orientation) && orientation is not ("portrait" or "landscape"))
            return $"video_orientation 無效：{orientation}";
        if (fields.TryGetValue("user_memory_text", out string memory) && memory.Trim().EnumerateRunes().Count() > 3000)
            return "原始旅遊感想最多 3000 字";

        List<JsonElement> meta;
        try
        {
            meta = JsonDocument.Parse(json).RootElement.EnumerateArray().ToList();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return "spot_meta_json 必須是非空的物件陣列";
        }

        static string Text(JsonElement item, string name) =>
            item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        if (meta.Count == 0) return "spot_meta_json 必須是非空的物件陣列";
        for (int i = 0; i < meta.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(Text(meta[i], "file_name"))) return $"第 {i + 1} 筆缺少 file_name";
            if (string.IsNullOrWhiteSpace(Text(meta[i], "spot_name"))) return $"第 {i + 1} 筆缺少 spot_name";
        }

        List<string> named = meta.Select(m => Text(m, "file_name").Trim()).ToList();
        if (named.Distinct().Count() != named.Count) return "圖片重複指定";

        using var zip = new ZipArchive(new MemoryStream(zipBytes));
        HashSet<string> images = zip.Entries.Select(e => Path.GetFileName(e.FullName))
            .Where(n => ZipImageExtensions.Contains(Path.GetExtension(n))).ToHashSet();
        if (images.SetEquals(named)) return null;

        return new
        {
            message = "圖片與地點資料無法完整配對",
            metadata_file_not_found = named.Except(images).ToArray(),
            images_without_metadata = images.Except(named).ToArray(),
        };
    }

    private static bool HasCwaKey(HttpRequestMessage request) =>
        request.RequestUri!.Query.Contains("Authorization=" + CwaKey);

    /// <summary>跟真的 Valhalla 一樣：請求的 alternates 是幾，最多就回幾條替代路線；沒要就只有主路線</summary>
    private object ValhallaRoute(Dictionary<string, string> fields)
    {
        int requested = 0;
        if (fields.TryGetValue("body", out string body))
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("alternates", out JsonElement n) && n.ValueKind == JsonValueKind.Number)
                requested = n.GetInt32();
        }

        return new { trip = ValhallaTrip, alternates = ValhallaAlternates.Take(requested).Select(trip => new { trip }).ToArray() };
    }

    private object NextTextPoll()
    {
        if (TextJobPolls.TryDequeue(out object next)) _lastTextPoll = next;
        return _lastTextPoll;
    }

    // HttpClientFactory 會定期回收 handler；這個假服務整個測試共用，不要被它關掉
    protected override void Dispose(bool disposing) { }
}
