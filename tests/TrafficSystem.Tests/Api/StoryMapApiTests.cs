// 劇本與地圖 API：劇本詳情、遊玩狀態、地圖節點
using System.Net;
using System.Net.Http.Json;
using backend.Models;
using backend.ViewModels;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
public class StoryMapApiTests
{
    private readonly ApiFactory _api;

    public StoryMapApiTests(ApiFactory api) => _api = api;

    #region 劇本

    [Fact]
    public async Task 劇本詳情依順序回傳每一站()
    {
        int user = _api.NewUserId();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, "台北101", "國立中正紀念堂", "西門町");

        HttpResponseMessage response = await _api.ClientFor(user).GetAsync($"/api/Story/{storyId}/Detail");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        StoryDetailResponse detail = (await response.ReadResultAsync<StoryDetailResponse>()).Result;
        Assert.Equal(storyId, detail.story_id);
        Assert.Equal(new[] { 1, 2, 3 }, detail.nodes.Select(n => n.order));
        Assert.Equal(new[] { "代號1", "代號2", "代號3" }, detail.nodes.Select(n => n.location_codename));
    }

    [Fact]
    public async Task 劇本詳情帶景點照片_劇本檔案館用()
    {
        int user = _api.NewUserId();
        string withPhoto = $"有照片的景點{Guid.NewGuid():N}", noPhoto = $"沒照片的景點{Guid.NewGuid():N}";
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, withPhoto, noPhoto);
        string photoUrl = await _api.GivePlacePhotoAsync(withPhoto);

        StoryDetailResponse detail = (await (await _api.ClientFor(user).GetAsync($"/api/Story/{storyId}/Detail")).ReadResultAsync<StoryDetailResponse>()).Result;

        Assert.Equal(new[] { photoUrl, null }, detail.nodes.Select(n => n.image_url));
    }

    [Fact]
    public async Task 完整劇本詳情可以查詢()
    {
        int user = _api.NewUserId();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: true, "台北101", "西門町");

        HttpResponseMessage response = await _api.ClientFor(user).GetAsync($"/api/Story/{storyId}/FullDetail");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ScriptBlueprintData data = (await response.ReadResultAsync<ScriptBlueprintData>()).Result;
        Assert.True(data.is_night_mode);
        Assert.Equal(2, data.nodes.Count);
    }

    [Fact]
    public async Task 查詢不存在的劇本回傳錯誤訊息()
    {
        HttpResponseMessage response = await _api.ClientFor(_api.NewUserId()).GetAsync("/api/Story/999999/Detail");

        ApiResult<StoryDetailResponse> result = await response.ReadResultAsync<StoryDetailResponse>();
        Assert.False(result.isSuccess);
        Assert.Contains("找不到此劇本", result.message);
    }

    [Fact]
    public async Task 開始遊玩後查得到進行中的劇本_結束後就沒有()
    {
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, "台北101");

        await client.PostAsJsonAsync("/api/Story/Confirm", new { story_id = storyId });
        CurrentPlayingStory playing = (await (await client.GetAsync("/api/Story/CurrentPlaying")).ReadResultAsync<CurrentPlayingStory>()).Result;
        Assert.Equal(storyId, playing.story_id);

        await client.PostAsJsonAsync("/api/Story/EndStory", new { story_id = storyId });
        CurrentPlayingStory after = (await (await client.GetAsync("/api/Story/CurrentPlaying")).ReadResultAsync<CurrentPlayingStory>()).Result;
        Assert.Null(after);
    }

    #endregion

    #region 地圖

    [Fact]
    public async Task 地圖節點不會因為景點有多種任務類型而重複()
    {
        int user = _api.NewUserId();
        // 每個景點在 place_type 有 3 筆（3 種任務類型）
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, "台北101", "國立中正紀念堂", "西門町");

        HttpResponseMessage response = await _api.ClientFor(user).GetAsync($"/api/Map/{storyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        MapResponse map = (await response.ReadResultAsync<MapResponse>()).Result;
        Assert.Equal(3, map.total_node_count);
        Assert.Equal(new[] { 1, 2, 3 }, map.nodes.Select(n => n.node_order));
    }

    [Fact]
    public async Task 地圖第一站開放_後面的站顯示地點代號當迷霧提示()
    {
        int user = _api.NewUserId();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, "台北101", "國立中正紀念堂", "西門町");

        MapResponse map = (await (await _api.ClientFor(user).GetAsync($"/api/Map/{storyId}")).ReadResultAsync<MapResponse>()).Result;

        Assert.Equal(new[] { true, false, false }, map.nodes.Select(n => n.is_unlocked));
        Assert.Equal(new[] { null, "代號2", "代號3" }, map.nodes.Select(n => n.fog_hint));
        Assert.All(map.nodes, n => Assert.NotEqual(0, n.lat));   // 座標有從 place 帶出來
    }

    #endregion
}
