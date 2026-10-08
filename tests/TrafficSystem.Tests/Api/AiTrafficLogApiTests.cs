// 呼叫 AI Service 的 request / response 紀錄（AiTrafficLogHandler）：每次呼叫一個 JSON 檔，檔案只記類型與大小
using System.Net.Http.Json;
using System.Text.Json;
using backend.utils;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("劇本", "API 整合測試")]
[AllureBddHierarchy("劇本", "API 整合測試")]
public class AiTrafficLogApiTests : IDisposable
{
    private readonly ApiFactory _api;
    private readonly List<int> _projects = new();

    public AiTrafficLogApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
    }

    /// <summary>刪掉測試上傳到 wwwroot/uploads/merchant_vlog 的照片</summary>
    public void Dispose()
    {
        foreach (int mmId in _projects)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "merchant_vlog", mmId.ToString());
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>所有 AI 呼叫紀錄（已解析成 JSON）</summary>
    private List<JsonElement> AllLogs() =>
        Directory.Exists(_api.AiLogRoot)
            ? Directory.GetFiles(_api.AiLogRoot, "*.json", SearchOption.AllDirectories)
                .Select(f => JsonDocument.Parse(File.ReadAllText(f)).RootElement.Clone())
                .ToList()
            : new List<JsonElement>();

    /// <summary>內容含有 marker 的那一筆紀錄（紀錄檔的中文沒有跳脫，直接比對原文）</summary>
    private JsonElement LogContaining(string marker) =>
        AllLogs().Single(log => log.GetRawText().Contains(marker));

    [Fact]
    public async Task 呼叫AI會記下request與response()
    {
        string text = $"紀錄測試{Guid.NewGuid():N}";

        var result = await (await _api.ClientFor(_api.NewUserId()).PostAsJsonAsync("/api/Npc/Speak", new { text })).ReadResultAsync<NpcSpeakResponse>();
        Assert.True(result.isSuccess, result.message);

        JsonElement log = LogContaining(text);
        Assert.Equal("POST", log.GetProperty("method").GetString());
        Assert.Equal(AiServiceConfig.Url("/api/npc/speak"), log.GetProperty("url").GetString());
        Assert.True(log.GetProperty("duration_ms").GetInt64() >= 0);

        // NPC 語音送的是 form-urlencoded，紀錄裡解碼成欄位
        Assert.Equal(text, log.GetProperty("request").GetProperty("fields").GetProperty("text").GetString());

        JsonElement response = log.GetProperty("response");
        Assert.Equal(200, response.GetProperty("status").GetInt32());
        Assert.Equal("/api/download/npc_npc-001.mp3", response.GetProperty("body").GetProperty("download_url").GetString());   // JSON 存成 JSON，不是字串

        // 只記錄 AI Service 的網址（Nominatim、ibon 等其他外部服務不記）
        Assert.All(AllLogs(), l => Assert.StartsWith(AiServiceConfig.BaseUrl, l.GetProperty("url").GetString()));
    }

    [Fact]
    public async Task 送給AI的檔案只記檔名與大小_AI照樣收到完整檔案()
    {
        HttpClient client = _api.MerchantClientFor(_api.NewUserId());
        var form = new MultipartFormDataContent
        {
            { new StringContent("紀錄測試小吃"), "store_name" },
            { new StringContent("招牌滷肉飯"), "promo_text" },
        };
        var image = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 7 });
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(image, "images", "photo.jpg");
        var preview = await (await client.PostAsync("/api/MerchantVlog/Preview", form)).ReadResultAsync<MerchantVlogPreviewResponse>();
        Assert.True(preview.isSuccess, preview.message);
        _projects.Add(preview.Result.mm_id);

        // 送出合成：照片打包成 image_zip、加上背景音樂 bgm_file，一起用 multipart 送給 AI
        string finalScript = $"紀錄測試旁白{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/MerchantVlog/CreateFinal", new { mm_id = preview.Result.mm_id, final_script = finalScript });

        JsonElement log = LogContaining(finalScript);
        Assert.Equal(AiServiceConfig.Url("/api/visitor/vlog/create_final"), log.GetProperty("url").GetString());
        Dictionary<string, JsonElement> parts = log.GetProperty("request").GetProperty("parts").EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString());
        Assert.Equal(finalScript, parts["final_script"].GetProperty("value").GetString());
        foreach (string file in new[] { "image_zip", "bgm_file" })
        {
            Assert.True(parts[file].GetProperty("bytes").GetInt32() > 0);
            Assert.False(parts[file].TryGetProperty("value", out _));   // 不記檔案內容
        }

        // 記錄時讀過一次檔案，AI 還是收到完整的照片壓縮檔
        FakeAiService.Received sent = _api.FakeAi.Last("/api/visitor/vlog/create_final");
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(sent.Files["image_zip"]));
        Assert.Single(zip.Entries);
    }
}
