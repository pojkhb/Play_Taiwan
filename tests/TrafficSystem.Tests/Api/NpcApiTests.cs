// NPC 語音 API（外部 AI /api/npc/speak、/api/jobs/{task_id} 由 FakeAiService 模擬）
// AI 是排隊模式：送出只拿到 task_id，再用 GET /api/Npc/Speak/{task_id} 查進度，完成時才有 mp3 網址
using System.Net.Http.Json;
using backend.Services;
using backend.utils;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("劇本", "API 整合測試")]
[AllureBddHierarchy("劇本", "API 整合測試")]
public class NpcApiTests
{
    private readonly ApiFactory _api;

    public NpcApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
    }

    private async Task<ApiResult<NpcSpeakJobResponse>> StatusAsync(string taskId) =>
        await (await _api.ClientFor(_api.NewUserId()).GetAsync($"/api/Npc/Speak/{taskId}")).ReadResultAsync<NpcSpeakJobResponse>();

    [Fact]
    public async Task 劇情前傳語音_排入佇列並回傳task_id()
    {
        int user = _api.NewUserId();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, "台北101");

        ApiResult<NpcSpeakResponse> result =
            await (await _api.ClientFor(user).PostAsync($"/api/Npc/Prologue/{storyId}", null)).ReadResultAsync<NpcSpeakResponse>();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal(FakeAiService.NpcTaskId, result.Result.task_id);
        Assert.Equal("waiting", result.Result.status);
        Assert.Equal("等待中", result.Result.status_text);
        Assert.Equal(1, result.Result.queue_position);
        Assert.Null(result.Result.audio_url);                              // 還沒做好
        Assert.Equal(NpcVoiceService.DefaultNpc, result.Result.npc_name);  // 測試劇本沒指定 NPC，用預設的薯光

        FakeAiService.Received sent = _api.FakeAi.Last("/api/npc/speak");
        Assert.Equal("前情提要", sent.Fields["text"]);                    // CreateStoryAsync 寫入的前傳
        Assert.Equal(NpcVoiceService.DefaultNpc, sent.Fields["npc_name"]);
        Assert.False(sent.Fields.ContainsKey("voice"));                    // AI 已改用 npc_name
    }

    [Fact]
    public async Task NPC說話可以指定NPC()
    {
        var body = new { text = "探員，第一條線索就藏在公園裡。", npc_name = "墨先生" };

        ApiResult<NpcSpeakResponse> result =
            await (await _api.ClientFor(_api.NewUserId()).PostAsJsonAsync("/api/Npc/Speak", body)).ReadResultAsync<NpcSpeakResponse>();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal(body.text, result.Result.text);
        Assert.Equal("墨先生", result.Result.npc_name);
        FakeAiService.Received sent = _api.FakeAi.Last("/api/npc/speak");
        Assert.Equal(body.text, sent.Fields["text"]);
        Assert.Equal("墨先生", sent.Fields["npc_name"]);
    }

    [Fact]
    public async Task 查進度_做好時回傳mp3網址()
    {
        ApiResult<NpcSpeakJobResponse> result = await StatusAsync(FakeAiService.NpcTaskId);

        Assert.True(result.isSuccess, result.message);
        Assert.Equal("completed", result.Result.status);
        Assert.Equal("完成", result.Result.status_text);
        Assert.Equal(AiServiceConfig.Url("/api/download/npc_npc-001.mp3"), result.Result.audio_url);
        Assert.Null(result.Result.queue_position);
        Assert.Equal("GET", _api.FakeAi.Last("/api/jobs/" + FakeAiService.NpcTaskId).Method);
    }

    [Fact]
    public async Task 查進度_排隊中回傳等待中與排第幾位()
    {
        _api.FakeAi.NpcJobResponse = new { task_id = FakeAiService.NpcTaskId, task_type = "npc_tts", status = "queued", stage = "等待處理", queue_position = 3 };

        ApiResult<NpcSpeakJobResponse> result = await StatusAsync(FakeAiService.NpcTaskId);

        Assert.True(result.isSuccess, result.message);
        Assert.Equal("waiting", result.Result.status);
        Assert.Equal("等待中", result.Result.status_text);
        Assert.Equal("等待處理", result.Result.stage);
        Assert.Equal(3, result.Result.queue_position);
        Assert.Null(result.Result.audio_url);
    }

    [Fact]
    public async Task 查進度_處理中回傳生成中()
    {
        _api.FakeAi.NpcJobResponse = new { task_id = FakeAiService.NpcTaskId, task_type = "npc_tts", status = "processing", stage = "語音合成中" };

        ApiResult<NpcSpeakJobResponse> result = await StatusAsync(FakeAiService.NpcTaskId);

        Assert.True(result.isSuccess, result.message);
        Assert.Equal("processing", result.Result.status);
        Assert.Equal("生成中", result.Result.status_text);
        Assert.Equal("語音合成中", result.Result.stage);
        Assert.Null(result.Result.queue_position);
    }

    [Fact]
    public async Task 查進度_AI回報失敗時帶出原因()
    {
        _api.FakeAi.NpcJobResponse = new { task_id = FakeAiService.NpcTaskId, task_type = "npc_tts", status = "failed", error = "TTS 服務逾時" };

        ApiResult<NpcSpeakJobResponse> result = await StatusAsync(FakeAiService.NpcTaskId);

        Assert.False(result.isSuccess);
        Assert.Equal("failed", result.Result.status);
        Assert.Equal("TTS 服務逾時", result.Result.message);
        Assert.Equal("TTS 服務逾時", result.message);
    }

    [Fact]
    public async Task 查進度_做好卻沒有語音檔算失敗()
    {
        _api.FakeAi.NpcJobResponse = new { task_id = FakeAiService.NpcTaskId, task_type = "npc_tts", status = "completed", result = new { } };

        ApiResult<NpcSpeakJobResponse> result = await StatusAsync(FakeAiService.NpcTaskId);

        Assert.False(result.isSuccess);
        Assert.Equal("failed", result.Result.status);
        Assert.Contains("沒有回傳語音檔", result.Result.message);
    }

    [Fact]
    public async Task 查進度_AI重啟後任務消失算失敗()
    {
        ApiResult<NpcSpeakJobResponse> result = await StatusAsync("lost-task");   // 假 AI 對不認得的任務回 404

        Assert.False(result.isSuccess);
        Assert.Equal("lost-task", result.Result.task_id);
        Assert.Equal("failed", result.Result.status);
        Assert.Equal(NpcVoiceService.LostMessage, result.Result.message);
    }

    [Fact]
    public async Task 查進度_不是語音任務的ID查不到結果()
    {
        // 用別的功能（例如劇本生成）的任務 ID 來查，不能把那個任務的結果帶出來
        _api.FakeAi.NpcJobResponse = new { task_id = FakeAiService.NpcTaskId, task_type = "story_batch", status = "completed", result = new { url = "/api/download/secret.json" } };

        ApiResult<NpcSpeakJobResponse> result = await StatusAsync(FakeAiService.NpcTaskId);

        Assert.False(result.isSuccess);
        Assert.Equal("failed", result.Result.status);
        Assert.Null(result.Result.audio_url);
    }

    [Fact]
    public async Task 沒有文字時擋下_不呼叫AI()
    {
        ApiResult<string> result =
            await (await _api.ClientFor(_api.NewUserId()).PostAsJsonAsync("/api/Npc/Speak", new { text = "  " })).ReadResultAsync<string>();

        Assert.False(result.isSuccess);
        Assert.Contains("請提供要唸的文字", result.message);
        Assert.Null(_api.FakeAi.Last("/api/npc/speak"));
    }

    [Fact]
    public async Task 不存在的劇本回傳錯誤()
    {
        ApiResult<string> result =
            await (await _api.ClientFor(_api.NewUserId()).PostAsync("/api/Npc/Prologue/999999", null)).ReadResultAsync<string>();

        Assert.False(result.isSuccess);
        Assert.Contains("找不到此劇本", result.message);
    }
}
