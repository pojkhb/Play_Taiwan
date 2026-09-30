// NPC 語音 API（外部 AI /api/npc/speak 由 FakeAiService 模擬）
using System.Net.Http.Json;
using backend.Services;
using backend.utils;
using backend.ViewModels;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
public class NpcApiTests
{
    private readonly ApiFactory _api;

    public NpcApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
    }

    [Fact]
    public async Task 劇情前傳語音_唸劇本的前傳並回傳mp3網址()
    {
        int user = _api.NewUserId();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, "台北101");

        ApiResult<NpcSpeakResponse> result =
            await (await _api.ClientFor(user).PostAsync($"/api/Npc/Prologue/{storyId}", null)).ReadResultAsync<NpcSpeakResponse>();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal(AiServiceConfig.Url("/api/download/npc_npc-001.mp3"), result.Result.audio_url);
        Assert.Equal(NpcVoiceService.DefaultVoice, result.Result.voice);

        FakeAiService.Received sent = _api.FakeAi.Last("/api/npc/speak");
        Assert.Equal("前情提要", sent.Fields["text"]);                    // CreateStoryAsync 寫入的前傳
        Assert.Equal(NpcVoiceService.DefaultVoice, sent.Fields["voice"]);
    }

    [Fact]
    public async Task NPC說話可以指定聲音()
    {
        var body = new { text = "探員，第一條線索就藏在公園裡。", voice = "zh-TW-YunJheNeural" };

        ApiResult<NpcSpeakResponse> result =
            await (await _api.ClientFor(_api.NewUserId()).PostAsJsonAsync("/api/Npc/Speak", body)).ReadResultAsync<NpcSpeakResponse>();

        Assert.True(result.isSuccess, result.message);
        FakeAiService.Received sent = _api.FakeAi.Last("/api/npc/speak");
        Assert.Equal(body.text, sent.Fields["text"]);
        Assert.Equal("zh-TW-YunJheNeural", sent.Fields["voice"]);
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
