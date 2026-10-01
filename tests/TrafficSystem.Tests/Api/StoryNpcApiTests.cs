// 劇本 NPC：AI 生成劇本時一起產生的角色，存進 npc 表；劇本詳情、地圖節點詳情與 NPC 互動都顯示這個 NPC
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

    /// <summary>給劇本一個 NPC（只掛在劇本上，節點沒有自己的 NPC），回傳 npc_id</summary>
    private async Task<int> GiveStoryNpcAsync(int storyId, string name, string role, string intro)
    {
        await _api.ExecuteAsync("INSERT INTO npc (npc_name, npc_role, npc_intro) VALUES (@name, @role, @intro);", new { name, role, intro });
        int npcId = await _api.QueryAsync<int>("SELECT MAX(npc_id) FROM npc WHERE npc_name = @name;", new { name });
        await _api.ExecuteAsync("UPDATE story SET npc_id = @npcId WHERE s_id = @storyId;", new { npcId, storyId });
        return npcId;
    }

    private async Task<int> FirstNodeIdAsync(int storyId) =>
        await _api.QueryAsync<int>("SELECT sn_id FROM story_node WHERE s_id = @storyId ORDER BY sn_order LIMIT 1;", new { storyId });

    [Fact]
    public async Task 劇本有NPC時_互動與詳情顯示NPC的名字身分和自我介紹()
    {
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, "台北101", "西門町");
        int npcId = await GiveStoryNpcAsync(storyId, "說書人阿明", "在城南說了四十年故事的老說書人", "年輕人，坐下來聽我說個故事吧。");
        int firstNode = await FirstNodeIdAsync(storyId);

        NpcInteractionResponse interaction =
            (await (await client.GetAsync($"/api/Map/Node/{firstNode}/Interact")).ReadResultAsync<NpcInteractionResponse>()).Result;
        Assert.Equal(npcId.ToString(), interaction.npc_id);
        Assert.Equal("說書人阿明", interaction.npc_name);
        Assert.Equal("在城南說了四十年故事的老說書人", interaction.npc_role);
        Assert.Equal("開場", interaction.npc_dialogue);   // 台詞是這一站的開場白

        NodeDetailResponse node = (await (await client.GetAsync($"/api/Map/Node/{firstNode}")).ReadResultAsync<NodeDetailResponse>()).Result;
        Assert.Equal("說書人阿明", node.npc_name);

        StoryDetailResponse detail = (await (await client.GetAsync($"/api/Story/{storyId}/Detail")).ReadResultAsync<StoryDetailResponse>()).Result;
        Assert.Equal("說書人阿明", detail.npc.name);
        Assert.Equal("在城南說了四十年故事的老說書人", detail.npc.role);
        Assert.Equal("年輕人，坐下來聽我說個故事吧。", detail.npc.intro);
        Assert.All(detail.nodes, n => Assert.Equal("說書人阿明", n.npc_name));
    }

    [Fact]
    public async Task 劇本沒有NPC時_互動顯示預設的旅遊引導員()
    {
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, "台北101");
        int firstNode = await FirstNodeIdAsync(storyId);

        NpcInteractionResponse interaction =
            (await (await client.GetAsync($"/api/Map/Node/{firstNode}/Interact")).ReadResultAsync<NpcInteractionResponse>()).Result;
        Assert.Equal("NPC-DEFAULT", interaction.npc_id);
        Assert.Equal(MapDao.DefaultNpcName, interaction.npc_name);
        Assert.Null(interaction.npc_role);

        StoryDetailResponse detail = (await (await client.GetAsync($"/api/Story/{storyId}/Detail")).ReadResultAsync<StoryDetailResponse>()).Result;
        Assert.Null(detail.npc);
        Assert.All(detail.nodes, n => Assert.Equal("", n.npc_name));
    }

    private static object CompletedWithNpc(string place1, string place2) => new
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
            npc = new { name = "說書人阿明", role = "在城南說了四十年故事的老說書人", intro = "年輕人，坐下來聽我說個故事吧。" },
            nodes = new[]
            {
                new { node_order = 1, place_name = place1, location_codename = "白色穹頂", node_title = "穹頂下的信", task_type = "文化問答型", task_description = "找出牌樓上的字", dialogues = new { opening = "開場", success = "完成" } },
                new { node_order = 2, place_name = place2, location_codename = "紅色古剎", node_title = "古剎的香火", task_type = "創意攝影型", task_description = "拍一張香爐", dialogues = new { opening = "開場", success = "完成" } },
            }
        }
    };

    [Fact]
    public async Task 遊你說算_AI回傳的NPC存進劇本_每一站都是這個NPC()
    {
        _api.FakeAi.TextJobPolls.Enqueue(CompletedWithNpc($"NPC測試景點A{Guid.NewGuid():N}", $"NPC測試景點B{Guid.NewGuid():N}"));
        int user = _api.NewUserId();

        ApiResult<GenerateByTextResult> result = await (await _api.ClientFor(user)
            .PostAsJsonAsync("/api/Story/GenerateByText", new { user_prompt = "兩個人想去台北中正區走走" })).ReadResultAsync<GenerateByTextResult>();
        Assert.True(result.isSuccess, result.message);
        int storyId = result.Result.story_id;

        int? npcId = await _api.QueryAsync<int?>("SELECT npc_id FROM story WHERE s_id = @storyId;", new { storyId });
        Assert.NotNull(npcId);
        Assert.Equal("說書人阿明", await _api.QueryAsync<string>("SELECT npc_name FROM npc WHERE npc_id = @npcId;", new { npcId }));
        Assert.Equal(0, await _api.QueryAsync<int>(
            "SELECT COUNT(*) FROM story_node WHERE s_id = @storyId AND (npc_id IS NULL OR npc_id <> @npcId);", new { storyId, npcId }));

        StoryDetailResponse detail = (await (await _api.ClientFor(user).GetAsync($"/api/Story/{storyId}/Detail")).ReadResultAsync<StoryDetailResponse>()).Result;
        Assert.Equal("年輕人，坐下來聽我說個故事吧。", detail.npc.intro);
    }

    private async Task<GameStoryResult> SaveGameStoryAsync(int user, GameStoryNpc npc)
    {
        var result = new GameStoryResult
        {
            story_no = 1,
            story = new GameStoryInfo { city_name = "臺北市", district_name = "中正區", party_size = 2, story_title = "NPC 測試劇本", is_night_mode = 0, story_postcards = 2, npc = npc },
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
    public async Task 劇本任務生成_AI回傳的NPC存進劇本並回傳NPC代號()
    {
        int user = _api.NewUserId();

        GameStoryResult saved = await SaveGameStoryAsync(user, new GameStoryNpc { npc_name = "夜市阿婆", npc_role = "在夜市賣了五十年粉圓的阿婆", npc_intro = "囡仔，來呷一碗粉圓再出發。" });

        Assert.NotNull(saved.story.npc.npc_id);
        Assert.All(saved.nodes, n => Assert.Equal(saved.story.npc.npc_id, n.npc_id));
        Assert.Equal(saved.story.npc.npc_id, await _api.QueryAsync<int?>("SELECT npc_id FROM story WHERE s_id = @id;", new { id = saved.story_id }));
        Assert.Equal("在夜市賣了五十年粉圓的阿婆", await _api.QueryAsync<string>("SELECT npc_role FROM npc WHERE npc_id = @id;", new { id = saved.story.npc.npc_id }));
    }

    [Fact]
    public async Task 劇本任務生成_AI沒有回NPC或名字是空的_劇本不掛NPC()
    {
        int user = _api.NewUserId();

        GameStoryResult withoutNpc = await SaveGameStoryAsync(user, null);
        GameStoryResult blankName = await SaveGameStoryAsync(user, new GameStoryNpc { npc_name = "  ", npc_role = "沒有名字的角色" });

        foreach (GameStoryResult saved in new[] { withoutNpc, blankName })
        {
            Assert.Null(saved.story.npc);
            Assert.All(saved.nodes, n => Assert.Null(n.npc_id));
            Assert.Null(await _api.QueryAsync<int?>("SELECT npc_id FROM story WHERE s_id = @id;", new { id = saved.story_id }));
        }
    }
}
