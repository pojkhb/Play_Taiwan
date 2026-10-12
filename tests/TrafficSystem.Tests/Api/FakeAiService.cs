// 模擬外部 AI 服務（aiservice.angelalala.com）：
// 攔下後端所有往外的 HTTP 請求，依路徑回應固定內容，並記下收到的表單欄位與檔案，讓測試檢查後端送了什麼。
// 路徑與欄位格式跟 VlogAiGateway / MerchantVlogService 呼叫的正式端點相同。
using System.Collections.Concurrent;
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

    /// <summary>check_status 目前要回的內容，測試可以改</summary>
    public object CheckStatusResponse { get; set; } = new { status = "processing", task_id = TaskId };

    public const string TaskId = "fake-task-001";

    /// <summary>「遊你說了算」背景工作代號</summary>
    public const string TextJobId = "fake-text-job";

    /// <summary>遊你說了算：每次查詢進度依序回傳的內容；用完後一直回最後一個</summary>
    public ConcurrentQueue<object> TextJobPolls { get; } = new();
    private object _lastTextPoll = new { job_id = TextJobId, status = "processing", stage = "排隊中" };

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

    /// <summary>AI Agent 找不到地點時的回應：HTTP 200，只有 message</summary>
    public static readonly object AgentNoPlace = new { message = "附近沒有符合您當下心情的地點。" };

    /// <summary>/api/agent/orchestrate（spin）要回的內容</summary>
    public object AgentOrchestrateResponse { get; set; } = AgentNoPlace;

    public void Reset()
    {
        Requests.Clear();
        TextJobPolls.Clear();
        _lastTextPoll = new { job_id = TextJobId, status = "processing", stage = "排隊中" };
        CheckStatusResponse = new { status = "processing", task_id = TaskId };
        AgentOrchestrateResponse = AgentNoPlace;
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

        object body = path switch
        {
            "/api/merchant/vlog/preview" => MerchantPreviewResponse,
            "/api/visitor/vlog/create_final" => new { status = "processing", task_id = TaskId, message = "已排入佇列" },
            _ when path == "/api/check_status/" + TaskId => CheckStatusResponse,
            "/upload" => new { pincode = "PT123456", qrcode_base64 = "iVBORw0KGgo=" },   // ibon 列印服務
            "/api/npc/speak" => new { status = "success", task_id = "npc-001", download_url = "/api/download/npc_npc-001.mp3" },
            "/api/api/admin/generate_script_blueprint_by_text" => new { status = "processing", job_id = TextJobId, check_url = "fake" },
            _ when path == "/api/api/admin/generate_script_blueprint_by_text/" + TextJobId => NextTextPoll(),
            "/api/agent/orchestrate" => AgentOrchestrateResponse,
            "/search" => new[] { new { lat = "25.0324", lon = "121.5199" } },   // Nominatim：地名 → 座標
            _ => null
        };

        if (body == null)
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("fake ai: no route " + path) };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
    }

    private object NextTextPoll()
    {
        if (TextJobPolls.TryDequeue(out object next)) _lastTextPoll = next;
        return _lastTextPoll;
    }

    // HttpClientFactory 會定期回收 handler；這個假服務整個測試共用，不要被它關掉
    protected override void Dispose(bool disposing) { }
}
