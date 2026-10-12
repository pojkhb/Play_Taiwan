// 劇本路線一關一關給（POST /api/Route/Plan、GET /api/Route/Stories）：只到目前這一關，還在迷霧中的站不回傳
using System.Net;
using System.Net.Http.Json;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("解謎地圖與迷霧", "API 整合測試")]
[AllureBddHierarchy("解謎地圖與迷霧", "API 整合測試")]
public class RouteStageApiTests
{
    private readonly ApiFactory _api;

    public RouteStageApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeNeo4j.Reset();   // Neo4j 連不上，景點座標用 MySQL
    }

    /// <summary>建立 3 站的劇本（景點名稱不重複）；回傳 (story_id, 每站 sn_id, 景點名稱)</summary>
    private async Task<(int storyId, int[] nodes, string[] places)> CreateStoryAsync(int user)
    {
        string[] places = Enumerable.Range(1, 3).Select(i => $"路線景點{i}_{Guid.NewGuid():N}"[..16]).ToArray();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, places);
        // 測試劇本的標題預設會帶景點名稱，換掉才能檢查「回傳裡沒有迷霧中的景點名稱」
        await _api.ExecuteAsync("UPDATE story SET story_title = '藏寶圖路線測試' WHERE s_id = @storyId;", new { storyId });

        using var conn = new MySql.Data.MySqlClient.MySqlConnection(_api.ConnectionString);
        int[] nodes = (await Dapper.SqlMapper.QueryAsync<int>(conn,
            "SELECT sn_id FROM story_node WHERE s_id = @storyId ORDER BY sn_order;", new { storyId })).ToArray();
        return (storyId, nodes, places);
    }

    private static object PlanBody(int storyId) => new
    {
        story_id = storyId,
        start = new { lat = 25.029, lng = 121.5, name = "出發點" },
        transportation = new[] { "步行" },
        include_return = true
    };

    private async Task<(RoutePlanResponse plan, string json)> PlanAsync(int user, int storyId)
    {
        HttpResponseMessage response = await _api.ClientFor(user).PostAsJsonAsync("/api/Route/Plan", PlanBody(storyId));
        string json = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, json);
        return ((await response.ReadResultAsync<RoutePlanResponse>()).Result, json);
    }

    private Task ArriveAsync(int user, int nodeId, int index) =>
        _api.ClientFor(user).PostAsync($"/api/Map/Node/{nodeId}/Arrive?lat={25.03 + index * 0.001}&lng=121.5", null);

    [Fact]
    public async Task 一開始只規劃到第一站_後面的站不回傳()
    {
        int user = _api.NewUserId();
        var (storyId, nodes, places) = await CreateStoryAsync(user);

        var (plan, json) = await PlanAsync(user, storyId);

        RouteStopPoint stop = Assert.Single(plan.stops);
        Assert.Equal(nodes[0], stop.sn_id);
        Assert.True(stop.is_current);
        Assert.False(stop.is_cleared);
        Assert.Equal(2, plan.locked_stop_count);

        // 還沒抵達：站名是地點代號，不是景點真實名稱
        Assert.Equal("代號1", stop.name);
        Assert.Equal("代號1", stop.codename);

        // 只有一段：出發點 → 第 1 站，是目前這一關的路線；還有站在迷霧中，不規劃返程
        RouteLeg leg = Assert.Single(plan.legs);
        Assert.Equal("去程", leg.direction);
        Assert.Equal("代號1", leg.to_name);
        Assert.Equal(nodes[0], leg.to_sn_id);
        Assert.True(leg.is_current);

        // 迷霧中的站：真實名稱、代號、座標都不在回傳裡
        foreach (string hidden in new[] { places[0], places[1], places[2], "代號2", "代號3", "25.031", "25.032" })
            Assert.DoesNotContain(hidden, json);
    }

    [Fact]
    public async Task 抵達後站名換成真實名稱_破關後才多出下一站的路線()
    {
        int user = _api.NewUserId();
        var (storyId, nodes, places) = await CreateStoryAsync(user);
        await _api.ExecuteAsync("INSERT INTO task (story_id, node_id, task_type, task_describe) VALUES (@storyId, @node, 6, '題目');",
            new { storyId, node = nodes[0] });

        // 抵達第 1 站但任務還沒過：站名換成真實名稱，還是只有一站
        await ArriveAsync(user, nodes[0], 0);
        var (arrived, arrivedJson) = await PlanAsync(user, storyId);
        Assert.Equal(places[0], Assert.Single(arrived.stops).name);
        Assert.True(arrived.stops[0].is_current);
        Assert.DoesNotContain(places[1], arrivedJson);

        // 第 1 站破關：多出第 2 站（代號）與第 1 站 → 第 2 站的路線
        await _api.ExecuteAsync("UPDATE task SET pass = 1 WHERE story_id = @storyId;", new { storyId });
        var (cleared, clearedJson) = await PlanAsync(user, storyId);

        Assert.Equal(new int?[] { nodes[0], nodes[1] }, cleared.stops.Select(s => s.sn_id));
        Assert.Equal(new[] { places[0], "代號2" }, cleared.stops.Select(s => s.name));
        Assert.Equal(new[] { true, false }, cleared.stops.Select(s => s.is_cleared));
        Assert.Equal(new[] { false, true }, cleared.stops.Select(s => s.is_current));
        Assert.Equal(1, cleared.locked_stop_count);

        Assert.Equal(2, cleared.legs.Count);
        Assert.Equal(new[] { false, true }, cleared.legs.Select(l => l.is_current));
        Assert.Equal(places[0], cleared.legs[1].from_name);
        Assert.Equal("代號2", cleared.legs[1].to_name);
        Assert.DoesNotContain(places[2], clearedJson);
    }

    [Fact]
    public async Task 全部破關後回傳每一站_並規劃返程()
    {
        int user = _api.NewUserId();
        var (storyId, nodes, places) = await CreateStoryAsync(user);
        for (int i = 0; i < 3; i++)
            await ArriveAsync(user, nodes[i], i);   // 沒有任務的站抵達就算破關

        var (plan, _) = await PlanAsync(user, storyId);

        Assert.Equal(places, plan.stops.Select(s => s.name));
        Assert.All(plan.stops, s => Assert.True(s.is_cleared));
        Assert.Equal(0, plan.locked_stop_count);
        Assert.Equal(new[] { "去程", "去程", "去程", "返程" }, plan.legs.Select(l => l.direction));
        Assert.All(plan.legs, l => Assert.False(l.is_current));
    }

    [Fact]
    public async Task 只規劃目前這一段_從使用者位置到目前這一關()
    {
        int user = _api.NewUserId();
        var (storyId, nodes, places) = await CreateStoryAsync(user);
        HttpClient client = _api.ClientFor(user);

        async Task<RoutePlanResponse> CurrentLegAsync(object start)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync("/api/Route/Plan",
                new { story_id = storyId, start, transportation = new[] { "步行" }, include_return = true, current_only = true });
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.ReadResultAsync<RoutePlanResponse>()).Result;
        }

        var me = new { lat = 25.0295, lng = 121.5, name = "我的位置" };

        // 第 1 關：我的位置 → 代號1
        RoutePlanResponse first = await CurrentLegAsync(me);
        RouteLeg leg = Assert.Single(first.legs);
        Assert.Equal(("我的位置", "代號1", (int?)nodes[0], true), (leg.from_name, leg.to_name, leg.to_sn_id, leg.is_current));

        // 破完第 1 關：只有一段，我的位置 → 代號2；已解鎖的兩站都在 stops 裡
        await ArriveAsync(user, nodes[0], 0);
        RoutePlanResponse second = await CurrentLegAsync(me);
        leg = Assert.Single(second.legs);
        Assert.Equal(("我的位置", "代號2", (int?)nodes[1]), (leg.from_name, leg.to_name, leg.to_sn_id));
        Assert.Equal(new[] { places[0], "代號2" }, second.stops.Select(s => s.name));

        // 沒有定位：從上一站出發
        leg = Assert.Single((await CurrentLegAsync(null)).legs);
        Assert.Equal((places[0], "代號2"), (leg.from_name, leg.to_name));

        // 全部破關後沒有路線可以規劃
        await ArriveAsync(user, nodes[1], 1);
        await ArriveAsync(user, nodes[2], 2);
        RoutePlanResponse done = await CurrentLegAsync(me);
        Assert.Empty(done.legs);
        Assert.Equal(3, done.stops.Count);
    }

    [Fact]
    public async Task 沒有參與的劇本不能規劃路線()
    {
        int owner = _api.NewUserId();
        var (storyId, _, places) = await CreateStoryAsync(owner);
        HttpClient stranger = _api.ClientFor(_api.NewUserId());

        HttpResponseMessage plan = await stranger.PostAsJsonAsync("/api/Route/Plan", PlanBody(storyId));
        Assert.Equal(HttpStatusCode.Forbidden, plan.StatusCode);
        Assert.DoesNotContain(places[0], await plan.Content.ReadAsStringAsync());

        HttpResponseMessage availability = await stranger.GetAsync($"/api/Route/Availability?story_id={storyId}");
        Assert.Equal(HttpStatusCode.Forbidden, availability.StatusCode);
    }

    [Fact]
    public async Task 劇本清單只有自己的劇本_節點只給已解鎖的()
    {
        int user = _api.NewUserId();
        var (storyId, nodes, places) = await CreateStoryAsync(user);
        var (othersStory, _, othersPlaces) = await CreateStoryAsync(_api.NewUserId());

        HttpResponseMessage response = await _api.ClientFor(user).GetAsync("/api/Route/Stories");
        string json = await response.Content.ReadAsStringAsync();
        List<RouteStoryItem> stories = (await response.ReadResultAsync<List<RouteStoryItem>>()).Result;

        RouteStoryItem mine = Assert.Single(stories);
        Assert.Equal(storyId, mine.story_id);
        Assert.Equal(3, mine.node_count);
        Assert.Equal(nodes[0], Assert.Single(mine.nodes).sn_id);
        Assert.DoesNotContain(places[1], json);
        Assert.DoesNotContain(othersPlaces[0], json);
        Assert.DoesNotContain($"\"story_id\":{othersStory},", json);
    }

    [Fact]
    public async Task 自選地點的路線不受劇本進度影響()
    {
        var body = new
        {
            start = new { lat = 25.029, lng = 121.5, name = "出發點" },
            points = new[]
            {
                new { lat = 25.03, lng = 121.5, name = "甲" },
                new { lat = 25.031, lng = 121.5, name = "乙" },
            },
            transportation = new[] { "步行" }
        };

        HttpResponseMessage response = await _api.ClientFor(_api.NewUserId()).PostAsJsonAsync("/api/Route/Plan", body);
        RoutePlanResponse plan = (await response.ReadResultAsync<RoutePlanResponse>()).Result;

        Assert.Equal(new[] { "甲", "乙" }, plan.stops.Select(s => s.name));
        Assert.Equal(0, plan.locked_stop_count);
        Assert.Equal(new[] { "去程", "去程", "返程" }, plan.legs.Select(l => l.direction));
        Assert.All(plan.legs, l => Assert.False(l.is_current));
    }
}
