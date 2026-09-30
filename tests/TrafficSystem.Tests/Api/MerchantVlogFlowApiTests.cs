// 商家 VLOG 完整流程（AI 服務用 FakeAiService 模擬）：
// 輸入店家名稱、營業時間、地址、敘事語氣、推廣資訊、多張照片 → 旁白草稿、推薦配文、TAG → 送出合成 → 影片網址
using System.IO.Compression;
using System.Net.Http.Json;
using backend.utils;
using backend.ViewModels;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
public class MerchantVlogFlowApiTests : IDisposable
{
    private readonly ApiFactory _api;
    private readonly List<int> _projects = new();

    public MerchantVlogFlowApiTests(ApiFactory api)
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

    private static MultipartFormDataContent Form(int imageCount, int? mmId = null, int? ntId = null,
                                                 string fileName = "photo.jpg", string contentType = "image/jpeg")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("阿嬤的芒果冰"), "store_name" },
            { new StringContent("週二至週日 11:00–21:00"), "open_time" },
            { new StringContent("臺中市中區中山路20號"), "address" },
            { new StringContent("夏季限定芒果冰買一送一"), "promo_text" },
        };
        if (mmId.HasValue) form.Add(new StringContent(mmId.ToString()), "mm_id");
        if (ntId.HasValue) form.Add(new StringContent(ntId.ToString()), "nt_id");

        for (int i = 0; i < imageCount; i++)
        {
            var image = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, (byte)i });
            image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            form.Add(image, "images", fileName);
        }
        return form;
    }

    private async Task<ApiResult<MerchantVlogPreviewResponse>> PreviewAsync(HttpClient client, MultipartFormDataContent form)
    {
        var result = await (await client.PostAsync("/api/MerchantVlog/Preview", form)).ReadResultAsync<MerchantVlogPreviewResponse>();
        if (result.Result != null) _projects.Add(result.Result.mm_id);
        return result;
    }

    private static async Task<MerchantVlogStatusResponse> StatusAsync(HttpClient client, int mmId) =>
        (await (await client.GetAsync($"/api/MerchantVlog/Status/{mmId}")).ReadResultAsync<MerchantVlogStatusResponse>()).Result;

    [Fact]
    public async Task 完整流程_店家資訊與照片_拿到旁白配文TAG_送出合成_取得影片()
    {
        HttpClient client = _api.MerchantClientFor(_api.NewUserId());
        List<NarrativeToneItem> tones = (await (await client.GetAsync("/api/MerchantVlog/Tones")).ReadResultAsync<List<NarrativeToneItem>>()).Result;
        NarrativeToneItem warm = tones.Single(t => t.nt_name == "溫情走心");

        // ── 1. 輸入資訊 + 3 張照片 → 旁白草稿、推薦配文、TAG ──
        ApiResult<MerchantVlogPreviewResponse> preview = await PreviewAsync(client, Form(imageCount: 3, ntId: warm.nt_id));

        Assert.True(preview.isSuccess, preview.message);
        MerchantVlogPreviewResponse draft = preview.Result;
        Assert.Equal("阿嬤的芒果冰", draft.store_name);
        Assert.Equal("週二至週日 11:00–21:00", draft.open_time);
        Assert.Equal("臺中市中區中山路20號", draft.address);
        Assert.Equal("溫情走心", draft.tone_name);
        Assert.Equal("夏天就是要來一碗阿嬤的芒果冰！", draft.script);           // 旁白草稿
        Assert.Equal("限定芒果冰買一送一，快揪朋友來！", draft.caption);         // 推薦配文
        Assert.Equal(new[] { "#台中美食", "#芒果冰" }, draft.hashtags);          // TAG：補 #、去空白、去重複
        Assert.Equal(3, draft.image_urls.Count);

        // 送給 AI 的內容：店名 + 推廣資訊（併入營業時間、地址、敘事語氣）
        FakeAiService.Received sentPreview = _api.FakeAi.Last("/api/merchant/vlog/preview");
        Assert.Equal("阿嬤的芒果冰", sentPreview.Fields["merchant_name"]);
        string focus = sentPreview.Fields["promo_focus"];
        Assert.Contains("夏季限定芒果冰買一送一", focus);
        Assert.Contains("營業時間：週二至週日 11:00–21:00", focus);
        Assert.Contains("地址：臺中市中區中山路20號", focus);
        Assert.Contains("敘事語氣：溫情走心", focus);

        // ── 2. 商家確認旁白（改了 TAG）→ 送出合成 ──
        HttpResponseMessage final = await client.PostAsJsonAsync("/api/MerchantVlog/CreateFinal", new
        {
            mm_id = draft.mm_id,
            final_script = "商家確認後的旁白",
            hashtags = new[] { "#芒果冰", " 夏日 " }
        });
        MerchantVlogTaskResponse task = (await final.ReadResultAsync<MerchantVlogTaskResponse>()).Result;
        Assert.Equal(FakeAiService.TaskId, task.task_id);
        Assert.Equal(2, task.status);

        // 送給 AI 的合成資料：最終旁白 + 3 張照片依順序打包成 zip
        FakeAiService.Received sentFinal = _api.FakeAi.Last("/api/visitor/vlog/create_final");
        Assert.Equal("商家確認後的旁白", sentFinal.Fields["final_script"]);
        Assert.True(sentFinal.Files["bgm_file"].Length > 0);   // AI 規定背景音樂必填
        using (var zip = new ZipArchive(new MemoryStream(sentFinal.Files["image_zip"])))
        {
            Assert.Equal(new[] { "01.jpg", "02.jpg", "03.jpg" }, zip.Entries.Select(e => e.FullName));
        }

        // ── 3. 合成中查進度 ──
        MerchantVlogStatusResponse processing = await StatusAsync(client, draft.mm_id);
        Assert.Equal(2, processing.status);
        Assert.Null(processing.video_url);

        // ── 4. AI 完成 → 拿到影片、推薦配文、TAG ──
        _api.FakeAi.CheckStatusResponse = new { status = "completed", task_id = FakeAiService.TaskId, download_url = "/download/fake-task-001.mp4" };
        MerchantVlogStatusResponse done = await StatusAsync(client, draft.mm_id);

        Assert.Equal(3, done.status);
        Assert.Equal("已完成", done.status_text);
        Assert.Equal(AiServiceConfig.Url("/download/fake-task-001.mp4"), done.video_url);   // 影片
        Assert.Equal("限定芒果冰買一送一，快揪朋友來！", done.caption);                      // 推薦配文
        Assert.Equal(new[] { "#芒果冰", "#夏日" }, done.hashtags);                           // TAG（商家改過的）
    }

    [Fact]
    public async Task 換語氣重新產生草稿時不帶照片會沿用原本的照片()
    {
        HttpClient client = _api.MerchantClientFor(_api.NewUserId());
        MerchantVlogPreviewResponse first = (await PreviewAsync(client, Form(imageCount: 2))).Result;

        MerchantVlogPreviewResponse again = (await PreviewAsync(client, Form(imageCount: 0, mmId: first.mm_id))).Result;

        Assert.Equal(first.mm_id, again.mm_id);
        Assert.Equal(first.image_urls, again.image_urls);
    }

    [Fact]
    public async Task 影片合成中不能再修改草稿()
    {
        HttpClient client = _api.MerchantClientFor(_api.NewUserId());
        MerchantVlogPreviewResponse draft = (await PreviewAsync(client, Form(imageCount: 1))).Result;
        await client.PostAsJsonAsync("/api/MerchantVlog/CreateFinal", new { mm_id = draft.mm_id, final_script = "旁白" });

        ApiResult<MerchantVlogPreviewResponse> edit = await PreviewAsync(client, Form(imageCount: 0, mmId: draft.mm_id));

        Assert.False(edit.isSuccess);
        Assert.Equal("影片合成中，暫時不能修改", edit.message);
    }

    [Fact]
    public async Task AI合成失敗時記錄失敗原因()
    {
        HttpClient client = _api.MerchantClientFor(_api.NewUserId());
        MerchantVlogPreviewResponse draft = (await PreviewAsync(client, Form(imageCount: 1))).Result;
        await client.PostAsJsonAsync("/api/MerchantVlog/CreateFinal", new { mm_id = draft.mm_id, final_script = "旁白" });

        _api.FakeAi.CheckStatusResponse = new { status = "failed", task_id = FakeAiService.TaskId, message = "照片解析度太低" };
        MerchantVlogStatusResponse status = await StatusAsync(client, draft.mm_id);

        Assert.Equal(4, status.status);
        Assert.Equal("AI 影片合成失敗：照片解析度太低", status.error_message);
    }

    [Fact]
    public async Task 別的商家看不到這個影音專案()
    {
        MerchantVlogPreviewResponse draft = (await PreviewAsync(_api.MerchantClientFor(_api.NewUserId()), Form(imageCount: 1))).Result;

        ApiResult<MerchantVlogStatusResponse> other =
            await (await _api.MerchantClientFor(_api.NewUserId()).GetAsync($"/api/MerchantVlog/Status/{draft.mm_id}")).ReadResultAsync<MerchantVlogStatusResponse>();

        Assert.False(other.isSuccess);
        Assert.Equal("找不到此影音專案", other.message);
    }

    [Theory]
    [InlineData(0, "photo.jpg", "image/jpeg", "請至少上傳一張照片")]
    [InlineData(1, "clip.mp4", "video/mp4", "不是照片")]
    [InlineData(31, "photo.jpg", "image/jpeg", "照片最多 30 張")]
    public async Task 照片不符合規定時擋下(int count, string fileName, string contentType, string expected)
    {
        ApiResult<MerchantVlogPreviewResponse> result =
            await PreviewAsync(_api.MerchantClientFor(_api.NewUserId()), Form(count, fileName: fileName, contentType: contentType));

        Assert.False(result.isSuccess);
        Assert.Contains(expected, result.message);
        Assert.Empty(_api.FakeAi.Requests);   // 檢查沒過就不會呼叫 AI
    }
}
