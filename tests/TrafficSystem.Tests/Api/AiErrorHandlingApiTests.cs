// 外部服務失敗時，後端不能當成成功或「查無資料」
// - 本地 Neo4j 連不上 → 附近景點改用 MySQL 的景點；Neo4j 正常但查無資料 → 空清單
// - /api/agent/orchestrate 找不到地點：只有 {"message"} → spin 回傳失敗與原因
using System.Net.Http.Json;
using System.Text.Json;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
public class AiErrorHandlingApiTests
{
    private readonly ApiFactory _api;

    public AiErrorHandlingApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
        _api.FakeNeo4j.Reset();
    }

    [Fact]
    public async Task 附近景點_Neo4j連不上時改用MySQL的景點()
    {
        string near = $"近的景點{Guid.NewGuid():N}"[..14], far = $"遠的景點{Guid.NewGuid():N}"[..14];
        await _api.CreateStoryAsync(_api.NewUserId(), "臺北市", night: false, near);
        await _api.ExecuteAsync("INSERT INTO place (p_name, p_latitude, p_longitude) VALUES (@far, 25.2, 121.5);", new { far });

        var result = await (await _api.ClientFor(_api.NewUserId())
            .GetAsync("/api/Story/NearbyAttractions?lat=25.03&lng=121.5&radiusKm=0.5")).ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        var items = result.Result.EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("name").GetString() == near);
        Assert.DoesNotContain(items, i => i.GetProperty("name").GetString() == far);
        var distances = items.Select(i => i.GetProperty("distance_m").GetDouble()).ToList();
        Assert.All(distances, d => Assert.InRange(d, 0, 500));
        Assert.Equal(distances.OrderBy(d => d), distances);
    }

    [Fact]
    public async Task 附近景點_Neo4j有資料時直接回傳Neo4j的景點()
    {
        _api.FakeNeo4j.Rows = new List<Dictionary<string, object>>
        {
            new() { ["name"] = "Neo4j景點A", ["lat"] = 25.031, ["lon"] = 121.501, ["distance_m"] = 120.5 },
            new() { ["name"] = "Neo4j景點B", ["lat"] = "25.032", ["lon"] = "121.502", ["distance_m"] = 300L },   // 有些景點經緯度存成字串
        };
        try
        {
            var result = await (await _api.ClientFor(_api.NewUserId())
                .GetAsync("/api/Story/NearbyAttractions?lat=25.03&lng=121.5&radiusKm=0.5")).ReadResultAsync();

            Assert.True(result.isSuccess, result.message);
            var items = result.Result.EnumerateArray().ToList();
            Assert.Equal(new[] { "Neo4j景點A", "Neo4j景點B" }, items.Select(i => i.GetProperty("name").GetString()));
            Assert.Equal(25.032, items[1].GetProperty("lat").GetDouble());
            Assert.Contains(_api.FakeNeo4j.Queries, q => q.Contains("MATCH (a:Attraction)"));
        }
        finally
        {
            _api.FakeNeo4j.Reset();
        }
    }

    [Fact]
    public async Task 附近景點_Neo4j正常但附近真的沒有景點時回空清單()
    {
        await _api.CreateStoryAsync(_api.NewUserId(), "臺北市", night: false, $"MySQL景點{Guid.NewGuid():N}"[..14]);
        _api.FakeNeo4j.Rows = new List<Dictionary<string, object>>();
        try
        {
            var result = await (await _api.ClientFor(_api.NewUserId())
                .GetAsync("/api/Story/NearbyAttractions?lat=25.03&lng=121.5&radiusKm=0.5")).ReadResultAsync();

            Assert.True(result.isSuccess, result.message);
            Assert.Empty(result.Result.EnumerateArray());
        }
        finally
        {
            _api.FakeNeo4j.Reset();   // 其他測試預設 Neo4j 連不上
        }
    }

    [Fact]
    public async Task Spin_AI找不到地點時回傳失敗與原因()
    {
        var body = new { input_text = "想找個安靜的地方走走", emotion_label = "疲憊", city_name = "臺北市", town_name = "中正區" };

        var result = await (await _api.ClientFor(_api.NewUserId()).PostAsJsonAsync("/api/Story/spin", body)).ReadResultAsync();

        Assert.False(result.isSuccess);
        Assert.Contains("附近沒有符合您當下心情的地點", result.message);

        using JsonDocument sent = JsonDocument.Parse(_api.FakeAi.Last("/api/agent/orchestrate").Fields["body"]);
        Assert.Equal(25.0324, sent.RootElement.GetProperty("user_lat").GetDouble());
        Assert.Equal("疲憊", sent.RootElement.GetProperty("emotion_label").GetString());
    }

    [Fact]
    public async Task Spin_AI有推薦時回傳推薦內容()
    {
        _api.FakeAi.AgentOrchestrateResponse = new
        {
            phase_2_cognition = new { },
            phase_3_graph_rag = new { },
            phase_4_action_and_tools = new { },
            phase_5_next_step = "往前走 200 公尺就到了"
        };
        var body = new { input_text = "想找個安靜的地方走走", city_name = "臺北市", town_name = "中正區" };

        var result = await (await _api.ClientFor(_api.NewUserId()).PostAsJsonAsync("/api/Story/spin", body)).ReadResultAsync();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal("往前走 200 公尺就到了", result.Result.GetProperty("agent_result").GetProperty("phase_5_next_step").GetString());
    }
}
