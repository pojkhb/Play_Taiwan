// 遊你說了算（POST /api/Story/GenerateByText）：AI 端點送出後回 job_id，後端要輪詢到完成才存檔
using System.Net.Http.Json;
using backend.Controllers;
using backend.Models;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("劇本", "API 整合測試")]
[AllureBddHierarchy("劇本", "API 整合測試")]
public class GenerateByTextApiTests
{
    private readonly ApiFactory _api;

    public GenerateByTextApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
        StoryController.TextBlueprintPollInterval = TimeSpan.FromMilliseconds(20);   // 測試不用真的等 3 秒
    }

    private static object Processing(string stage) => new { job_id = FakeAiService.TextJobId, status = "processing", stage, data = (object)null };

    private static object Completed(string place1, string place2) => new
    {
        job_id = FakeAiService.TextJobId,
        status = "completed",
        parsed_intent = new { city_name = "臺北市", town_name = "中正區", traveler_count = 2, preferences = new[] { "歷史人文" }, transportation = new[] { "步行" }, node_count = 2, is_night = false },
        data = new
        {
            title = "城南的失落卷軸",
            preface = "一封沒有署名的信，把你帶到了城南。",
            synopsis = "沿著兩個地標找回卷軸。",
            is_night_mode = false,
            nodes = new[]
            {
                new { node_order = 1, place_name = place1, location_codename = "白色穹頂", node_title = "穹頂下的信", task_type = "文化問答型", task_description = "找出牌樓上的字", dialogues = new { opening = "開場", success = "完成" } },
                new { node_order = 2, place_name = place2, location_codename = "紅色古剎", node_title = "古剎的香火", task_type = "創意攝影型", task_description = "拍一張香爐", dialogues = new { opening = "開場", success = "完成" } },
            }
        }
    };

    [Fact]
    public async Task 輪詢到AI完成後存成劇本()
    {
        _api.FakeAi.TextJobPolls.Enqueue(Processing("解析需求"));
        _api.FakeAi.TextJobPolls.Enqueue(Processing("生成劇本"));
        _api.FakeAi.TextJobPolls.Enqueue(Completed($"文字測試景點A{Guid.NewGuid():N}", $"文字測試景點B{Guid.NewGuid():N}"));

        HttpResponseMessage response = await _api.ClientFor(_api.NewUserId())
            .PostAsJsonAsync("/api/Story/GenerateByText", new { user_prompt = "兩個人想去台北中正區走走，看看古蹟" });
        ApiResult<GenerateByTextResult> result = await response.ReadResultAsync<GenerateByTextResult>();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal("臺北市", result.Result.detected_city);
        Assert.True(result.Result.story_id > 0);
        Assert.Equal(2, await _api.QueryAsync<int>("SELECT COUNT(*) FROM story_node WHERE s_id = @id;", new { id = result.Result.story_id }));
        string sent = _api.FakeAi.Last("/api/api/admin/generate_script_blueprint_by_text").Fields["body"];
        Assert.Equal("兩個人想去台北中正區走走，看看古蹟", System.Text.Json.JsonDocument.Parse(sent).RootElement.GetProperty("user_prompt").GetString());
    }

    [Fact]
    public async Task AI生成失敗時回傳失敗階段與原因()
    {
        _api.FakeAi.TextJobPolls.Enqueue(new { job_id = FakeAiService.TextJobId, status = "error", stage = "查詢 Neo4j 地點", error = "找不到 中正區 地點資料。", data = (object)null });

        ApiResult<string> result = await (await _api.ClientFor(_api.NewUserId())
            .PostAsJsonAsync("/api/Story/GenerateByText", new { user_prompt = "台北中正區" })).ReadResultAsync<string>();

        Assert.False(result.isSuccess);
        Assert.Equal("AI 劇本生成失敗（查詢 Neo4j 地點）：找不到 中正區 地點資料。", result.message);
    }
}
