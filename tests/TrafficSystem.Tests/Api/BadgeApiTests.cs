// 勳章 API：圖鑑、完成劇本後抽勳章（一個劇本只能抽一枚）
using System.Net;
using System.Net.Http.Json;
using backend.Models;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
public class BadgeApiTests
{
    private readonly ApiFactory _api;

    public BadgeApiTests(ApiFactory api) => _api = api;

    private static async Task<ApiResult<BadgeDrawResponse>> DrawAsync(HttpClient client, int storyId) =>
        await (await client.PostAsJsonAsync("/api/Badge/Draw", new { story_id = storyId })).ReadResultAsync<BadgeDrawResponse>();

    /// <summary>用 API 走一次「開始遊玩 → 結束劇本」</summary>
    private static async Task PlayAndFinishAsync(HttpClient client, int storyId)
    {
        HttpResponseMessage confirm = await client.PostAsJsonAsync("/api/Story/Confirm", new { story_id = storyId });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        HttpResponseMessage end = await client.PostAsJsonAsync("/api/Story/EndStory", new { story_id = storyId });
        Assert.True((await end.ReadResultAsync()).isSuccess);
    }

    [Fact]
    public async Task 勳章圖鑑回傳六大類共70枚_新玩家都還沒擁有()
    {
        HttpResponseMessage response = await _api.ClientFor(_api.NewUserId()).GetAsync("/api/Badge/Status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<BadgeSeriesGroup> groups = (await response.ReadResultAsync<List<BadgeSeriesGroup>>()).Result;
        Assert.Equal(6, groups.Count);
        Assert.Equal(70, groups.Sum(g => g.badges.Count));
        Assert.DoesNotContain(groups.SelectMany(g => g.badges), b => b.is_owned);
    }

    [Fact]
    public async Task 劇本還沒完成不能抽勳章()
    {
        int user = _api.NewUserId();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, "台北101");

        HttpResponseMessage response = await _api.ClientFor(user).PostAsJsonAsync("/api/Badge/Draw", new { story_id = storyId });

        ApiResult<BadgeDrawResponse> result = await response.ReadResultAsync<BadgeDrawResponse>();
        Assert.False(result.isSuccess);
        Assert.Equal("完成這個劇本後才能抽勳章", result.message);
    }

    [Fact]
    public async Task 開始遊玩_結束劇本_抽勳章_重抽拿到同一枚_圖鑑與首頁同步更新()
    {
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, "台北101", "國立中正紀念堂", "西門町");

        await PlayAndFinishAsync(client, storyId);

        // 第一次抽：從「台北」城市勳章或路線經過的地標裡抽一枚
        BadgeDrawResponse first = (await DrawAsync(client, storyId)).Result;
        Assert.True(first.is_new);
        Assert.Equal(new[] { "島嶼城市", "台灣印記" }, first.categories);
        Assert.Contains(first.badge.b_thing, new[] { "台北", "台北101", "中正紀念堂" });

        // 再抽一次：一個劇本只能抽一枚，回傳同一枚
        BadgeDrawResponse second = (await DrawAsync(client, storyId)).Result;
        Assert.False(second.is_new);
        Assert.Equal(first.badge.b_id, second.badge.b_id);

        // 圖鑑：只有抽到的那枚變成已擁有
        List<BadgeSeriesGroup> groups = (await (await client.GetAsync("/api/Badge/Status")).ReadResultAsync<List<BadgeSeriesGroup>>()).Result;
        Assert.Equal(new[] { first.badge.b_id }, groups.SelectMany(g => g.badges).Where(b => b.is_owned).Select(b => b.b_id));

        // 首頁總覽：完成 1 個劇本、1 枚勳章
        HomeOverviewResponse overview = (await (await client.GetAsync("/api/Home/Overview")).ReadResultAsync<HomeOverviewResponse>()).Result;
        Assert.Equal(1, overview.ho_completed_story);
        Assert.Equal(1, overview.ho_badge_count);
    }

    [Fact]
    public async Task 夜間劇本完成後可以抽午夜台灣()
    {
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);
        int storyId = await _api.CreateStoryAsync(user, "臺中市", night: true, "某個廣場", "某座公園");

        await PlayAndFinishAsync(client, storyId);
        BadgeDrawResponse result = (await DrawAsync(client, storyId)).Result;

        Assert.Equal(new[] { "島嶼城市", "午夜台灣" }, result.categories);
        Assert.Contains(result.badge.b_fication, result.categories);
    }
}
