// 加入 Google 行事曆：連結格式、沒指定時間時的預設出發時間
using backend.Services;
using backend.util;
using Microsoft.AspNetCore.WebUtilities;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Story;

[AllureSuiteHierarchy("劇本", "單元測試")]
[AllureBddHierarchy("劇本", "單元測試")]
public class CalendarTests
{
    [Fact]
    public void Google行事曆連結帶入標題_台灣時間_說明與地點()
    {
        string url = GoogleCalendarLink.Build("Play Taiwan｜州廳鐘聲裡的家書",
            new DateTime(2026, 10, 4, 10, 0, 0), new DateTime(2026, 10, 4, 14, 30, 0), "第一行\n第二行", "臺中州廳 臺中市西區民權路99號");

        Assert.StartsWith("https://calendar.google.com/calendar/render?", url);
        var query = QueryHelpers.ParseQuery(new Uri(url).Query);
        Assert.Equal("TEMPLATE", query["action"]);
        Assert.Equal("Play Taiwan｜州廳鐘聲裡的家書", query["text"]);
        Assert.Equal("20261004T100000/20261004T143000", query["dates"]);
        Assert.Equal("Asia/Taipei", query["ctz"]);
        Assert.Equal("第一行\n第二行", query["details"]);
        Assert.Equal("臺中州廳 臺中市西區民權路99號", query["location"]);
    }

    [Theory]
    [InlineData(false, "2026-10-01T15:20:00", "2026-10-02T10:00:00")]   // 白天劇本：明天早上 10 點
    [InlineData(true, "2026-10-01T15:20:00", "2026-10-02T19:00:00")]    // 夜間劇本：明天晚上 7 點
    [InlineData(false, "2026-12-31T23:50:00", "2027-01-01T10:00:00")]   // 跨年
    public void 沒指定時間時預設明天出發(bool night, string now, string expected)
    {
        Assert.Equal(DateTime.Parse(expected), StoryService.DefaultCalendarStart(night, DateTime.Parse(now)));
    }
}
