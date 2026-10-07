// 劇本與地圖 API：劇本詳情、遊玩狀態、地圖節點
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using backend.Models;
using backend.Services;
using backend.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("解謎地圖與迷霧", "API 整合測試")]
[AllureBddHierarchy("解謎地圖與迷霧", "API 整合測試")]
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
    public async Task 劇本詳情帶日夜模式與每站任務類型()
    {
        int user = _api.NewUserId();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: true, "台北101", "西門町");
        await _api.ExecuteAsync("UPDATE story_node SET sn_task_type = '文化問答型' WHERE s_id = @storyId AND sn_order = 1;", new { storyId });

        StoryDetailResponse detail = (await (await _api.ClientFor(user).GetAsync($"/api/Story/{storyId}/Detail")).ReadResultAsync<StoryDetailResponse>()).Result;

        Assert.True(detail.is_night_mode);
        Assert.Equal(new[] { "文化問答型", "" }, detail.nodes.Select(n => n.task_type));
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

    #region 迷霧（後端處理）

    /// <summary>建立 3 站的劇本，每站都有照片；回傳 (story_id, 各站照片網址)</summary>
    private async Task<(int storyId, string[] photos)> CreateFoggyStoryAsync(int user)
    {
        string[] places = Enumerable.Range(1, 3).Select(i => $"迷霧景點{i}_{Guid.NewGuid():N}").ToArray();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, places);
        var photos = new List<string>();
        foreach (string place in places)
            photos.Add(await _api.GivePlacePhotoAsync(place));
        return (storyId, photos.ToArray());
    }

    private async Task<MapResponse> GetMapAsync(int user, int storyId) =>
        (await (await _api.ClientFor(user).GetAsync($"/api/Map/{storyId}")).ReadResultAsync<MapResponse>()).Result;

    private static double Meters(double lat1, double lng1, double lat2, double lng2)
    {
        double dLat = (lat2 - lat1) * 111320, dLng = (lng2 - lng1) * 111320 * Math.Cos(lat1 * Math.PI / 180);
        return Math.Sqrt(dLat * dLat + dLng * dLng);
    }

    [Fact]
    public async Task 迷霧中的站不回傳真正的站名_照片與精確座標()
    {
        int user = _api.NewUserId();
        var (storyId, photos) = await CreateFoggyStoryAsync(user);

        MapResponse map = await GetMapAsync(user, storyId);

        // 第 1 站開放：真正的站名、照片、座標
        MapNode open = map.nodes[0];
        Assert.Equal("第1站", open.location_name);
        Assert.Equal(photos[0], open.image_url);
        Assert.Equal(25.03, open.lat, 6);
        Assert.Null(open.fog_radius_m);

        // 第 2、3 站在迷霧中：站名換成地點代號、照片換成迷霧圖、座標偏移但真正的點在迷霧範圍內
        for (int i = 1; i < 3; i++)
        {
            MapNode fog = map.nodes[i];
            Assert.Equal($"代號{i + 1}", fog.location_name);
            Assert.StartsWith("http", fog.image_url);
            Assert.EndsWith("/images/fog/default_fog.png", fog.image_url);
            Assert.DoesNotContain(photos[i], JsonSerializer.Serialize(map));
            Assert.Equal(300, fog.fog_radius_m);
            double offset = Meters(25.03 + i * 0.001, 121.5, fog.lat, fog.lng);
            Assert.InRange(offset, 50, 250);
        }

        // 同一站每次的迷霧位置都一樣，不會在地圖上跳動
        MapResponse again = await GetMapAsync(user, storyId);
        Assert.Equal(map.nodes.Select(n => (n.lat, n.lng)), again.nodes.Select(n => (n.lat, n.lng)));
    }

    [Fact]
    public async Task 迷霧中的站不能查看詳情_互動_導航或打卡()
    {
        int user = _api.NewUserId();
        var (storyId, _) = await CreateFoggyStoryAsync(user);
        HttpClient client = _api.ClientFor(user);
        int locked = (await GetMapAsync(user, storyId)).nodes[2].node_id;

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/Map/Node/{locked}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/Map/Node/{locked}/Interact")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/Map/Navigate", new { node_id = locked })).StatusCode);

        // 就算人真的站在第 3 站，也不能跳過第 2 站直接打卡
        HttpResponseMessage arrive = await client.PostAsync($"/api/Map/Node/{locked}/Arrive?lat=25.032&lng=121.5", null);
        Assert.Equal(HttpStatusCode.Forbidden, arrive.StatusCode);
        Assert.Contains("迷霧", (await arrive.ReadResultAsync()).message);
    }

    [Fact]
    public async Task 抵達第一站後第二站的迷霧散開()
    {
        int user = _api.NewUserId();
        var (storyId, photos) = await CreateFoggyStoryAsync(user);
        HttpClient client = _api.ClientFor(user);
        MapResponse before = await GetMapAsync(user, storyId);

        HttpResponseMessage arrive = await client.PostAsync($"/api/Map/Node/{before.nodes[0].node_id}/Arrive?lat=25.03&lng=121.5", null);
        Assert.Equal(HttpStatusCode.OK, arrive.StatusCode);

        MapResponse after = await GetMapAsync(user, storyId);
        Assert.Equal(new[] { true, true, false }, after.nodes.Select(n => n.is_unlocked));
        Assert.Equal("第2站", after.nodes[1].location_name);
        Assert.Equal(photos[1], after.nodes[1].image_url);
        Assert.Equal(25.031, after.nodes[1].lat, 6);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Map/Navigate", new { node_id = after.nodes[1].node_id })).StatusCode);
    }

    [Fact]
    public async Task 迷霧圖可以直接下載()
    {
        int user = _api.NewUserId();
        var (storyId, _) = await CreateFoggyStoryAsync(user);
        string fogUrl = (await GetMapAsync(user, storyId)).nodes[1].image_url;

        HttpResponseMessage response = await _api.CreateClient().GetAsync(new Uri(fogUrl).PathAndQuery);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType.MediaType);
    }

    private async Task<FogGenerateResult> GenerateFogAsync(int storyId)
    {
        using IServiceScope scope = _api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FogService>().GenerateForStoryAsync(storyId);
    }

    [Fact]
    public async Task 做好迷霧圖後_迷霧中的站顯示自己照片的迷霧版()
    {
        int user = _api.NewUserId();
        var (storyId, photos) = await CreateFoggyStoryAsync(user);

        FogGenerateResult result = await GenerateFogAsync(storyId);
        MapResponse map = await GetMapAsync(user, storyId);

        Assert.Equal(3, result.generated);
        Assert.Equal(photos[0], map.nodes[0].image_url);   // 已解鎖：原始照片
        for (int i = 1; i < 3; i++)
        {
            string key = FogService.SourceKey(photos[i]);
            Assert.EndsWith($"/images/fog/generated/{key}.jpg", map.nodes[i].image_url);
            Assert.True(File.Exists(Path.Combine(_api.FogRoot, "images", "fog", "generated", key + ".jpg")));
        }
        Assert.DoesNotContain(photos[1], JsonSerializer.Serialize(map));
        Assert.DoesNotContain("fog_image", JsonSerializer.Serialize(map));   // 內部欄位不回傳
    }

    [Fact]
    public async Task 同一張照片只做一次迷霧圖()
    {
        int user = _api.NewUserId();
        var (storyId, _) = await CreateFoggyStoryAsync(user);

        await GenerateFogAsync(storyId);
        FogGenerateResult again = await GenerateFogAsync(storyId);

        Assert.Equal(0, again.generated);
        Assert.Equal(3, again.reused);
    }

    [Fact]
    public async Task 還沒做迷霧圖的劇本_打開地圖後會在背景補做()
    {
        int user = _api.NewUserId();
        var (storyId, _) = await CreateFoggyStoryAsync(user);

        Assert.EndsWith("/images/fog/default_fog.png", (await GetMapAsync(user, storyId)).nodes[1].image_url);

        string image = null;
        for (int i = 0; i < 50 && (image == null || !image.Contains("/generated/")); i++)
        {
            await Task.Delay(100);
            image = (await GetMapAsync(user, storyId)).nodes[1].image_url;
        }
        Assert.Contains("/images/fog/generated/", image);
    }

    [Fact]
    public async Task 迷霧圖可以從資料表換掉()
    {
        int user = _api.NewUserId();
        var (storyId, _) = await CreateFoggyStoryAsync(user);
        await _api.ExecuteAsync("UPDATE fog SET is_default = 0;");
        await _api.ExecuteAsync("INSERT INTO fog (fog_name, fog_image, is_default) VALUES ('設計稿迷霧', 'https://cdn.test/fog-v2.png', 1);");
        try
        {
            Assert.Equal("https://cdn.test/fog-v2.png", (await GetMapAsync(user, storyId)).nodes[1].image_url);
        }
        finally
        {
            await _api.ExecuteAsync("DELETE FROM fog WHERE fog_image = 'https://cdn.test/fog-v2.png'; UPDATE fog SET is_default = 1 WHERE fog_id = 1;");
        }
    }

    #endregion
}
