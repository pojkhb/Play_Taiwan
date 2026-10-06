// 自選地點的交通方式可用性（POST /api/Route/Availability）
using System.Net.Http.Json;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("交通路網", "API 整合測試")]
[AllureBddHierarchy("交通路網", "API 整合測試")]
public class RouteAvailabilityApiTests
{
    private readonly ApiFactory _api;

    public RouteAvailabilityApiTests(ApiFactory api) => _api = api;

    [Fact]
    public async Task 自選地點查詢交通方式可用性()
    {
        var points = new[]
        {
            new { name = "臺博館", lat = 25.0428, lng = 121.5150 },
            new { name = "中正紀念堂", lat = 25.0346, lng = 121.5218 },
        };

        var result = await (await _api.ClientFor(_api.NewUserId()).PostAsJsonAsync("/api/Route/Availability", points)).ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        var modes = result.Result.EnumerateArray().Select(m => m.GetProperty("mode").GetString()).ToList();
        Assert.Contains("步行", modes);
        Assert.True(result.Result.EnumerateArray().First(m => m.GetProperty("mode").GetString() == "步行").GetProperty("enabled").GetBoolean());
    }
}
