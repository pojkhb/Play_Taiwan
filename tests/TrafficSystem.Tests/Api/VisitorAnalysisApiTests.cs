// 客群分析 / AI 經營策略：.NET 轉呼 FastAPI（由 FakeAiService 模擬）
using System.Net.Http.Json;
using backend.Models;
using backend.ViewModels;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
public class VisitorAnalysisApiTests
{
    private readonly ApiFactory _api;

    public VisitorAnalysisApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
    }

    [Fact]
    public async Task Attractions_轉呼FastAPI並回傳去重景點清單()
    {
        HttpClient client = _api.CreateClient();
        HttpResponseMessage response = await client.GetAsync(
            "/api/VisitorAnalysis/Attractions?keyword=勤美&page=1&pageSize=20");

        ApiResult<VisitorAttractionListResponse> result =
            await response.ReadResultAsync<VisitorAttractionListResponse>();

        Assert.True(result.isSuccess, result.message);
        Assert.NotNull(result.Result);
        Assert.Equal(1, result.Result.total);
        Assert.Single(result.Result.items);
        Assert.Equal("勤美誠品綠園道", result.Result.items[0].name);
        Assert.Equal(new[] { "attr-001", "attr-001b" }, result.Result.items[0].uids);
        Assert.True(result.Result.items[0].has_analysis);

        FakeAiService.Received sent = _api.FakeAi.Last("/internal/visitor-analysis/attractions");
        Assert.Equal("GET", sent.Method);
    }

    [Fact]
    public async Task StrategyChat_LLM未就緒時回傳not_ready且不含假策略()
    {
        HttpClient client = _api.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/VisitorAnalysis/Strategy/Chat",
            new VisitorStrategyChatRequest
            {
                attraction_uid = "attr-001",
                business_type = "咖啡廳",
                goal = "提高平日來客",
                budget = "5萬以內",
                visitor_type = "domestic",
                question = "如何吸引 30-39 歲客群？"
            });

        ApiResult<VisitorStrategyChatResponse> result =
            await response.ReadResultAsync<VisitorStrategyChatResponse>();

        Assert.True(result.isSuccess, result.message);
        Assert.NotNull(result.Result);
        Assert.Equal("not_ready", result.Result.status);
        Assert.Empty(result.Result.strategies ?? new List<object>());

        FakeAiService.Received sent = _api.FakeAi.Last("/internal/visitor-analysis/strategy/generate");
        Assert.Equal("POST", sent.Method);
        Assert.Contains("attr-001", sent.Fields["body"]);
    }

    [Fact]
    public async Task StrategyChat_缺少uid回傳400()
    {
        HttpClient client = _api.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/VisitorAnalysis/Strategy/Chat",
            new VisitorStrategyChatRequest { question = "你好" });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }
}
