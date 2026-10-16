// 現在揪出發（POST /api/Story/GenerateGameStory）：規劃好景點、每份劇本各自送給 AI 後先回 job_id，
// 前端輪詢 GET /api/Story/GenerateGameStory/{job_id}，後端每次查詢向 AI（/api/jobs/{task_id}）問一次每份的進度，全部做完才在那次查詢存檔。
// 送出那一步要用 Valhalla 規劃行程，這裡直接在資料庫建好送出後的工作（story_game_job），測輪詢與存檔
using System.Net;
using System.Text.Json;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("劇本", "API 整合測試")]
[AllureBddHierarchy("劇本", "API 整合測試")]
public class GameStoryJobApiTests
{
    private readonly ApiFactory _api;

    public GameStoryJobApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
    }

    /// <summary>一份劇本的兩站：第一站白天、規劃了創意攝影＋協作解謎；第二站天黑後才到、另外加跨關集結</summary>
    private record Story(string TaskId, string PlaceA, string PlaceB);

    private static Story NewStory() => new($"task-{Guid.NewGuid():N}", $"uid-a-{Guid.NewGuid():N}", $"uid-b-{Guid.NewGuid():N}");

    private static object Place(string placeId, string name, params int[] types) => new
    {
        place_id = placeId, p_name = name, is_hotel = 2, is_hidden = 2,
        type_list = types.Select(id => new { type_id = id, type_name = "" }).ToArray()
    };

    /// <summary>直接建一筆「已送給 AI」的工作（跟 StoryService 存進 plan_json 的格式相同），回傳 job_id</summary>
    private async Task<string> CreateJobAsync(int user, params Story[] stories)
    {
        string jobId = Guid.NewGuid().ToString("N");
        var plan = new
        {
            party_size = 2,
            preferences = new[] { "美食" },
            transport = new[] { "步行" },
            plan_bus_transit = false,
            merchant_questions = new Dictionary<string, object>(),
            stories = stories.Select((s, i) => new
            {
                story_no = i + 1,
                task_id = s.TaskId,
                error = (string)null,
                request = new
                {
                    city_name = "臺中市", district_name = "西區", party_size = 2, s_tag = new[] { "美食" }, is_night_mode = 2, nt_name = "溫情走心",
                    places = new[] { Place(s.PlaceA, "審計新村", 3, 5), Place(s.PlaceB, "忠信市場", 3, 2, 5) }
                },
                stops = new[]
                {
                    new { place_id = s.PlaceA, arrival = new DateTime(2026, 10, 16, 16, 20, 0), is_night = false },
                    new { place_id = s.PlaceB, arrival = new DateTime(2026, 10, 16, 19, 30, 0), is_night = true }
                }
            }).ToArray()
        };

        await _api.ExecuteAsync("INSERT INTO story_game_job (job_id, au_id, status, plan_json) VALUES (@jobId, @user, 1, @plan);",
            new { jobId, user, plan = JsonSerializer.Serialize(plan) });
        return jobId;
    }

    /// <summary>AI 生成好的一份劇本（/api/stories/tasks 的結果），AI 多給了一題沒規劃的文化問答型（6）</summary>
    private static object Generated(Story s, string title) => new
    {
        story = new { story_title = title, story_prologue = "前傳", story_synopsis = "簡介", story_badge = Array.Empty<string>() },
        npc = new { name = "墨先生", role = "說書人", intro = "我是墨先生" },
        nodes = new[] { s.PlaceA, s.PlaceB }.Select((placeId, i) => new
        {
            place_id = placeId, sn_order = i + 1, sn_title = $"第{i + 1}站", location_codename = "代號",
            sn_opening_text = "開場", sn_success_text = "成功", dialogues = new { opening = "開場", success = "成功" },
            tasks = new object[]
            {
                new { task_type = 3, task_describe = "拍一張照", task_hint = "提示" },
                new { task_type = 5, task_describe = "合力解謎", task_hint = "提示", correct_answer = "答案", task_clue = new[] { new { seat_no = 1, clue_text = "線索一" }, new { seat_no = 2, clue_text = "線索二" } } },
                new { task_type = 6, task_describe = "沒規劃的題型", task_hint = "提示", task_option = new[] { new { option_key = "A", option_context = "選項", is_correct = 1 } } }
            }
        }).ToArray()
    };

    private static async Task<ApiResult<GameStoryJobResult>> PollAsync(HttpClient client, string jobId) =>
        await (await client.GetAsync("/api/Story/GenerateGameStory/" + jobId)).ReadResultAsync<GameStoryJobResult>();

    private int JobPolls(Story s) => _api.FakeAi.Requests.Count(r => r.Path == "/api/jobs/" + s.TaskId);

    [Fact]
    public async Task 輪詢到每份都做完才存檔_之後再查回傳同一批劇本不會重複存()
    {
        Story first = NewStory(), second = NewStory();
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);
        string jobId = await CreateJobAsync(user, first, second);

        _api.FakeAi.StoryJobs[first.TaskId] = FakeAiService.StoryJob(first.TaskId, "running");
        _api.FakeAi.StoryJobs[second.TaskId] = FakeAiService.StoryJob(second.TaskId, "queued");
        ApiResult<GameStoryJobResult> waiting = await PollAsync(client, jobId);
        Assert.True(waiting.isSuccess, waiting.message);
        Assert.Equal("processing", waiting.Result.status);
        Assert.Equal(2, waiting.Result.total_count);
        Assert.Equal(0, waiting.Result.finished_count);
        Assert.Null(waiting.Result.stories);

        _api.FakeAi.StoryJobs[first.TaskId] = FakeAiService.StoryJob(first.TaskId, "done", Generated(first, "第一份"));
        _api.FakeAi.StoryJobs[second.TaskId] = FakeAiService.StoryJob(second.TaskId, "running");
        ApiResult<GameStoryJobResult> half = await PollAsync(client, jobId);
        Assert.Equal("processing", half.Result.status);
        Assert.Equal(1, half.Result.finished_count);
        Assert.Equal("劇本生成中（已完成 1/2 份）", half.Result.stage);
        Assert.Equal(0, await _api.QueryAsync<int>("SELECT COUNT(*) FROM story WHERE au_id = @user;", new { user }));

        _api.FakeAi.StoryJobs[second.TaskId] = FakeAiService.StoryJob(second.TaskId, "done", Generated(second, "第二份"));
        ApiResult<GameStoryJobResult> done = await PollAsync(client, jobId);
        Assert.True(done.isSuccess, done.message);
        Assert.Equal("done", done.Result.status);
        Assert.Equal("劇本生成成功，共 2 份劇本", done.message);
        Assert.Equal(new[] { "第一份", "第二份" }, done.Result.stories.Select(s => s.story.story_title));
        Assert.Equal(new[] { 1, 2 }, done.Result.stories.Select(s => s.story_no));
        Assert.All(done.Result.stories, s => Assert.True(s.story_id > 0));

        GameStoryResult story = done.Result.stories[0];
        int npcId = await _api.QueryAsync<int>("SELECT npc_id FROM npc WHERE npc_name = '墨先生';");
        Assert.Equal(npcId, story.story.npc.npc_id);   // AI 挑的 NPC 存進劇本
        Assert.Equal(npcId, await _api.QueryAsync<int>("SELECT npc_id FROM story WHERE s_id = @id;", new { id = story.story_id }));
        Assert.Equal(0, story.story.is_night_mode);   // 兩站只有一站天黑後才到，沒有過半，不算夜間劇本
        Assert.Equal(new[] { 0, 1 }, story.nodes.Select(n => n.is_night_only));
        Assert.Equal(new DateTime(2026, 10, 16, 19, 30, 0), story.nodes[1].estimated_arrival);
        Assert.Equal("審計新村", story.nodes[0].p_name);

        // 只留規劃好的題型（AI 多給的文化問答型丟掉），答案、提示、線索不回傳給前端
        Assert.Equal(new[] { 3, 5 }, story.nodes[0].tasks.Select(t => t.task_type));
        Assert.All(story.nodes.SelectMany(n => n.tasks), t =>
        {
            Assert.True(t.task_id > 0);
            Assert.Null(t.task_hint);
            Assert.Null(t.correct_answer);
            Assert.Null(t.task_clue);
        });
        Assert.Equal(2, await _api.QueryAsync<int>(
            "SELECT COUNT(*) FROM task_clue c JOIN task t ON t.task_id = c.task_id WHERE t.story_id = @id AND t.node_id = @node;",
            new { id = story.story_id, node = story.nodes[0].sn_id }));   // 線索有存進資料庫

        int polls = JobPolls(first) + JobPolls(second);
        ApiResult<GameStoryJobResult> again = await PollAsync(client, jobId);
        Assert.Equal("done", again.Result.status);
        Assert.Equal(done.Result.stories.Select(s => s.story_id), again.Result.stories.Select(s => s.story_id));
        Assert.Null(again.Result.stories[0].nodes[0].tasks[1].correct_answer);
        Assert.Equal(2, await _api.QueryAsync<int>("SELECT COUNT(*) FROM story WHERE au_id = @user;", new { user }));
        Assert.Equal(polls, JobPolls(first) + JobPolls(second));   // 存好之後不用再問 AI
    }

    [Fact]
    public async Task 某一份AI生成失敗_只存做好的那幾份()
    {
        Story ok = NewStory(), broken = NewStory();
        int user = _api.NewUserId();
        string jobId = await CreateJobAsync(user, ok, broken);
        _api.FakeAi.StoryJobs[ok.TaskId] = FakeAiService.StoryJob(ok.TaskId, "done", Generated(ok, "做好的"));
        _api.FakeAi.StoryJobs[broken.TaskId] = FakeAiService.StoryJob(broken.TaskId, "error", error: "任務生成失敗，請聯絡管理員並提供任務 ID");

        ApiResult<GameStoryJobResult> result = await PollAsync(_api.ClientFor(user), jobId);

        Assert.Equal("done", result.Result.status);
        Assert.Equal(2, result.Result.total_count);
        Assert.Equal("做好的", Assert.Single(result.Result.stories).story.story_title);
        Assert.Equal(1, await _api.QueryAsync<int>("SELECT COUNT(*) FROM story WHERE au_id = @user;", new { user }));
    }

    [Fact]
    public async Task 每份都失敗時回傳失敗原因_再查還是失敗()
    {
        Story broken = NewStory(), lost = NewStory();   // lost 不在假 AI 的任務清單裡：AI 查不到（回 404）
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);
        string jobId = await CreateJobAsync(user, broken, lost);
        _api.FakeAi.StoryJobs[broken.TaskId] = FakeAiService.StoryJob(broken.TaskId, "error", error: "任務生成失敗");

        ApiResult<GameStoryJobResult> result = await PollAsync(client, jobId);

        Assert.True(result.isSuccess, result.message);
        Assert.Equal("failed", result.Result.status);
        Assert.Contains("AI 劇本任務生成失敗（處理失敗）：任務生成失敗", result.Result.error);
        Assert.Contains("AI 服務找不到劇本生成任務", result.Result.error);
        Assert.Equal(result.Result.error, result.message);
        Assert.Equal("failed", (await PollAsync(client, jobId)).Result.status);
        Assert.Equal(0, await _api.QueryAsync<int>("SELECT COUNT(*) FROM story WHERE au_id = @user;", new { user }));
    }

    [Fact]
    public async Task 超過10分鐘_做好的幾份先存起來_沒做完的不存()
    {
        Story ok = NewStory(), slow = NewStory();
        int user = _api.NewUserId();
        string jobId = await CreateJobAsync(user, ok, slow);
        await _api.ExecuteAsync("UPDATE story_game_job SET created_at = NOW() - INTERVAL 11 MINUTE WHERE job_id = @jobId;", new { jobId });
        _api.FakeAi.StoryJobs[ok.TaskId] = FakeAiService.StoryJob(ok.TaskId, "done", Generated(ok, "做好的"));
        _api.FakeAi.StoryJobs[slow.TaskId] = FakeAiService.StoryJob(slow.TaskId, "running");

        ApiResult<GameStoryJobResult> result = await PollAsync(_api.ClientFor(user), jobId);

        Assert.Equal("done", result.Result.status);
        Assert.Single(result.Result.stories);
    }

    [Fact]
    public async Task 超過10分鐘一份都沒做完就算失敗()
    {
        Story slow = NewStory();
        int user = _api.NewUserId();
        string jobId = await CreateJobAsync(user, slow);
        await _api.ExecuteAsync("UPDATE story_game_job SET created_at = NOW() - INTERVAL 11 MINUTE WHERE job_id = @jobId;", new { jobId });
        _api.FakeAi.StoryJobs[slow.TaskId] = FakeAiService.StoryJob(slow.TaskId, "queued");

        ApiResult<GameStoryJobResult> result = await PollAsync(_api.ClientFor(user), jobId);

        Assert.Equal("failed", result.Result.status);
        Assert.Contains("超過 10 分鐘", result.Result.error);
    }

    [Fact]
    public async Task 存檔中斷後接手_只做最後整理不會重複存劇本()
    {
        Story s = NewStory();
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);
        string jobId = await CreateJobAsync(user, s);
        _api.FakeAi.StoryJobs[s.TaskId] = FakeAiService.StoryJob(s.TaskId, "done", Generated(s, "只存一次"));
        int storyId = (await PollAsync(client, jobId)).Result.stories[0].story_id;

        // 模擬劇本寫進資料庫後、整理完之前後端重啟：工作卡在「存檔中」超過 5 分鐘
        await _api.ExecuteAsync("UPDATE story_game_job SET status = 2, updated_at = NOW() - INTERVAL 6 MINUTE WHERE job_id = @jobId;", new { jobId });
        _api.FakeAi.StoryJobs.Clear();   // AI 也重啟過，查不到任務

        ApiResult<GameStoryJobResult> result = await PollAsync(client, jobId);

        Assert.Equal("done", result.Result.status);
        Assert.Equal(storyId, Assert.Single(result.Result.stories).story_id);
        Assert.Equal(1, await _api.QueryAsync<int>("SELECT COUNT(*) FROM story WHERE au_id = @user;", new { user }));
    }

    [Fact]
    public async Task 別人的job_id查不到進度()
    {
        Story s = NewStory();
        string jobId = await CreateJobAsync(_api.NewUserId(), s);

        HttpResponseMessage response = await _api.ClientFor(_api.NewUserId()).GetAsync("/api/Story/GenerateGameStory/" + jobId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await response.ReadResultAsync<string>()).isSuccess);
    }
}
