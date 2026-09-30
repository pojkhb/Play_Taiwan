// 劇本回顧：GET /api/History/{story_id}/Recap、POST /api/History/{story_id}/Recap/Narration
using System.Net;
using System.Net.Http.Json;
using backend.Models;
using backend.Services;
using backend.utils;
using backend.ViewModels;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
public class StoryRecapApiTests
{
    private readonly ApiFactory _api;

    public StoryRecapApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
    }

    /// <summary>
    /// 建立一個玩完的 3 站劇本：每站有照片與完成劇情、第 1 站有兩題（問答答對、拍照），第 2 站一題答錯，
    /// 並拿到一張明信片、抽到一枚勳章。回傳 (story_id, 景點名稱)
    /// </summary>
    private async Task<(int storyId, string[] places)> CreateFinishedStoryAsync(int user)
    {
        string[] places = Enumerable.Range(1, 3).Select(i => $"回顧景點{i}_{Guid.NewGuid():N}"[..14]).ToArray();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: true, places);
        await _api.ExecuteAsync("UPDATE story SET story_title = '捷運迷城的懸案' WHERE s_id = @storyId;", new { storyId });
        await _api.ExecuteAsync(@"
            UPDATE story_node SET sn_success_text = CONCAT('第', sn_order, '站的謎題解開了。線索指向下一站。')
            WHERE s_id = @storyId;", new { storyId });
        foreach (string place in places)
            await _api.GivePlacePhotoAsync(place);

        int[] nodes = (await QueryIntsAsync("SELECT sn_id FROM story_node WHERE s_id = @storyId ORDER BY sn_order;", new { storyId })).ToArray();
        int quiz = await InsertTaskAsync(storyId, nodes[0], 6, "這座建築是哪一年落成？");
        int photo = await InsertTaskAsync(storyId, nodes[0], 3, "和建築的圓頂合照");
        int wrong = await InsertTaskAsync(storyId, nodes[1], 6, "這條街以前叫什麼？");
        await InsertTaskAsync(storyId, nodes[2], 6, "沒作答的題目");

        await InsertRecordAsync(user, quiz, isCorrect: 1, media: null);
        int photoRecord = await InsertRecordAsync(user, photo, isCorrect: null, media: "https://img.test/selfie.jpg");
        await _api.ExecuteAsync("INSERT INTO record_media (media_id, record_id, media_url) VALUES (@id, @photoRecord, 'https://img.test/dome.png'), (@id2, @photoRecord, 'https://img.test/clip.mp4');",
            new { id = _api.NewUserId(), id2 = _api.NewUserId(), photoRecord });
        await InsertRecordAsync(user, wrong, isCorrect: 0, media: null);

        await _api.ExecuteAsync(@"
            INSERT INTO story_session (au_id, s_id, ss_current, ss_status, started_at, completed_at)
            VALUES (@user, @storyId, 3, 'completed', NOW() - INTERVAL 150 MINUTE, NOW());
            INSERT INTO postcard (au_id, s_id, sn_id, p_name, p_imag_url, is_night) VALUES (@user, @storyId, @node, '夜色明信片', 'https://img.test/card.png', 1);
            INSERT INTO au_badge (au_id, b_id, s_id) VALUES (@user, 1, @storyId);",
            new { user, storyId, node = nodes[0] });
        return (storyId, places);
    }

    private async Task<List<int>> QueryIntsAsync(string sql, object param)
    {
        using var conn = new MySql.Data.MySqlClient.MySqlConnection(_api.ConnectionString);
        return (await Dapper.SqlMapper.QueryAsync<int>(conn, sql, param)).ToList();
    }

    private async Task<int> InsertTaskAsync(int storyId, int nodeId, int type, string describe)
    {
        await _api.ExecuteAsync("INSERT INTO task (story_id, node_id, task_type, task_describe) VALUES (@storyId, @nodeId, @type, @describe);",
            new { storyId, nodeId, type, describe });
        return await _api.QueryAsync<int>("SELECT MAX(task_id) FROM task WHERE story_id = @storyId;", new { storyId });
    }

    private async Task<int> InsertRecordAsync(int user, int taskId, int? isCorrect, string media)
    {
        int recordId = _api.NewUserId();   // record_id 不是自動編號，借用遞增的號碼
        await _api.ExecuteAsync(@"
            INSERT INTO user_task_record (record_id, au_id, task_id, is_correct, answer_media_url)
            VALUES (@recordId, @user, @taskId, @isCorrect, @media);", new { recordId, user, taskId, isCorrect, media });
        return recordId;
    }

    private async Task<StoryRecapResponse> RecapAsync(int user, int storyId) =>
        (await (await _api.ClientFor(user).GetAsync($"/api/History/{storyId}/Recap")).ReadResultAsync<StoryRecapResponse>()).Result;

    [Fact]
    public async Task 玩完的劇本可以看完整回顧()
    {
        int user = _api.NewUserId();
        var (storyId, places) = await CreateFinishedStoryAsync(user);

        StoryRecapResponse recap = await RecapAsync(user, storyId);

        Assert.Equal("捷運迷城的懸案", recap.title);
        Assert.Equal("前情提要", recap.prologue);
        Assert.True(recap.is_night_mode);
        Assert.InRange(recap.play_minutes.Value, 149, 151);

        // 每一站：真正的景點、照片、劇情（迷霧已散開）
        Assert.Equal(places, recap.nodes.Select(n => n.place_name));
        Assert.Equal(new[] { "第1站", "第2站", "第3站" }, recap.nodes.Select(n => n.chapter_title));
        Assert.All(recap.nodes, n => Assert.StartsWith("https://img.test/", n.place_image_url));
        Assert.Equal("第1站的謎題解開了。線索指向下一站。", recap.nodes[0].success_text);

        // 玩家拍的照片：只有圖片（影片不算），依拍攝的站分組
        Assert.Equal(new[] { "https://img.test/selfie.jpg", "https://img.test/dome.png" }, recap.nodes[0].photos.OrderByDescending(p => p.EndsWith(".jpg")));
        Assert.Empty(recap.nodes[1].photos);

        // 作答結果：答對、沒有對錯（拍照）、答錯、沒作答
        Assert.Equal(new bool?[] { true, null }, recap.nodes[0].tasks.Select(t => t.is_correct));
        Assert.Equal("文化問答型", recap.nodes[0].tasks[0].type_name);
        Assert.Equal("這座建築是哪一年落成？", recap.nodes[0].tasks[0].question);
        Assert.False(recap.nodes[1].tasks.Single().is_correct);
        Assert.False(recap.nodes[2].tasks.Single().answered);

        Assert.Equal(3, recap.stats.node_count);
        Assert.Equal(3, recap.stats.tasks_answered);
        Assert.Equal(1, recap.stats.tasks_correct);
        Assert.Equal(2, recap.stats.photo_count);
        Assert.Equal(1, recap.stats.postcard_count);

        StoryRecapPostcard card = Assert.Single(recap.postcards);
        Assert.True(card.is_night_edition);
        Assert.Equal(1, recap.badge.badge_id);
        Assert.Null(recap.vlog);

        // 沒有 Vlog：旁白依各站劇情組成，語音還沒產生
        Assert.Equal("story", recap.narration.source);
        Assert.StartsWith("「捷運迷城的懸案」旅程回顧。第1站，" + places[0], recap.narration.text);
        Assert.Null(recap.narration.audio_url);
    }

    [Fact]
    public async Task 有Vlog時回顧帶影片_旁白用玩家確認過的版本()
    {
        int user = _api.NewUserId();
        var (storyId, _) = await CreateFinishedStoryAsync(user);
        await _api.ExecuteAsync(@"
            INSERT INTO au_vlog (au_id, s_id, av_final_script, av_video_url, av_vlog_status)
            VALUES (@user, @storyId, '這是我確認過的旁白。', 'https://video.test/final.mp4', 3);", new { user, storyId });

        StoryRecapResponse recap = await RecapAsync(user, storyId);

        Assert.Equal("https://video.test/final.mp4", recap.vlog.video_url);
        Assert.Equal(3, recap.vlog.status);
        Assert.Equal("vlog", recap.narration.source);
        Assert.Equal("這是我確認過的旁白。", recap.narration.text);
    }

    [Fact]
    public async Task 按播放時產生旁白語音_之後直接用存好的檔案()
    {
        int user = _api.NewUserId();
        var (storyId, _) = await CreateFinishedStoryAsync(user);
        _api.FakeAi.Images[AiServiceConfig.Url("/api/download/npc_npc-001.mp3")] = new byte[] { 0x49, 0x44, 0x33, 1, 2, 3 };   // 假的 mp3
        HttpClient client = _api.ClientFor(user);

        HttpResponseMessage first = await client.PostAsync($"/api/History/{storyId}/Recap/Narration", null);
        StoryRecapNarration narration = (await first.ReadResultAsync<StoryRecapNarration>()).Result;

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        string key = StoryRecapService.NarrationKey(NpcVoiceService.DefaultVoice, narration.text);
        Assert.EndsWith($"/audio/narration/{key}.mp3", narration.audio_url);
        Assert.True(File.Exists(Path.Combine(_api.FogRoot, "audio", "narration", key + ".mp3")));
        Assert.Equal(narration.text, _api.FakeAi.Last("/api/npc/speak").Fields["text"]);

        // 第二次直接回傳存好的檔案，不再呼叫 AI；回顧也會帶出語音網址
        await client.PostAsync($"/api/History/{storyId}/Recap/Narration", null);
        Assert.Single(_api.FakeAi.Requests, r => r.Path == "/api/npc/speak");
        Assert.Equal(narration.audio_url, (await RecapAsync(user, storyId)).narration.audio_url);
    }

    [Fact]
    public async Task 還沒玩完或別人的劇本不能回顧_不存在回傳404()
    {
        int owner = _api.NewUserId();
        var (storyId, _) = await CreateFinishedStoryAsync(owner);
        int playing = await _api.CreateStoryAsync(owner, "臺北市", night: false, $"進行中{Guid.NewGuid():N}"[..12]);

        Assert.Equal(HttpStatusCode.BadRequest, (await _api.ClientFor(owner).GetAsync($"/api/History/{playing}/Recap")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.ClientFor(_api.NewUserId()).GetAsync($"/api/History/{storyId}/Recap")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.ClientFor(owner).GetAsync("/api/History/999999/Recap")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.ClientFor(owner).PostAsync($"/api/History/{playing}/Recap/Narration", null)).StatusCode);
    }

    [Fact]
    public async Task 過往旅途的景點清單是真正的景點名稱()
    {
        int user = _api.NewUserId();
        var (storyId, places) = await CreateFinishedStoryAsync(user);

        HistoryStoryItem detail = (await (await _api.ClientFor(user).GetAsync($"/api/History/{storyId}")).ReadResultAsync<HistoryStoryItem>()).Result;

        Assert.Equal(places, detail.spots);
    }
}
