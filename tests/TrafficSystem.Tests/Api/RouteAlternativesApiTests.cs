// 路線的替代走法（POST /api/Route/Plan）：現在要走的那一段，同一種交通方式除了主路線還會多給幾條替代走法，
// 前端全部畫在地圖上讓使用者點選切換。Valhalla 由 FakeAi 模擬（跟真的一樣，請求有要替代路線才會回）。
using System.Net;
using System.Net.Http.Json;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("交通路網", "API 整合測試")]
[AllureBddHierarchy("交通路網", "API 整合測試")]
public class RouteAlternativesApiTests : IDisposable
{
    private readonly ApiFactory _api;

    public RouteAlternativesApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
        _api.FakeNeo4j.Reset();   // Neo4j 連不上，景點座標用 MySQL

        // 主路線 10 分鐘；兩條替代走法，其中一條還快了幾秒（真的 Valhalla 也會這樣）
        _api.FakeAi.ValhallaTrip = Trip(seconds: 600, km: 0.80, (25.0290, 121.5000), (25.0295, 121.5000), (25.0300, 121.5000));
        _api.FakeAi.ValhallaAlternates.Add(Trip(seconds: 594, km: 0.82, (25.0290, 121.5000), (25.0295, 121.5010), (25.0300, 121.5000)));
        _api.FakeAi.ValhallaAlternates.Add(Trip(seconds: 660, km: 0.90, (25.0290, 121.5000), (25.0295, 121.4990), (25.0300, 121.5000)));
    }

    // 其他測試預期 Valhalla 沒開（用直線估算），用完要還原
    public void Dispose() => _api.FakeAi.Reset();

    /// <summary>Valhalla 的一條路線（trip）：一段，時間（秒）、距離（公里）、路線形狀</summary>
    private static object Trip(double seconds, double km, params (double lat, double lng)[] points) => new
    {
        legs = new[] { new { summary = new { time = seconds, length = km }, shape = EncodePolyline6(points) } },
        summary = new { time = seconds, length = km }
    };

    /// <summary>Valhalla 的路線形狀編碼（精度 6 位的 encoded polyline）</summary>
    private static string EncodePolyline6((double lat, double lng)[] points)
    {
        var sb = new System.Text.StringBuilder();
        long lastLat = 0, lastLng = 0;

        void Append(long delta)
        {
            long value = delta < 0 ? ~(delta << 1) : delta << 1;
            while (value >= 0x20)
            {
                sb.Append((char)((0x20 | (value & 0x1f)) + 63));
                value >>= 5;
            }
            sb.Append((char)(value + 63));
        }

        foreach (var (lat, lng) in points)
        {
            long y = (long)Math.Round(lat * 1e6), x = (long)Math.Round(lng * 1e6);
            Append(y - lastLat);
            Append(x - lastLng);
            (lastLat, lastLng) = (y, x);
        }

        return sb.ToString();
    }

    private async Task<RoutePlanResponse> PlanAsync(int user, object body)
    {
        HttpResponseMessage response = await _api.ClientFor(user).PostAsJsonAsync("/api/Route/Plan", body);
        string json = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, json);
        return (await response.ReadResultAsync<RoutePlanResponse>()).Result;
    }

    [Fact]
    public async Task 自選地點_第一段多給同一種交通方式的替代走法_主路線不會被替代走法搶走()
    {
        RoutePlanResponse plan = await PlanAsync(_api.NewUserId(), new
        {
            start = new { lat = 25.029, lng = 121.5, name = "出發點" },
            points = new[] { new { lat = 25.030, lng = 121.5, name = "目的地" } },
            transportation = new[] { "步行" },
            include_return = true
        });

        Assert.Equal(new[] { "去程", "返程" }, plan.legs.Select(l => l.direction));

        // 去程（現在要走的那一段）：主路線 + 2 條替代走法，每一條都有自己的代號與路線座標
        RouteLeg outbound = plan.legs[0];
        Assert.Equal(new[] { "步行-1", "步行-2", "步行-3" }, outbound.options.Select(o => o.option_id).OrderBy(id => id));
        Assert.Equal(new[] { false, true, true }, outbound.options.OrderBy(o => o.option_id).Select(o => o.is_alternative));
        Assert.All(outbound.options, o => Assert.Equal(3, Assert.Single(o.segments).coordinates.Count));
        Assert.Equal(3, outbound.options.Select(o => string.Join("|", o.segments[0].coordinates.Select(c => $"{c[0]},{c[1]}"))).Distinct().Count());

        // 採用的是主路線（10 分鐘），不是快了幾秒的替代走法；使用者自己點了才換
        RouteOption chosen = Assert.Single(outbound.options, o => o.is_chosen);
        Assert.Equal(("步行-1", false, 10.0), (chosen.option_id, chosen.is_alternative, chosen.minutes));
        Assert.Equal((10.0, 0.8), (outbound.minutes, outbound.distance_km));
        Assert.Equal("步行 0.8 公里，約 10 分鐘", chosen.summary);
        Assert.StartsWith("替代路線：", outbound.options.Single(o => o.option_id == "步行-2").summary);
        Assert.Equal(9.9, outbound.options.Single(o => o.option_id == "步行-2").minutes);

        // 返程不是現在要走的：只有主路線
        RouteOption back = Assert.Single(plan.legs[1].options);
        Assert.Equal(("步行-1", false, true), (back.option_id, back.is_alternative, back.is_chosen));
    }

    [Fact]
    public async Task 劇本路線_只有前往目前這一關的那一段有替代走法()
    {
        int user = _api.NewUserId();
        string[] places = Enumerable.Range(1, 3).Select(i => $"替代路線景點{i}_{Guid.NewGuid():N}"[..18]).ToArray();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, places);
        object body = new { story_id = storyId, start = new { lat = 25.029, lng = 121.5, name = "出發點" }, transportation = new[] { "步行", "機車" } };

        // 一開始：只有「出發點 → 第 1 站」這一段，每種交通方式各有主路線 + 2 條替代走法
        RouteLeg first = Assert.Single((await PlanAsync(user, body)).legs);
        Assert.True(first.is_current);
        Assert.Equal(new[] { "機車-1", "機車-2", "機車-3", "步行-1", "步行-2", "步行-3" }, first.options.Select(o => o.option_id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(2, first.options.Count(o => !o.is_alternative));
        Assert.False(Assert.Single(first.options, o => o.is_chosen).is_alternative);

        // 抵達第 1 站（沒有任務，抵達就破關）→ 第 2 站解鎖
        int firstNode = await _api.QueryAsync<int>("SELECT sn_id FROM story_node WHERE s_id = @storyId AND sn_order = 1;", new { storyId });
        Assert.Equal(HttpStatusCode.OK, (await _api.ClientFor(user).PostAsync($"/api/Map/Node/{firstNode}/Arrive?lat=25.03&lng=121.5", null)).StatusCode);

        RoutePlanResponse plan = await PlanAsync(user, body);

        // 走過的那一段只剩主路線；前往目前這一關（第 2 站）的那一段才有替代走法
        Assert.Equal(new[] { false, true }, plan.legs.Select(l => l.is_current));
        Assert.All(plan.legs[0].options, o => Assert.False(o.is_alternative));
        Assert.Equal(2, plan.legs[0].options.Count);
        Assert.Equal(6, plan.legs[1].options.Count);
        Assert.Equal(4, plan.legs[1].options.Count(o => o.is_alternative));
    }

    [Fact]
    public async Task Valhalla沒開時_照舊用直線估算_只有一條()
    {
        _api.FakeAi.Reset();   // 沒有設定路線 → Valhalla 回 404

        RoutePlanResponse plan = await PlanAsync(_api.NewUserId(), new
        {
            start = new { lat = 25.029, lng = 121.5, name = "出發點" },
            points = new[] { new { lat = 25.030, lng = 121.5, name = "目的地" } },
            transportation = new[] { "步行" },
            include_return = false
        });

        RouteOption only = Assert.Single(Assert.Single(plan.legs).options);
        Assert.Equal(("步行-1", false, true), (only.option_id, only.is_alternative, only.is_chosen));
        Assert.Contains("直線估算", only.summary);
    }
}
