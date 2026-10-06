// 明信片：查詢單張、圖片轉址、ibon 列印（外部列印服務由 FakeAiService 模擬）、分享、刪除
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("明信片與上傳", "API 整合測試")]
[AllureBddHierarchy("明信片與上傳", "API 整合測試")]
public class PostcardCatalogApiTests
{
    private readonly ApiFactory _api;

    public PostcardCatalogApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
    }

    /// <summary>建立一張屬於 owner 的明信片，圖片由 FakeAi 提供，回傳 (p_id, 圖片網址)</summary>
    private async Task<(int id, string imageUrl)> CreatePostcardAsync(int owner)
    {
        int storyId = await _api.CreateStoryAsync(owner, "臺北市", night: false, $"明信片景點{Guid.NewGuid():N}");
        string imageUrl = $"https://img.test/postcard-{Guid.NewGuid():N}.png";
        _api.FakeAi.Images[imageUrl] = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        await _api.ExecuteAsync(@"
            INSERT INTO postcard (au_id, s_id, p_name, p_summary, p_imag_url, is_night)
            VALUES (@owner, @storyId, '測試明信片', '測試用', @imageUrl, 2);", new { owner, storyId, imageUrl });
        int id = await _api.QueryAsync<int>("SELECT MAX(p_id) FROM postcard WHERE au_id = @owner;", new { owner });
        return (id, imageUrl);
    }

    [Fact]
    public async Task 查詢單張明信片()
    {
        int owner = _api.NewUserId();
        var (id, imageUrl) = await CreatePostcardAsync(owner);

        var result = await (await _api.ClientFor(owner).GetAsync($"/api/PostcardCatalog/{id}")).ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal("測試明信片", result.Result.GetProperty("PostcardName").GetString());
        Assert.Equal(imageUrl, result.Result.GetProperty("ImageUrl").GetString());
    }

    [Fact]
    public async Task 查詢不存在的明信片回傳404()
    {
        HttpResponseMessage response = await _api.ClientFor(_api.NewUserId()).GetAsync("/api/PostcardCatalog/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 明信片圖片網址會轉址到實際圖片()
    {
        int owner = _api.NewUserId();
        var (id, imageUrl) = await CreatePostcardAsync(owner);
        HttpClient client = _api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        HttpResponseMessage response = await client.GetAsync($"/api/PostcardCatalog/{id}/image");

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.Found });
        Assert.Equal(imageUrl, response.Headers.Location.ToString());
    }

    [Fact]
    public async Task 送到ibon列印拿到取件碼()
    {
        int owner = _api.NewUserId();
        var (id, _) = await CreatePostcardAsync(owner);

        var result = await (await _api.ClientFor(owner).PostAsJsonAsync("/api/PostcardCatalog/Print", new { postcard_id = id })).ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal("PT123456", result.Result.GetProperty("ibon_pickup_code").GetString());
        Assert.NotNull(_api.FakeAi.Last("/upload"));   // 圖片有送到 ibon 列印服務
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
    public async Task 只能刪除自己的明信片()
    {
        int owner = _api.NewUserId(), stranger = _api.NewUserId();
        var (id, _) = await CreatePostcardAsync(owner);

        var byStranger = await (await _api.ClientFor(stranger).PostAsync($"/api/PostcardCatalog/{id}/Delete", null)).ReadResultAsync();
        Assert.False(byStranger.isSuccess);

        var byOwner = await (await _api.ClientFor(owner).PostAsync($"/api/PostcardCatalog/{id}/Delete", null)).ReadResultAsync();
        Assert.True(byOwner.isSuccess, byOwner.message);
        Assert.Equal(0, await _api.QueryAsync<int>("SELECT COUNT(*) FROM postcard WHERE p_id = @id;", new { id }));
    }
}
