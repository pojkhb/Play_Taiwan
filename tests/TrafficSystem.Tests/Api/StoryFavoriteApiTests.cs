// 喜愛的劇本：POST /api/Story/{story_id}/Favorite、GET /api/Story/Favorites
using System.Net;
using System.Net.Http.Json;
using backend.Models;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("劇本", "API 整合測試")]
[AllureBddHierarchy("劇本", "API 整合測試")]
public class StoryFavoriteApiTests
{
    private readonly ApiFactory _api;

    public StoryFavoriteApiTests(ApiFactory api) => _api = api;

    private static string Place(string name) => $"{name}{Guid.NewGuid():N}"[..14];

    private async Task<List<FavoriteStoryItem>> FavoritesAsync(int user) =>
        (await (await _api.ClientFor(user).GetAsync("/api/Story/Favorites")).ReadResultAsync<List<FavoriteStoryItem>>()).Result;

    private async Task<StoryDetailResponse> DetailAsync(int user, int storyId) =>
        (await (await _api.ClientFor(user).GetAsync($"/api/Story/{storyId}/Detail")).ReadResultAsync<StoryDetailResponse>()).Result;

    [Fact]
    public async Task 加入喜愛後出現在清單_取消後就不見()
    {
        int user = _api.NewUserId();
        string first = Place("第一站");
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: true, first, Place("第二站"));
        string cover = await _api.GivePlacePhotoAsync(first);
        HttpClient client = _api.ClientFor(user);

        Assert.Empty(await FavoritesAsync(user));
        Assert.False((await DetailAsync(user, storyId)).is_favorite);

        HttpResponseMessage add = await client.PostAsJsonAsync($"/api/Story/{storyId}/Favorite", new { is_favorite = true });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        FavoriteStoryItem item = Assert.Single(await FavoritesAsync(user));
        Assert.Equal(storyId, item.story_id);
        Assert.Equal("劇本簡介", item.synopsis);
        Assert.Equal("臺北市", item.city_name);
        Assert.Equal(2, item.node_count);
        Assert.True(item.is_night_mode);
        Assert.Equal(cover, item.cover_image_url);   // 第一站的照片當封面
        Assert.False(item.is_completed);
        Assert.True((await DetailAsync(user, storyId)).is_favorite);

        await client.PostAsJsonAsync($"/api/Story/{storyId}/Favorite", new { is_favorite = false });
        Assert.Empty(await FavoritesAsync(user));
        Assert.False((await DetailAsync(user, storyId)).is_favorite);
    }

    [Fact]
    public async Task 重複加入喜愛不會出錯()
    {
        int user = _api.NewUserId();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, Place("景點"));
        HttpClient client = _api.ClientFor(user);

        await client.PostAsJsonAsync($"/api/Story/{storyId}/Favorite", new { is_favorite = true });
        HttpResponseMessage again = await client.PostAsJsonAsync($"/api/Story/{storyId}/Favorite", new { is_favorite = true });

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Single(await FavoritesAsync(user));
    }

    [Fact]
    public async Task 只能設定自己的劇本()
    {
        int owner = _api.NewUserId(), stranger = _api.NewUserId();
        int storyId = await _api.CreateStoryAsync(owner, "臺北市", night: false, Place("景點"));

        HttpResponseMessage response = await _api.ClientFor(stranger).PostAsJsonAsync($"/api/Story/{storyId}/Favorite", new { is_favorite = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await DetailAsync(owner, storyId)).is_favorite);
        Assert.Empty(await FavoritesAsync(stranger));
        Assert.Equal(HttpStatusCode.NotFound,
            (await _api.ClientFor(owner).PostAsJsonAsync("/api/Story/999999/Favorite", new { is_favorite = true })).StatusCode);
    }

    [Fact]
    public async Task 清單最新的劇本在前面_玩完的標示已完成()
    {
        int user = _api.NewUserId();
        int older = await _api.CreateStoryAsync(user, "臺北市", night: false, Place("舊劇本"));
        int newer = await _api.CreateStoryAsync(user, "臺北市", night: false, Place("新劇本"));
        await _api.ExecuteAsync("UPDATE story SET created_at = created_at - INTERVAL 1 DAY WHERE s_id = @older;", new { older });
        await _api.ExecuteAsync(@"
            INSERT INTO story_session (au_id, s_id, ss_current, ss_status, started_at, completed_at)
            VALUES (@user, @older, 1, 'completed', NOW(), NOW());", new { user, older });
        HttpClient client = _api.ClientFor(user);
        await client.PostAsJsonAsync($"/api/Story/{older}/Favorite", new { is_favorite = true });
        await client.PostAsJsonAsync($"/api/Story/{newer}/Favorite", new { is_favorite = true });

        List<FavoriteStoryItem> list = await FavoritesAsync(user);

        Assert.Equal(new[] { newer, older }, list.Select(s => s.story_id));
        Assert.Equal(new[] { false, true }, list.Select(s => s.is_completed));
    }

    [Fact]
    public async Task 沒登入不能用喜愛功能()
    {
        HttpClient anonymous = _api.ClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/Story/Favorites")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/Story/1/Favorite", new { is_favorite = true })).StatusCode);
    }
}
