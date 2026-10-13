// 明信片：查詢、圖片的顯示網址與下載網址（圖片由後端自己回傳，不是 AI 服務的檔案連結）、生成、
// ibon 列印（外部列印服務由 FakeAiService 模擬）、分享、刪除
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using backend.utils;
using Microsoft.AspNetCore.Mvc.Testing;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("明信片與上傳", "API 整合測試")]
[AllureBddHierarchy("明信片與上傳", "API 整合測試")]
public class PostcardCatalogApiTests
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

    private readonly ApiFactory _api;

    public PostcardCatalogApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
    }

    /// <summary>
    /// 建立一張屬於 owner 的明信片，回傳 (p_id, AI 服務的檔案連結, 劇本)。
    /// 圖片內容由 FakeAi 提供；content 傳 null 代表 AI 服務那邊拿不到這張圖
    /// </summary>
    private async Task<(int id, string sourceUrl, int storyId)> CreatePostcardAsync(int owner, byte[] content, string name = "測試明信片")
    {
        int storyId = await _api.CreateStoryAsync(owner, "臺北市", night: false, $"明信片景點{Guid.NewGuid():N}");
        string sourceUrl = $"https://img.test/api/download/postcard-{Guid.NewGuid():N}.png";
        if (content != null) _api.FakeAi.Images[sourceUrl] = content;
        await _api.ExecuteAsync(@"
            INSERT INTO postcard (au_id, s_id, p_name, p_summary, p_imag_url, is_night)
            VALUES (@owner, @storyId, @name, '測試用', @sourceUrl, 0);", new { owner, storyId, name, sourceUrl });
        int id = await _api.QueryAsync<int>("SELECT MAX(p_id) FROM postcard WHERE au_id = @owner;", new { owner });
        return (id, sourceUrl, storyId);
    }

    private Task<(int id, string sourceUrl, int storyId)> CreatePostcardAsync(int owner) => CreatePostcardAsync(owner, Png);

    private static string DisplayUrl(int id) => $"http://localhost/api/PostcardCatalog/{id}/image";
    private static string DownloadUrl(int id) => $"http://localhost/api/PostcardCatalog/{id}/download";

    /// <summary>後端向 AI 服務拿這張圖拿了幾次</summary>
    private int Fetches(string sourceUrl) => _api.FakeAi.Requests.Count(r => r.Path == new Uri(sourceUrl).AbsolutePath);

    /// <summary>圖片抓回後端之後存放的檔案</summary>
    private string StoredFile(int id) => Path.Combine(_api.FogRoot, "postcards", $"{id}.img");

    [Fact]
    public async Task 查詢明信片_單張_清單_依劇本都給顯示網址與下載網址_不給AI服務的檔案連結()
    {
        int owner = _api.NewUserId();
        var (id, sourceUrl, storyId) = await CreatePostcardAsync(owner);
        HttpClient client = _api.ClientFor(owner);

        HttpResponseMessage single = await client.GetAsync($"/api/PostcardCatalog/{id}");
        var result = await single.ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal("測試明信片", result.Result.GetProperty("PostcardName").GetString());
        Assert.Equal(DisplayUrl(id), result.Result.GetProperty("ImageUrl").GetString());
        Assert.Equal(DownloadUrl(id), result.Result.GetProperty("DownloadUrl").GetString());

        foreach (string path in new[] { "/api/PostcardCatalog", $"/api/PostcardCatalog/by-story/{storyId}" })
        {
            HttpResponseMessage response = await client.GetAsync(path);
            JsonElement card = Assert.Single((await response.ReadResultAsync()).Result.EnumerateArray());
            Assert.Equal(DisplayUrl(id), card.GetProperty("ImageUrl").GetString());
            Assert.Equal(DownloadUrl(id), card.GetProperty("DownloadUrl").GetString());
            Assert.DoesNotContain(sourceUrl, await response.Content.ReadAsStringAsync());
        }

        Assert.DoesNotContain(sourceUrl, await single.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 查詢不存在的明信片回傳404()
    {
        HttpResponseMessage response = await _api.ClientFor(_api.NewUserId()).GetAsync("/api/PostcardCatalog/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 顯示網址直接回傳圖片_不轉址也不是下載_圖片抓回後端後不用再問AI服務()
    {
        var (id, sourceUrl, _) = await CreatePostcardAsync(_api.NewUserId());
        HttpClient client = _api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });   // 不用登入

        HttpResponseMessage response = await client.GetAsync($"/api/PostcardCatalog/{id}/image");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType.MediaType);
        Assert.Null(response.Content.Headers.ContentDisposition);   // 沒有 attachment：打開就是顯示
        Assert.Equal(Png, await response.Content.ReadAsByteArrayAsync());
        Assert.True(response.Headers.CacheControl.Public);
        Assert.True(File.Exists(StoredFile(id)));

        // AI 服務把檔案清掉（或連不上）之後照樣顯示，而且沒有再去問它
        _api.FakeAi.Images.TryRemove(sourceUrl, out _);
        HttpResponseMessage again = await client.GetAsync($"/api/PostcardCatalog/{id}/image");

        Assert.Equal(Png, await again.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, Fetches(sourceUrl));
    }

    [Fact]
    public async Task 下載網址是同一張圖_存成檔案_檔名是明信片名稱()
    {
        var (id, _, _) = await CreatePostcardAsync(_api.NewUserId(), Png, name: "台北101 專屬明信片");

        HttpResponseMessage response = await _api.ClientFor(null).GetAsync($"/api/PostcardCatalog/{id}/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType.MediaType);
        ContentDispositionHeaderValue disposition = response.Content.Headers.ContentDisposition;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Equal("台北101 專屬明信片.png", disposition.FileNameStar);
        Assert.Equal(Png, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task 圖片拿不到或不是圖片回502_沒有這張明信片回404()
    {
        HttpClient client = _api.ClientFor(null);

        var (missing, _, _) = await CreatePostcardAsync(_api.NewUserId(), content: null);   // AI 服務那邊沒有這個檔案
        Assert.Equal(HttpStatusCode.BadGateway, (await client.GetAsync($"/api/PostcardCatalog/{missing}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, (await client.GetAsync($"/api/PostcardCatalog/{missing}/download")).StatusCode);

        var (notImage, _, _) = await CreatePostcardAsync(_api.NewUserId(), "<html>錯誤頁</html>"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadGateway, (await client.GetAsync($"/api/PostcardCatalog/{notImage}/image")).StatusCode);
        Assert.False(File.Exists(StoredFile(notImage)));   // 不是圖片的內容不會存起來

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/PostcardCatalog/999999/image")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/PostcardCatalog/999999/download")).StatusCode);
    }

    [Fact]
    public async Task 生成明信片_回傳顯示網址與下載網址_圖片馬上抓回後端()
    {
        int owner = _api.NewUserId();
        HttpClient client = _api.ClientFor(owner);
        int storyId = await _api.CreateStoryAsync(owner, "臺北市", night: false, $"明信片景點{Guid.NewGuid():N}");

        // AI 服務回的檔案連結是相對路徑（跟 NPC 語音、Vlog 一樣），要補上 AI 服務的根網址
        string file = $"/api/download/postcard_{Guid.NewGuid():N}.png";
        string sourceUrl = AiServiceConfig.Url(file);
        _api.FakeAi.Images[sourceUrl] = Png;
        _api.FakeAi.PostcardResponse = new { status = "success", task_id = "d1863bc6", postcard_introduction = "晨曦的秘密花園", download_url = file };

        using var form = new MultipartFormDataContent();
        var photo = new ByteArrayContent(ApiFactory.TinyPng);
        photo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(photo, "user_image", "taipei101.png");
        form.Add(new StringContent("台北101"), "spot_name");
        form.Add(new StringContent("復古水墨風"), "user_prompt");
        form.Add(new StringContent(storyId.ToString()), "story_id");

        var result = await (await client.PostAsync("/api/PostcardCatalog/GenerateAi", form)).ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        int id = result.Result.GetProperty("p_id").GetInt32();
        Assert.Equal("台北101 專屬明信片", result.Result.GetProperty("p_name").GetString());
        Assert.Equal("晨曦的秘密花園", result.Result.GetProperty("p_summary").GetString());
        Assert.Equal(DisplayUrl(id), result.Result.GetProperty("p_imag_url").GetString());
        Assert.Equal(DownloadUrl(id), result.Result.GetProperty("download_url").GetString());

        // 資料庫存的是 AI 服務的完整檔案連結；圖片已經在後端，AI 服務連不上也顯示得出來
        Assert.Equal(sourceUrl, await _api.QueryAsync<string>("SELECT p_imag_url FROM postcard WHERE p_id = @id;", new { id }));
        Assert.True(File.Exists(StoredFile(id)));
        _api.FakeAi.Images.TryRemove(sourceUrl, out _);
        Assert.Equal(Png, await (await client.GetAsync($"/api/PostcardCatalog/{id}/image")).Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task 首頁卡片的明信片圖片也是顯示網址()
    {
        int owner = _api.NewUserId();
        var (id, sourceUrl, _) = await CreatePostcardAsync(owner);

        HttpResponseMessage response = await _api.ClientFor(owner).GetAsync("/api/Home/Overview");
        JsonElement cards = (await response.ReadResultAsync()).Result.GetProperty("ho_recent_cards");

        JsonElement card = Assert.Single(cards.EnumerateArray(), c => c.GetProperty("hc_type").GetString() == "postcard");
        Assert.Equal(id, card.GetProperty("hc_id").GetInt32());
        Assert.Equal(DisplayUrl(id), card.GetProperty("hc_image").GetString());
        Assert.DoesNotContain(sourceUrl, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 送到ibon列印拿到取件碼()
    {
        int owner = _api.NewUserId();
        var (id, _, _) = await CreatePostcardAsync(owner);

        var result = await (await _api.ClientFor(owner).PostAsJsonAsync("/api/PostcardCatalog/Print", new { postcard_id = id })).ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal("PT123456", result.Result.GetProperty("ibon_pickup_code").GetString());
        Assert.Equal(Png, _api.FakeAi.Last("/upload").Files["file"]);   // 送到 ibon 列印服務的就是顯示的那張圖
    }

    [Fact]
    public async Task 分享到社群()
    {
        var result = await (await _api.ClientFor(_api.NewUserId())
            .PostAsJsonAsync("/api/PostcardCatalog/Share", new { story_id = 1, platform = "Instagram" })).ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        Assert.Contains("Instagram", result.message);
    }

    [Fact]
    public async Task 只能刪除自己的明信片_存在後端的圖片一起刪掉()
    {
        int owner = _api.NewUserId(), stranger = _api.NewUserId();
        var (id, _, _) = await CreatePostcardAsync(owner);
        await _api.ClientFor(null).GetAsync($"/api/PostcardCatalog/{id}/image");   // 看過一次，圖片存在後端了
        Assert.True(File.Exists(StoredFile(id)));

        var byStranger = await (await _api.ClientFor(stranger).PostAsync($"/api/PostcardCatalog/{id}/Delete", null)).ReadResultAsync();
        Assert.False(byStranger.isSuccess);
        Assert.True(File.Exists(StoredFile(id)));

        var byOwner = await (await _api.ClientFor(owner).PostAsync($"/api/PostcardCatalog/{id}/Delete", null)).ReadResultAsync();
        Assert.True(byOwner.isSuccess, byOwner.message);
        Assert.Equal(0, await _api.QueryAsync<int>("SELECT COUNT(*) FROM postcard WHERE p_id = @id;", new { id }));
        Assert.False(File.Exists(StoredFile(id)));
    }
}
