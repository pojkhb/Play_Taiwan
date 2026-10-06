// 把劇本加入 Google 行事曆：POST /api/Story/{story_id}/Calendar
using System.Net;
using System.Net.Http.Json;
using backend.ViewModels;
using Microsoft.AspNetCore.WebUtilities;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("劇本", "API 整合測試")]
[AllureBddHierarchy("劇本", "API 整合測試")]
public class StoryCalendarApiTests
{
    private readonly ApiFactory _api;

    public StoryCalendarApiTests(ApiFactory api) => _api = api;

    [Fact]
    public async Task 劇本加入行事曆_只放第一站當集合點_其他站不會出現()
    {
        int user = _api.NewUserId();
        string[] places = Enumerable.Range(1, 3).Select(i => $"行事曆景點{i}_{Guid.NewGuid():N}"[..14]).ToArray();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, places);
        // 測試工具建的劇本標題會列出所有景點，改成真實劇本的標題
        await _api.ExecuteAsync("UPDATE story SET story_title = '州廳鐘聲裡的家書' WHERE s_id = @storyId;", new { storyId });
        await _api.ExecuteAsync("UPDATE place SET p_address = '臺北市中正區襄陽路2號' WHERE p_name = @name;", new { name = places[0] });

        HttpResponseMessage response = await _api.ClientFor(user).PostAsJsonAsync($"/api/Story/{storyId}/Calendar",
            new { start_time = "2026-10-04T10:00:00", duration_minutes = 240 });
        StoryCalendarResponse result = (await response.ReadResultAsync<StoryCalendarResponse>()).Result;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new DateTime(2026, 10, 4, 10, 0, 0), result.start_time);
        Assert.Equal(new DateTime(2026, 10, 4, 14, 0, 0), result.end_time);
        Assert.Equal($"{places[0]} 臺北市中正區襄陽路2號", result.location);
        Assert.Contains("劇本簡介", result.details);
        Assert.Contains("共 3 站・白天劇本", result.details);

        var query = QueryHelpers.ParseQuery(new Uri(result.google_calendar_url).Query);
        Assert.Equal("TEMPLATE", query["action"]);
        Assert.Equal("Play Taiwan｜州廳鐘聲裡的家書", query["text"]);
        Assert.Equal("20261004T100000/20261004T140000", query["dates"]);
        Assert.Equal(result.location, query["location"]);

        // 第 2、3 站還在迷霧中，不能出現在行事曆
        foreach (string hidden in places.Skip(1))
        {
            Assert.DoesNotContain(hidden, result.details);
            Assert.DoesNotContain(hidden, query["text"].ToString());
        }
    }

    [Fact]
    public async Task 沒帶時間_夜間劇本預設明天晚上七點_依站數估時間()
    {
        int user = _api.NewUserId();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: true, $"夜景{Guid.NewGuid():N}"[..12], $"夜市{Guid.NewGuid():N}"[..12]);

        HttpResponseMessage response = await _api.ClientFor(user).PostAsync($"/api/Story/{storyId}/Calendar", null);
        StoryCalendarResponse result = (await response.ReadResultAsync<StoryCalendarResponse>()).Result;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DateTime tomorrowNight = DateTime.UtcNow.AddHours(8).Date.AddDays(1).AddHours(19);
        Assert.Equal(tomorrowNight, result.start_time);
        Assert.Equal(tomorrowNight.AddMinutes(80), result.end_time);   // 2 站 × 40 分鐘
        Assert.Contains("夜間劇本", result.details);
    }

    [Fact]
    public async Task 找不到劇本回傳404_遊玩時間不合理回傳400()
    {
        HttpClient client = _api.ClientFor(_api.NewUserId());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/Story/999999/Calendar", null)).StatusCode);

        int storyId = await _api.CreateStoryAsync(_api.NewUserId(), "臺北市", night: false, $"景點{Guid.NewGuid():N}"[..12]);
        HttpResponseMessage tooLong = await client.PostAsJsonAsync($"/api/Story/{storyId}/Calendar", new { duration_minutes = 5000 });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task 沒登入不能加入行事曆()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.ClientFor(null).PostAsync("/api/Story/1/Calendar", null)).StatusCode);
    }
}
