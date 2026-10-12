// 劇本與地圖 API：劇本詳情、遊玩狀態、地圖節點
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using backend.Models;
using backend.Services;
using backend.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
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

        // 第 1 站開放：站名、座標是真的；但還沒抵達，照片仍然是迷霧圖（真實照片一看就知道答案）
        MapNode open = map.nodes[0];
        Assert.Equal("第1站", open.location_name);
        Assert.EndsWith("/images/fog/default_fog.png", open.image_url);
        Assert.DoesNotContain(photos[0], JsonSerializer.Serialize(map));
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
        Assert.Equal(25.031, after.nodes[1].lat, 6);

        // 照片抵達後才給：到過的第 1 站是真實照片；第 2 站解鎖了但還沒到，仍然是迷霧圖
        Assert.Equal(photos[0], after.nodes[0].image_url);
        Assert.Contains("/images/fog/", after.nodes[1].image_url);   // 通用迷霧圖，或背景已經做好的這一站照片迷霧版
        Assert.DoesNotContain(photos[1], JsonSerializer.Serialize(after));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Map/Navigate", new { node_id = after.nodes[1].node_id })).StatusCode);
    }

    /// <summary>在這一站放一題任務，回傳 task_id</summary>
    private async Task<int> AddTaskAsync(int storyId, int nodeId)
    {
        await _api.ExecuteAsync("INSERT INTO task (story_id, node_id, task_type, task_describe) VALUES (@storyId, @nodeId, 6, '這裡以前叫什麼？');",
            new { storyId, nodeId });
        return await _api.QueryAsync<int>("SELECT MAX(task_id) FROM task WHERE node_id = @nodeId;", new { nodeId });
    }

    [Fact]
    public async Task 有任務的站_抵達後下一站還在迷霧中_任務全過才散開()
    {
        int user = _api.NewUserId();
        var (storyId, _) = await CreateFoggyStoryAsync(user);
        HttpClient client = _api.ClientFor(user);
        MapResponse start = await GetMapAsync(user, storyId);
        int first = start.nodes[0].node_id, second = start.nodes[1].node_id;
        int task1 = await AddTaskAsync(storyId, first), task2 = await AddTaskAsync(storyId, first);

        // 一開始：只有第 1 站是亮的
        Assert.Equal(first, start.current_node_id);
        Assert.Equal(new[] { true, false, false }, start.nodes.Select(n => n.is_current));
        Assert.Equal(300, start.reveal_radius_m);

        // 抵達第 1 站、只過一題：第 2 站還是迷霧，也不能直接去打卡
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/Map/Node/{first}/Arrive?lat=25.03&lng=121.5", null)).StatusCode);
        await _api.ExecuteAsync("UPDATE task SET pass = 1 WHERE task_id = @task1;", new { task1 });

        MapResponse arrived = await GetMapAsync(user, storyId);
        Assert.Equal(new[] { true, false, false }, arrived.nodes.Select(n => n.is_unlocked));
        Assert.Equal(new[] { true, false, false }, arrived.nodes.Select(n => n.is_arrived));
        Assert.False(start.nodes[0].is_arrived);

        // 地標名稱：還沒抵達只有代號，抵達後才有景點真實名稱
        Assert.Equal(new[] { "代號1", "代號2", "代號3" }, start.nodes.Select(n => n.codename));
        Assert.All(start.nodes, n => Assert.Null(n.place_name));
        Assert.StartsWith("迷霧景點1_", arrived.nodes[0].place_name);
        Assert.Null(arrived.nodes[1].place_name);
        Assert.Equal(first, arrived.current_node_id);
        Assert.Equal("代號2", arrived.nodes[1].location_name);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/Map/Node/{second}/Arrive?lat=25.031&lng=121.5", null)).StatusCode);

        // 第 1 站任務全過：第 2 站散開，變成目前這一關
        await _api.ExecuteAsync("UPDATE task SET pass = 1 WHERE task_id = @task2;", new { task2 });

        MapResponse cleared = await GetMapAsync(user, storyId);
        Assert.Equal(new[] { true, true, false }, cleared.nodes.Select(n => n.is_unlocked));
        Assert.Equal(new[] { false, true, false }, cleared.nodes.Select(n => n.is_current));
        Assert.Equal(second, cleared.current_node_id);
        Assert.True(cleared.nodes[0].is_completed);
        Assert.Equal("第2站", cleared.nodes[1].location_name);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/Map/Node/{second}/Arrive?lat=25.031&lng=121.5", null)).StatusCode);
    }

    [Fact]
    public async Task 每一站都破關後_全部解鎖_沒有目前這一關()
    {
        int user = _api.NewUserId();
        var (storyId, _) = await CreateFoggyStoryAsync(user);
        HttpClient client = _api.ClientFor(user);
        MapResponse map = await GetMapAsync(user, storyId);

        // 沒有任務的站抵達就算破關，照順序走完三站
        for (int i = 0; i < 3; i++)
        {
            int node = (await GetMapAsync(user, storyId)).nodes[i].node_id;
            HttpResponseMessage arrive = await client.PostAsync($"/api/Map/Node/{node}/Arrive?lat={25.03 + i * 0.001}&lng=121.5", null);
            Assert.Equal(HttpStatusCode.OK, arrive.StatusCode);
        }

        MapResponse done = await GetMapAsync(user, storyId);
        Assert.All(done.nodes, n => Assert.True(n.is_unlocked));
        Assert.All(done.nodes, n => Assert.False(n.is_current));
        Assert.Null(done.current_node_id);
        Assert.Equal(map.total_node_count, done.unlocked_node_count);
    }

    [Fact]
    public async Task 目前這一關還沒抵達_詳情與NPC互動不給景點介紹和真實照片_抵達後才給()
    {
        int user = _api.NewUserId();
        var (storyId, photos) = await CreateFoggyStoryAsync(user);
        await _api.ExecuteAsync(@"
            UPDATE place p
            INNER JOIN place_type pt ON pt.place_name = p.p_name
            INNER JOIN story_node sn ON sn.place_id = pt.place_id
            SET p.p_introduction = '這裡是巴洛克式的百年紅磚建築', p.p_summary = '百年紅磚廳舍', p.p_open_time = '09:00-17:00'
            WHERE sn.s_id = @storyId;", new { storyId });
        HttpClient client = _api.ClientFor(user);
        int first = (await GetMapAsync(user, storyId)).nodes[0].node_id;

        // 第 1 站已解鎖但還沒到：看得到站名與 NPC，看不到景點介紹、營業時間、真實照片
        NodeDetailResponse detail = (await (await client.GetAsync($"/api/Map/Node/{first}")).ReadResultAsync<NodeDetailResponse>()).Result;
        Assert.Equal("第1站", detail.location_name);
        Assert.Equal(MapService.NotArrivedIntro, detail.intro_story);
        Assert.Null(detail.opening_hours);

        NpcInteractionResponse npc = (await (await client.GetAsync($"/api/Map/Node/{first}/Interact")).ReadResultAsync<NpcInteractionResponse>()).Result;
        Assert.StartsWith("http", npc.scene_image_url);
        Assert.Contains("/images/fog/", npc.scene_image_url);
        Assert.NotEqual(photos[0], npc.scene_image_url);
        Assert.Null(npc.location_subtitle);

        // 抵達後：景點介紹、營業時間、真實照片都給
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/Map/Node/{first}/Arrive?lat=25.03&lng=121.5", null)).StatusCode);

        detail = (await (await client.GetAsync($"/api/Map/Node/{first}")).ReadResultAsync<NodeDetailResponse>()).Result;
        Assert.Equal("這裡是巴洛克式的百年紅磚建築", detail.intro_story);
        Assert.Equal("09:00-17:00", detail.opening_hours);

        npc = (await (await client.GetAsync($"/api/Map/Node/{first}/Interact")).ReadResultAsync<NpcInteractionResponse>()).Result;
        Assert.Equal(photos[0], npc.scene_image_url);
        Assert.Equal("百年紅磚廳舍", npc.location_subtitle);
    }

    /// <summary>後端設定打開示範模式（Demo:AllowStageSkip = true）時的 HttpClient</summary>
    private HttpClient DemoClientFor(int user)
    {
        HttpClient client = _api.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string> { ["Demo:AllowStageSkip"] = "true" }))).CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiFactory.Token(user));
        return client;
    }

    [Fact]
    public async Task 示範模式預設關閉_不能直接破關()
    {
        int user = _api.NewUserId();
        var (storyId, _) = await CreateFoggyStoryAsync(user);
        MapResponse map = await GetMapAsync(user, storyId);

        Assert.False(map.demo_mode);
        HttpClient client = _api.ClientFor(user);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/Map/Node/{map.nodes[0].node_id}/DemoClear", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/Map/{storyId}/DemoReset", null)).StatusCode);
        Assert.Equal(new[] { true, false, false }, (await GetMapAsync(user, storyId)).nodes.Select(n => n.is_unlocked));
    }

    [Fact]
    public async Task 示範模式_不用到現場也不用作答就能一關一關破_還可以重來()
    {
        int user = _api.NewUserId();
        var (storyId, _) = await CreateFoggyStoryAsync(user);
        HttpClient demo = DemoClientFor(user);
        int[] nodes = (await GetMapAsync(user, storyId)).nodes.Select(n => n.node_id).ToArray();
        await AddTaskAsync(storyId, nodes[0]);
        await AddTaskAsync(storyId, nodes[2]);

        // 不能跳關：第 3 站還在迷霧中
        Assert.Equal(HttpStatusCode.Forbidden, (await demo.PostAsync($"/api/Map/Node/{nodes[2]}/DemoClear", null)).StatusCode);

        // 破第 1 關：有任務也直接通過，第 2 站解鎖
        MapResponse first = (await (await demo.PostAsync($"/api/Map/Node/{nodes[0]}/DemoClear", null)).ReadResultAsync<MapResponse>()).Result;
        Assert.True(first.demo_mode);
        Assert.Equal(new[] { true, true, false }, first.nodes.Select(n => n.is_unlocked));
        Assert.Equal(nodes[1], first.current_node_id);
        Assert.True(first.nodes[0].is_completed);
        Assert.Equal("in_progress", await _api.QueryAsync<string>("SELECT ss_status FROM story_session WHERE s_id = @storyId;", new { storyId }));

        // 破完三關：全部解鎖，遊玩紀錄標成完成
        await demo.PostAsync($"/api/Map/Node/{nodes[1]}/DemoClear", null);
        MapResponse done = (await (await demo.PostAsync($"/api/Map/Node/{nodes[2]}/DemoClear", null)).ReadResultAsync<MapResponse>()).Result;
        Assert.All(done.nodes, n => Assert.True(n.is_unlocked));
        Assert.Null(done.current_node_id);
        Assert.Equal("completed", await _api.QueryAsync<string>("SELECT ss_status FROM story_session WHERE s_id = @storyId;", new { storyId }));

        // 重來：回到只有第 1 站是亮的
        MapResponse reset = (await (await demo.PostAsync($"/api/Map/{storyId}/DemoReset", null)).ReadResultAsync<MapResponse>()).Result;
        Assert.Equal(new[] { true, false, false }, reset.nodes.Select(n => n.is_unlocked));
        Assert.Equal(nodes[0], reset.current_node_id);
        Assert.All(reset.nodes, n => Assert.False(n.is_arrived));
        Assert.Equal(0, await _api.QueryAsync<int>("SELECT COUNT(*) FROM task WHERE story_id = @storyId AND pass = 1;", new { storyId }));

        // 別人的劇本不能用示範破關
        Assert.Equal(HttpStatusCode.Forbidden, (await DemoClientFor(_api.NewUserId()).PostAsync($"/api/Map/Node/{nodes[0]}/DemoClear", null)).StatusCode);
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
        // 三站都還沒抵達（第 1 站已解鎖也一樣）：都是自己照片的迷霧版
        for (int i = 0; i < 3; i++)
        {
            string key = FogService.SourceKey(photos[i]);
            Assert.EndsWith($"/images/fog/generated/{key}.jpg", map.nodes[i].image_url);
            Assert.True(File.Exists(Path.Combine(_api.FogRoot, "images", "fog", "generated", key + ".jpg")));
        }
        Assert.All(photos, photo => Assert.DoesNotContain(photo, JsonSerializer.Serialize(map)));
        Assert.DoesNotContain("fog_image", JsonSerializer.Serialize(map));   // 內部欄位不回傳

        // 抵達第 1 站後才換成原始照片
        await _api.ClientFor(user).PostAsync($"/api/Map/Node/{map.nodes[0].node_id}/Arrive?lat=25.03&lng=121.5", null);
        Assert.Equal(photos[0], (await GetMapAsync(user, storyId)).nodes[0].image_url);
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
