// NPC 名單：薯光、珍奶奶、阿達力、墨先生、霓霓、阿吉伯（seed_reference.sql）。
// AI 生成劇本時從名單挑一位，劇本詳情、地圖節點、NPC 互動、提示對話框都顯示這位 NPC；沒有指定時用預設的薯光
using System.Net;
using System.Net.Http.Json;
using backend.Controllers;
using backend.dao;
using backend.Models;
using backend.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
public class StoryNpcApiTests
{
    private readonly ApiFactory _api;

    public StoryNpcApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
        StoryController.TextBlueprintPollInterval = TimeSpan.FromMilliseconds(20);
    }

    private Task<int> NpcIdAsync(string name) =>
        _api.QueryAsync<int>("SELECT npc_id FROM npc WHERE npc_name = @name;", new { name });

    /// <summary>建立劇本並指定 NPC（只掛在劇本上，節點沒有自己的 NPC），回傳 (story_id, 第一站 node_id)</summary>
    private async Task<(int storyId, int firstNode)> CreateStoryWithNpcAsync(int user, string npcName)
    {
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, "台北101", "西門町");
        if (npcName != null)
            await _api.ExecuteAsync("UPDATE story SET npc_id = @npcId WHERE s_id = @storyId;", new { npcId = await NpcIdAsync(npcName), storyId });
        int firstNode = await _api.QueryAsync<int>("SELECT sn_id FROM story_node WHERE s_id = @storyId ORDER BY sn_order LIMIT 1;", new { storyId });
        return (storyId, firstNode);
    }

    [Fact]
    public async Task 名單有六位NPC_薯光是預設_每位都有圖片可以下載()
    {
        Assert.Equal(6, await _api.QueryAsync<int>("SELECT COUNT(*) FROM npc;"));
        Assert.Equal("薯光", await _api.QueryAsync<string>("SELECT npc_name FROM npc WHERE is_default = 1;"));

        foreach (string avatar in new[] { "shuguang", "zhen-nainai", "a-da-li", "mo-xian-sheng", "ni-ni", "a-ji-bo" })
        {
            Assert.Equal(1, await _api.QueryAsync<int>("SELECT COUNT(*) FROM npc WHERE npc_avatar = @path;", new { path = $"/images/npc/{avatar}.png" }));
            HttpResponseMessage image = await _api.CreateClient().GetAsync($"/images/npc/{avatar}.png");
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
            Assert.Equal("image/png", image.Content.Headers.ContentType.MediaType);
        }
    }

    [Fact]
    public async Task 劇本指定NPC時_互動_節點詳情_劇本詳情都顯示這位NPC()
    {
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);
        var (storyId, firstNode) = await CreateStoryWithNpcAsync(user, "墨先生");

        NpcInteractionResponse interaction =
            (await (await client.GetAsync($"/api/Map/Node/{firstNode}/Interact")).ReadResultAsync<NpcInteractionResponse>()).Result;
        Assert.Equal((await NpcIdAsync("墨先生")).ToString(), interaction.npc_id);
        Assert.Equal("墨先生", interaction.npc_name);
        Assert.Equal("博學嚴謹的文史工作者，擅長解讀古地圖與歷史檔案", interaction.npc_role);
        Assert.StartsWith("http", interaction.npc_avatar_url);
        Assert.EndsWith("/images/npc/mo-xian-sheng.png", interaction.npc_avatar_url);
        Assert.Equal("zh-TW-YunJheNeural", interaction.npc_voice);
        Assert.Equal("開場", interaction.npc_dialogue);   // 台詞是這一站的開場白

        NodeDetailResponse node = (await (await client.GetAsync($"/api/Map/Node/{firstNode}")).ReadResultAsync<NodeDetailResponse>()).Result;
        Assert.Equal("墨先生", node.npc_name);
        Assert.EndsWith("/images/npc/mo-xian-sheng.png", node.npc_avatar_url);

        StoryDetailResponse detail = (await (await client.GetAsync($"/api/Story/{storyId}/Detail")).ReadResultAsync<StoryDetailResponse>()).Result;
        Assert.Equal("墨先生", detail.npc.name);
        Assert.Equal("博學嚴謹的文史工作者，擅長解讀古地圖與歷史檔案", detail.npc.role);
        Assert.False(string.IsNullOrWhiteSpace(detail.npc.intro));
        Assert.StartsWith("http", detail.npc.avatar_url);
        Assert.Equal("zh-TW-YunJheNeural", detail.npc.voice);
        Assert.All(detail.nodes, n => Assert.Equal("墨先生", n.npc_name));
    }

    [Fact]
    public async Task 劇本沒有指定NPC時_用預設的薯光()
    {
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);
        var (storyId, firstNode) = await CreateStoryWithNpcAsync(user, null);

        NpcInteractionResponse interaction =
            (await (await client.GetAsync($"/api/Map/Node/{firstNode}/Interact")).ReadResultAsync<NpcInteractionResponse>()).Result;
        Assert.Equal("薯光", interaction.npc_name);
        Assert.EndsWith("/images/npc/shuguang.png", interaction.npc_avatar_url);

        StoryDetailResponse detail = (await (await client.GetAsync($"/api/Story/{storyId}/Detail")).ReadResultAsync<StoryDetailResponse>()).Result;
        Assert.Equal("薯光", detail.npc.name);
        Assert.All(detail.nodes, n => Assert.Equal("薯光", n.npc_name));
    }

    [Fact]
    public async Task 提示對話框顯示這一站NPC的圖片()
    {
        int user = _api.NewUserId();
        var (storyId, firstNode) = await CreateStoryWithNpcAsync(user, "阿吉伯");
        await _api.ExecuteAsync(@"
            INSERT INTO task (story_id, node_id, task_type, task_describe, task_hint)
            VALUES (@storyId, @firstNode, 2, '在牌樓下集合', '看看牌樓上的字');", new { storyId, firstNode });
        int taskId = await _api.QueryAsync<int>("SELECT MAX(task_id) FROM task WHERE node_id = @firstNode;", new { firstNode });

        TaskHintResponse hint = (await (await _api.ClientFor(user).GetAsync($"/api/Task/{taskId}/Hint")).ReadResultAsync<TaskHintResponse>()).Result;

        Assert.StartsWith("http", hint.npc_avatar_url);
        Assert.EndsWith("/images/npc/a-ji-bo.png", hint.npc_avatar_url);
    }

    [Fact]
    public async Task 劇情前傳語音用劇本NPC的聲線()
    {
        int user = _api.NewUserId();
        var (storyId, _) = await CreateStoryWithNpcAsync(user, "霓霓");

        HttpResponseMessage response = await _api.ClientFor(user).PostAsync($"/api/Npc/Prologue/{storyId}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("zh-TW-HsiaoYuNeural", _api.FakeAi.Last("/api/npc/speak").Fields["voice"]);
    }

    private static object CompletedWithNpc(string npcName, string place1, string place2) => new
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
            npc = new { name = npcName, role = "AI 寫的身分（不採用，以名單為準）", intro = "AI 寫的自我介紹" },
            nodes = new[]
            {
                new { node_order = 1, place_name = place1, location_codename = "白色穹頂", node_title = "穹頂下的信", task_type = "文化問答型", task_description = "找出牌樓上的字", dialogues = new { opening = "開場", success = "完成" } },
                new { node_order = 2, place_name = place2, location_codename = "紅色古剎", node_title = "古剎的香火", task_type = "創意攝影型", task_description = "拍一張香爐", dialogues = new { opening = "開場", success = "完成" } },
            }
        }
    };

    [Theory]
    [InlineData("墨先生", "墨先生")]
    [InlineData("說書人阿明", "薯光")]   // 名字不在名單上 → 預設的薯光
    public async Task 遊你說算_AI挑的NPC存進劇本_每一站都是這位NPC(string aiNpc, string expected)
    {
        _api.FakeAi.TextJobPolls.Enqueue(CompletedWithNpc(aiNpc, $"NPC測試景點A{Guid.NewGuid():N}", $"NPC測試景點B{Guid.NewGuid():N}"));
        int user = _api.NewUserId();

        ApiResult<GenerateByTextResult> result = await (await _api.ClientFor(user)
            .PostAsJsonAsync("/api/Story/GenerateByText", new { user_prompt = "兩個人想去台北中正區走走" })).ReadResultAsync<GenerateByTextResult>();
        Assert.True(result.isSuccess, result.message);
        int storyId = result.Result.story_id;

        int expectedId = await NpcIdAsync(expected);
        Assert.Equal(expectedId, await _api.QueryAsync<int?>("SELECT npc_id FROM story WHERE s_id = @storyId;", new { storyId }));
        Assert.Equal(0, await _api.QueryAsync<int>(
            "SELECT COUNT(*) FROM story_node WHERE s_id = @storyId AND (npc_id IS NULL OR npc_id <> @expectedId);", new { storyId, expectedId }));
        Assert.Equal(6, await _api.QueryAsync<int>("SELECT COUNT(*) FROM npc;"));   // 不會新增 NPC
    }

    private async Task<GameStoryResult> SaveGameStoryAsync(int user, string aiNpcName)
    {
        var result = new GameStoryResult
        {
            story_no = 1,
            story = new GameStoryInfo
            {
                city_name = "臺北市", district_name = "中正區", party_size = 2, story_title = "NPC 測試劇本", is_night_mode = 0, story_postcards = 2,
                npc = aiNpcName == null ? null : new GameStoryNpc { npc_name = aiNpcName }
            },
            nodes = new List<GameStoryNode>
            {
                new() { place_id = Guid.NewGuid().ToString(), sn_order = 1, sn_title = "第1站", sn_opening_text = "開場", tasks = new List<GameStoryTask>() },
                new() { place_id = Guid.NewGuid().ToString(), sn_order = 2, sn_title = "第2站", sn_opening_text = "開場", tasks = new List<GameStoryTask>() }
            }
        };

        using IServiceScope scope = _api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<StoryDao>().SaveGameStoriesAsync(user, new List<string>(), new List<GameStoryResult> { result }, planBusTransit: false);
        return result;
    }

    [Fact]
    public async Task 劇本任務生成_AI挑的NPC存進劇本_回傳名單上的NPC資料()
    {
        GameStoryResult saved = await SaveGameStoryAsync(_api.NewUserId(), "霓霓");

        int niniId = await NpcIdAsync("霓霓");
        Assert.Equal(niniId, saved.story.npc.npc_id);
        Assert.Equal("對美感與光影極度敏銳的街頭藝術家，專門引導光影觀察與夜遊探索", saved.story.npc.npc_role);
        Assert.Equal("/images/npc/ni-ni.png", saved.story.npc.npc_avatar_url);   // DAO 回傳站內路徑，Controller 再組成完整網址
        Assert.Equal("zh-TW-HsiaoYuNeural", saved.story.npc.npc_voice);
        Assert.All(saved.nodes, n => Assert.Equal(niniId, n.npc_id));
        Assert.Equal(niniId, await _api.QueryAsync<int?>("SELECT npc_id FROM story WHERE s_id = @id;", new { id = saved.story_id }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("夜市阿婆")]
    public async Task 劇本任務生成_AI沒挑或名字不在名單上_用預設的薯光(string aiNpcName)
    {
        GameStoryResult saved = await SaveGameStoryAsync(_api.NewUserId(), aiNpcName);

        int shuguangId = await NpcIdAsync("薯光");
        Assert.Equal("薯光", saved.story.npc.npc_name);
        Assert.All(saved.nodes, n => Assert.Equal(shuguangId, n.npc_id));
        Assert.Equal(shuguangId, await _api.QueryAsync<int?>("SELECT npc_id FROM story WHERE s_id = @id;", new { id = saved.story_id }));
    }
}
