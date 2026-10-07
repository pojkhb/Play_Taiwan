// API 文件與登入驗證
using System.Net;
using System.Net.Http.Headers;
using backend.Models;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("帳號與身分驗證", "API 整合測試")]
[AllureBddHierarchy("帳號與身分驗證", "API 整合測試")]
public class AuthApiTests
{
    private readonly ApiFactory _api;

    public AuthApiTests(ApiFactory api) => _api = api;

    [Fact]
    public async Task Swagger文件可以正常產生()
    {
        HttpResponseMessage response = await _api.ClientFor(null).GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/Badge/Draw", json);
        Assert.Contains("/api/Story/GenerateGameStory", json);
        Assert.Contains("/api/VisitorVlog/Preview", json);
        Assert.Contains("/api/MerchantVlog/Preview", json);
    }

    [Fact]
    public async Task 沒帶Token呼叫需要登入的API回傳401()
    {
        HttpResponseMessage response = await _api.ClientFor(null).GetAsync("/api/Home/Overview");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token過期回傳401()
    {
        HttpClient client = _api.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiFactory.Token(_api.NewUserId(), expires: DateTime.UtcNow.AddMinutes(-1)));

        HttpResponseMessage response = await client.GetAsync("/api/Home/Overview");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 用別的金鑰簽的Token回傳401()
    {
        HttpClient client = _api.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiFactory.Token(_api.NewUserId(), secret: "Somebody_Else_Secret_That_Is_Long_Enough_2026"));

        HttpResponseMessage response = await client.GetAsync("/api/Home/Overview");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 帶正確Token可以查首頁總覽_新玩家全部是0()
    {
        HttpResponseMessage response = await _api.ClientFor(_api.NewUserId()).GetAsync("/api/Home/Overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        HomeOverviewResponse overview = (await response.ReadResultAsync<HomeOverviewResponse>()).Result;
        Assert.Equal(0, overview.ho_completed_story);
        Assert.Equal(0, overview.ho_badge_count);
        Assert.Equal(0, overview.ho_postcard_count);
        Assert.Equal(0, overview.ho_vlog_count);
    }
}
