// NPC 語音 API（外部 AI /api/npc/speak、/api/jobs/{task_id} 由 FakeAiService 模擬）
// 流程：送出 → done 直接播放；processing 就用 GET /api/Npc/Speak/{job_id} 查到 done 或 failed。做好的 wav 存在後端
using System.Net;
using System.Net.Http.Json;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("劇本", "API 整合測試")]
[AllureBddHierarchy("劇本", "API 整合測試")]
public class NpcApiTests
{
    private readonly ApiFactory _api;

    /// <summary>假的 wav（RIFF....WAVE 開頭）</summary>
    private static readonly byte[] FakeWav = { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45, 1, 2, 3 };

    public NpcApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
    }

    /// <summary>每個測試用不同的台詞：同一位 NPC 唸同一段話會共用語音檔</summary>
    private static string UniqueText(string prefix = "探員，線索就藏在公園裡") => $"{prefix}（{Guid.NewGuid():N}）";

    private async Task<(HttpStatusCode code, ApiResult<NpcSpeakResponse> result)> SpeakAsync(HttpClient client, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/Npc/Speak", body);
        return (response.StatusCode, await response.ReadResultAsync<NpcSpeakResponse>());
    }

    private static async Task<NpcSpeakResponse> StatusAsync(HttpClient client, string jobId) =>
        (await (await client.GetAsync($"/api/Npc/Speak/{jobId}")).ReadResultAsync<NpcSpeakResponse>()).Result;

    private string AudioFile(string jobId) => Path.Combine(_api.FogRoot, "audio", "npc", jobId + ".wav");

    [Fact]
    public async Task 下拉選單_列出NPC名單_預設是薯光()
    {
        ApiResult<List<NpcOptionItem>> result =
            await (await _api.ClientFor(_api.NewUserId()).GetAsync("/api/Npc/Options")).ReadResultAsync<List<NpcOptionItem>>();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal(new[] { "薯光", "珍奶奶", "阿達力", "墨先生", "霓霓", "阿吉伯" }, result.Result.Select(o => o.value));
        Assert.All(result.Result, o => Assert.Equal(o.value, o.label));
        Assert.Equal("薯光", Assert.Single(result.Result, o => o.is_default).value);

        NpcOptionItem mo = result.Result.Single(o => o.value == "墨先生");
        Assert.Equal("博學嚴謹的文史工作者，擅長解讀古地圖與歷史檔案", mo.description);
        Assert.StartsWith("http", mo.avatar_url);
        Assert.EndsWith("/images/npc/mo-xian-sheng.png", mo.avatar_url);
    }

    [Fact]
    public async Task NPC說話_送給AI排隊_查進度到做好_語音存在後端()
    {
        HttpClient client = _api.ClientFor(_api.NewUserId());
        string text = UniqueText();

        var (code, sent) = await SpeakAsync(client, new { text, npc_name = "墨先生" });

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.True(sent.isSuccess, sent.message);
        Assert.Equal(NpcSpeakResponse.Processing, sent.Result.status);
        Assert.Matches("^[0-9a-f]{40}$", sent.Result.job_id);
        Assert.Equal("等待處理", sent.Result.stage);
        Assert.Equal(2, sent.Result.queue_position);
        Assert.Null(sent.Result.audio_url);

        FakeAiService.Received ai = _api.FakeAi.Last("/api/npc/speak");
        Assert.Equal(text, ai.Fields["text"]);
        Assert.Equal("墨先生", ai.Fields["npc_name"]);
        Assert.False(ai.Fields.ContainsKey("voice"));

        // AI 還在生成
        _api.FakeAi.NpcJobs[_api.FakeAi.LastNpcTaskId] = FakeAiService.NpcJob(_api.FakeAi.LastNpcTaskId, "running");
        NpcSpeakResponse running = await StatusAsync(client, sent.Result.job_id);
        Assert.Equal(NpcSpeakResponse.Processing, running.status);
        Assert.Equal("正在生成 XTTS 語音", running.stage);
        Assert.Null(running.queue_position);

        // AI 做好了：抓回後端存檔，回傳後端的網址
        _api.FakeAi.CompleteNpcJob(_api.FakeAi.LastNpcTaskId, FakeWav);
        NpcSpeakResponse done = await StatusAsync(client, sent.Result.job_id);
        Assert.Equal(NpcSpeakResponse.Done, done.status);
        Assert.StartsWith("http", done.audio_url);
        Assert.EndsWith($"/audio/npc/{sent.Result.job_id}.wav", done.audio_url);
        Assert.Equal(FakeWav, await File.ReadAllBytesAsync(AudioFile(sent.Result.job_id)));

        // 同一位 NPC 唸同一段話：直接回傳存好的檔案，不再呼叫 AI
        var (_, again) = await SpeakAsync(client, new { text, npc_name = "墨先生" });
        Assert.Equal(NpcSpeakResponse.Done, again.Result.status);
        Assert.Equal(done.audio_url, again.Result.audio_url);
        Assert.Single(_api.FakeAi.Requests, r => r.Path == "/api/npc/speak");

        // 換一位 NPC 唸就是另一個語音
        var (_, other) = await SpeakAsync(client, new { text, npc_name = "霓霓" });
        Assert.Equal(NpcSpeakResponse.Processing, other.Result.status);
        Assert.NotEqual(sent.Result.job_id, other.Result.job_id);
    }

    [Fact]
    public async Task 沒帶NPC時由預設的薯光唸()
    {
        var (_, result) = await SpeakAsync(_api.ClientFor(_api.NewUserId()), new { text = UniqueText() });

        Assert.True(result.isSuccess, result.message);
        Assert.Equal("薯光", _api.FakeAi.Last("/api/npc/speak").Fields["npc_name"]);
    }

    [Fact]
    public async Task 同一段話還在做時再送出_不會重複送給AI()
    {
        HttpClient client = _api.ClientFor(_api.NewUserId());
        object body = new { text = UniqueText(), npc_name = "阿達力" };

        var (_, first) = await SpeakAsync(client, body);
        var (_, second) = await SpeakAsync(_api.ClientFor(_api.NewUserId()), body);   // 另一位隊員同時按播放

        Assert.Equal(first.Result.job_id, second.Result.job_id);
        Assert.Equal(NpcSpeakResponse.Processing, second.Result.status);
        Assert.Single(_api.FakeAi.Requests, r => r.Path == "/api/npc/speak");
    }

    [Theory]
    [InlineData("  ", null, "請提供要唸的文字")]
    [InlineData("有文字", "夜市阿婆", "沒有這位 NPC")]
    [InlineData("有文字", "zh-TW-YunJheNeural", "沒有這位 NPC")]
    public async Task 沒有文字或NPC不在名單上_回400_不呼叫AI(string text, string npcName, string message)
    {
        var (code, result) = await SpeakAsync(_api.ClientFor(_api.NewUserId()), new { text, npc_name = npcName });

        Assert.Equal(HttpStatusCode.BadRequest, code);
        Assert.False(result.isSuccess);
        Assert.Contains(message, result.message);
        Assert.Null(_api.FakeAi.Last("/api/npc/speak"));
    }

    [Fact]
    public async Task 超過1000字回400()
    {
        var (code, _) = await SpeakAsync(_api.ClientFor(_api.NewUserId()), new { text = new string('字', 1001) });

        Assert.Equal(HttpStatusCode.BadRequest, code);
        Assert.Null(_api.FakeAi.Last("/api/npc/speak"));
    }

    [Fact]
    public async Task AI生成失敗回傳failed_再按一次會重新送出()
    {
        HttpClient client = _api.ClientFor(_api.NewUserId());
        object body = new { text = UniqueText(), npc_name = "阿吉伯" };
        var (_, sent) = await SpeakAsync(client, body);
        string firstTask = _api.FakeAi.LastNpcTaskId;
        _api.FakeAi.NpcJobs[firstTask] = FakeAiService.NpcJob(firstTask, "error", error: "任務生成失敗，請聯絡管理員並提供任務 ID");

        NpcSpeakResponse failed = await StatusAsync(client, sent.Result.job_id);

        Assert.Equal(NpcSpeakResponse.Failed, failed.status);
        Assert.Contains("任務生成失敗", failed.error);
        Assert.Null(failed.audio_url);

        var (_, retry) = await SpeakAsync(client, body);
        Assert.Equal(NpcSpeakResponse.Processing, retry.Result.status);
        Assert.Equal(sent.Result.job_id, retry.Result.job_id);
        Assert.NotEqual(firstTask, _api.FakeAi.LastNpcTaskId);
        Assert.Equal(2, _api.FakeAi.Requests.Count(r => r.Path == "/api/npc/speak"));
    }

    [Fact]
    public async Task AI查不到任務時回傳failed_請使用者重新播放()
    {
        HttpClient client = _api.ClientFor(_api.NewUserId());
        var (_, sent) = await SpeakAsync(client, new { text = UniqueText() });
        _api.FakeAi.NpcJobs.Clear();   // AI 服務重啟過

        NpcSpeakResponse failed = await StatusAsync(client, sent.Result.job_id);

        Assert.Equal(NpcSpeakResponse.Failed, failed.status);
        Assert.Contains("請重新播放", failed.error);
    }

    [Fact]
    public async Task AI任務已滿時回503()
    {
        _api.FakeAi.NpcSpeakBusy = true;
        HttpClient client = _api.ClientFor(_api.NewUserId());
        object body = new { text = UniqueText() };

        var (code, result) = await SpeakAsync(client, body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, code);
        Assert.Contains("稍後再試", result.message);

        // AI 空出來後可以再送
        _api.FakeAi.NpcSpeakBusy = false;
        var (_, retry) = await SpeakAsync(client, body);
        Assert.Equal(NpcSpeakResponse.Processing, retry.Result.status);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]   // 格式對但沒送出過（例如後端重啟過）
    [InlineData("..%2F..%2Fappsettings")]
    public async Task 查不到的job_id回404(string jobId)
    {
        HttpResponseMessage response = await _api.ClientFor(_api.NewUserId()).GetAsync($"/api/Npc/Speak/{jobId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 劇情前傳語音_由劇本的NPC唸前傳()
    {
        int user = _api.NewUserId();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, "台北101");
        string prologue = UniqueText("舊城的鐘聲已經停了很久");
        await _api.ExecuteAsync(@"
            UPDATE story SET story_prologue = @prologue, npc_id = (SELECT npc_id FROM npc WHERE npc_name = '珍奶奶')
            WHERE s_id = @storyId;", new { prologue, storyId });
        HttpClient client = _api.ClientFor(user);

        ApiResult<NpcSpeakResponse> result = await (await client.PostAsync($"/api/Npc/Prologue/{storyId}", null)).ReadResultAsync<NpcSpeakResponse>();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal(NpcSpeakResponse.Processing, result.Result.status);
        FakeAiService.Received ai = _api.FakeAi.Last("/api/npc/speak");
        Assert.Equal(prologue, ai.Fields["text"]);
        Assert.Equal("珍奶奶", ai.Fields["npc_name"]);

        // 查進度跟 Speak 共用同一支
        _api.FakeAi.CompleteNpcJob(_api.FakeAi.LastNpcTaskId, FakeWav);
        Assert.Equal(NpcSpeakResponse.Done, (await StatusAsync(client, result.Result.job_id)).status);

        // 也可以指定別的 NPC 唸
        await client.PostAsync($"/api/Npc/Prologue/{storyId}?npc_name=墨先生", null);
        Assert.Equal("墨先生", _api.FakeAi.Last("/api/npc/speak").Fields["npc_name"]);
    }

    [Fact]
    public async Task 劇情前傳_不存在的劇本回404()
    {
        HttpResponseMessage response = await _api.ClientFor(_api.NewUserId()).PostAsync("/api/Npc/Prologue/999999", null);
        ApiResult<string> result = await response.ReadResultAsync<string>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("找不到此劇本", result.message);
    }
}
