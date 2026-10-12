// 任務提示：選擇題答錯 1 次就給提示，協作解謎與沒有對錯的題型一開始就給，不用讓玩家多猜幾次
using System.Net;
using System.Net.Http.Json;
using backend.Models;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("任務", "API 整合測試")]
[AllureBddHierarchy("任務", "API 整合測試")]
public class TaskHintApiTests
{
    private readonly ApiFactory _api;

    public TaskHintApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
    }

    /// <summary>建立劇本並在第一站放一題有提示的任務，回傳 (第一站 node_id, task_id)</summary>
    private async Task<(int firstNode, int taskId)> CreateTaskAsync(int user, int typeId)
    {
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, "台北101");
        int firstNode = await _api.QueryAsync<int>("SELECT sn_id FROM story_node WHERE s_id = @storyId;", new { storyId });
        await _api.ExecuteAsync(@"
            INSERT INTO task (story_id, node_id, task_type, task_describe, task_hint)
            VALUES (@storyId, @firstNode, @typeId, '牌樓上寫的是哪四個字？', '抬頭看牌樓正中間');", new { storyId, firstNode, typeId });
        int taskId = await _api.QueryAsync<int>("SELECT MAX(task_id) FROM task WHERE node_id = @firstNode;", new { firstNode });
        return (firstNode, taskId);
    }

    private async Task<TaskHintResponse> HintAsync(HttpClient client, int taskId) =>
        (await (await client.GetAsync($"/api/Task/{taskId}/Hint")).ReadResultAsync<TaskHintResponse>()).Result;

    private async Task<NodePlayTask> NodeTaskAsync(HttpClient client, int firstNode, int taskId) =>
        (await (await client.GetAsync($"/api/Task/Node/{firstNode}")).ReadResultAsync<NodePlayResponse>())
            .Result.tasks.Single(t => t.task_id == taskId);

    [Theory]
    [InlineData(6)]    // 文化問答型
    [InlineData(7)]    // 景點猜猜樂
    [InlineData(9)]    // 商家知識問答
    [InlineData(10)]   // 圖像地理猜謎型
    public async Task 選擇題答錯一次就給提示(int typeId)
    {
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);
        var (firstNode, taskId) = await CreateTaskAsync(user, typeId);
        int optionId = await _api.QueryAsync<int>("SELECT COALESCE(MAX(option_id), 0) + 1 FROM task_option;");
        await _api.ExecuteAsync(@"
            INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key)
            VALUES (@a, @taskId, '博愛特區', 1, 'A'), (@b, @taskId, '天下為公', 0, 'B');", new { a = optionId, b = optionId + 1, taskId });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/Map/Node/{firstNode}/Arrive?lat=25.03&lng=121.5", null)).StatusCode);

        TaskHintResponse before = await HintAsync(client, taskId);
        Assert.False(before.is_available);
        Assert.Equal("先作答一次，答錯就能取得提示。", before.hint_text);
        Assert.False((await NodeTaskAsync(client, firstNode, taskId)).hint_available);

        ApiResult<TaskAnswerResponse> answer = await (await client.PostAsJsonAsync("/api/Task/Answer",
            new { task_id = taskId, gps_lat = 25.03, gps_lon = 121.5, selected_option_key = "B" })).ReadResultAsync<TaskAnswerResponse>();
        Assert.True(answer.isSuccess, answer.message);
        Assert.False(answer.Result.is_correct);

        TaskHintResponse after = await HintAsync(client, taskId);
        Assert.True(after.is_available);
        Assert.Equal("抬頭看牌樓正中間", after.hint_text);
        Assert.True((await NodeTaskAsync(client, firstNode, taskId)).hint_available);
    }

    [Theory]
    [InlineData(5)]   // 協作解謎型
    [InlineData(2)]   // 跨關集結型（沒有對錯）
    [InlineData(8)]   // e 人訪談型（沒有對錯）
    public async Task 協作解謎與沒有對錯的題型一開始就給提示(int typeId)
    {
        int user = _api.NewUserId();
        var (_, taskId) = await CreateTaskAsync(user, typeId);

        TaskHintResponse hint = await HintAsync(_api.ClientFor(user), taskId);

        Assert.True(hint.is_available);
        Assert.Equal("抬頭看牌樓正中間", hint.hint_text);
    }
}
