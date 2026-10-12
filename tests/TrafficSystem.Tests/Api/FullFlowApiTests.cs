// 整體流程：用 Sqls/mysql/TestData_FullFlow.sql 的測試劇本，從確認劇本一路玩到過往紀錄，每一步都打真的 API。
// 這支測試同時是「前端串接順序」的範本：照著步驟編號呼叫就是一趟完整的遊玩。
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using backend.Models;
using backend.ViewModels;
using Dapper;
using MySql.Data.MySqlClient;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("整體流程", "API 整合測試")]
[AllureBddHierarchy("整體流程", "API 整合測試")]
public class FullFlowApiTests
{
    private const string StoryTitle = "【整體測試】舊城鐘聲尋寶記";

    // 四站的座標（跟 TestData_FullFlow.sql 的答案對照表一樣）
    private static readonly (double lat, double lng)[] Stops =
    {
        (24.1378509, 120.6835697),   // 宮原眼科
        (24.1423646, 120.6787413),   // 第二市場
        (24.1385594, 120.6784293),   // 臺中州廳
        (24.1395787, 120.6729394),   // 臺中文學館
    };

    private readonly ApiFactory _api;

    public FullFlowApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
        _api.FakeNeo4j.Reset();   // Neo4j 連不上，景點座標與照片用測試資料裡的 MySQL place
    }

    /// <summary>把測試資料灌進資料庫（掛在指定帳號底下），回傳劇本代號。可以重複執行</summary>
    private async Task<int> SeedAsync(int user)
    {
        var builder = new MySqlConnectionStringBuilder(_api.ConnectionString) { AllowUserVariables = true };
        using var conn = new MySqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync($"SET @fullflow_au_id = {user};");

        string sql = await File.ReadAllTextAsync(Path.Combine(ApiFactory.RepoRoot, "Sqls", "mysql", "TestData_FullFlow.sql"));
        await new MySqlScript(conn, sql).ExecuteAsync();

        return await conn.ExecuteScalarAsync<int>("SELECT s_id FROM story WHERE story_title = @StoryTitle AND au_id = @user;", new { StoryTitle, user });
    }

    private static async Task<T> OkAsync<T>(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.RequestMessage?.RequestUri?.PathAndQuery} → {(int)response.StatusCode} {body}");
        ApiResult<T> result = await response.ReadResultAsync<T>();
        Assert.True(result.isSuccess, result.message);
        return result.Result;
    }

    private static string Position(int stop) =>
        FormattableString.Invariant($"lat={Stops[stop].lat}&lng={Stops[stop].lng}");

    /// <summary>上傳一個檔案（POST /api/Upload），回傳檔案網址</summary>
    private static async Task<string> UploadAsync(HttpClient client, string fileName, string contentType)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(ApiFactory.TinyPng);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return await OkAsync<string>(await client.PostAsync("/api/Upload", form));
    }

    /// <summary>在第 stop 站的位置作答</summary>
    private static async Task<TaskAnswerResponse> AnswerAsync(HttpClient client, int stop, int taskId, object answer)
    {
        var body = new Dictionary<string, object> { ["task_id"] = taskId, ["gps_lat"] = Stops[stop].lat, ["gps_lon"] = Stops[stop].lng };
        foreach (var property in answer.GetType().GetProperties())
            body[property.Name] = property.GetValue(answer);
        return await OkAsync<TaskAnswerResponse>(await client.PostAsJsonAsync("/api/Task/Answer", body));
    }

    private static async Task<MapResponse> MapAsync(HttpClient client, int storyId) =>
        await OkAsync<MapResponse>(await client.GetAsync($"/api/Map/{storyId}"));

    /// <summary>抵達第 stop 站，拿到這一站的任務（依任務類型代號取用）</summary>
    private static async Task<Dictionary<int, NodePlayTask>> ArriveAsync(HttpClient client, MapResponse map, int stop)
    {
        int nodeId = map.nodes[stop].node_id;
        await OkAsync<NodeDetailResponse>(await client.PostAsync($"/api/Map/Node/{nodeId}/Arrive?{Position(stop)}", null));
        NodePlayResponse play = await OkAsync<NodePlayResponse>(await client.GetAsync($"/api/Task/Node/{nodeId}"));
        Assert.All(play.tasks, t => Assert.Equal(0, t.pass));
        return play.tasks.ToDictionary(t => t.type_id);
    }

    [Fact]
    public async Task 整體流程_從確認劇本玩到過往紀錄()
    {
        int user = _api.NewUserId();
        int storyId = await SeedAsync(user);
        HttpClient client = _api.ClientFor(user);

        // ── 1. 劇本詳情：4 站，每站有代號 ──
        StoryDetailResponse detail = await OkAsync<StoryDetailResponse>(await client.GetAsync($"/api/Story/{storyId}/Detail"));
        Assert.Equal(new[] { "紅磚醫生館", "六角樓", "白色廳舍", "老榕樹宿舍" }, detail.nodes.Select(n => n.location_codename));

        // ── 2. 確認劇本 → 這份劇本變成進行中 ──
        await OkAsync<StoryDetailResponse>(await client.PostAsJsonAsync("/api/Story/Confirm", new { story_id = storyId }));
        CurrentPlayingStory playing = await OkAsync<CurrentPlayingStory>(await client.GetAsync("/api/Story/CurrentPlaying"));
        Assert.Equal(storyId, playing.story_id);

        // ── 3. 地圖：只有第 1 站亮著，還沒抵達所以是代號和迷霧圖；其他站在迷霧中 ──
        MapResponse map = await MapAsync(client, storyId);
        Assert.Equal(new[] { true, false, false, false }, map.nodes.Select(n => n.is_unlocked));
        Assert.Equal(map.nodes[0].node_id, map.current_node_id);
        Assert.Equal("紅磚醫生館", map.nodes[0].codename);
        Assert.Null(map.nodes[0].place_name);
        Assert.Contains("/images/fog/", map.nodes[0].image_url);
        Assert.Equal(Stops[0].lat, map.nodes[0].lat, 6);
        Assert.NotEqual(Stops[1].lat, map.nodes[1].lat, 6);   // 迷霧中的站只給大概位置
        Assert.Equal(new[] { "六角樓", "白色廳舍", "老榕樹宿舍" }, map.nodes.Skip(1).Select(n => n.location_name));

        // ── 4. 路線：只到目前這一關；沒帶交通方式就用劇本選的（步行、公車）──
        RoutePlanResponse plan = await OkAsync<RoutePlanResponse>(await client.PostAsJsonAsync("/api/Route/Plan", new
        {
            story_id = storyId,
            start = new { lat = 24.1371, lng = 120.6869, name = "臺中車站" },
            include_return = false,
            current_only = true
        }));
        RouteLeg leg = Assert.Single(plan.legs);
        Assert.Equal(("臺中車站", "紅磚醫生館", true), (leg.from_name, leg.to_name, leg.is_current));
        Assert.Equal(3, plan.locked_stop_count);
        Assert.DoesNotContain("第二市場", JsonSerializer.Serialize(plan, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

        // ── 5. 周邊好去：跟著位置推薦，劇本自己的站不會出現；附近美食有優惠券的排最前面 ──
        List<NearbyPlaceResponse> nearby = await OkAsync<List<NearbyPlaceResponse>>(await client.GetAsync($"/api/Map/{storyId}/Nearby?{Position(0)}&radius_m=300"));
        Assert.Contains(nearby, p => p.name == "【測試】中山路豆花" && p.category == "飲食");
        Assert.Contains(nearby, p => p.name == "【測試】中山綠橋" && p.category == "其他");
        Assert.DoesNotContain(nearby, p => p.name == "宮原眼科");
        Assert.Equal(nearby.OrderBy(p => p.distance_m).Select(p => p.name), nearby.Select(p => p.name));
        Assert.Empty(await OkAsync<List<NearbyPlaceResponse>>(await client.GetAsync($"/api/Map/{storyId}/Nearby")));

        NearbyFoodResponse food = await OkAsync<NearbyFoodResponse>(await client.GetAsync($"/api/Map/NearbyFood?{Position(1)}&radius_m=300"));
        Assert.Equal("【測試】第二市場茶棧", food.places[0].name);
        Assert.Equal("闖關紅茶九折", Assert.Single(food.places[0].coupons).coupon_name);

        // ── 6. 還沒抵達：不能看任務；人太遠不能抵達；迷霧中的站不能抵達 ──
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/Task/Node/{map.nodes[0].node_id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/Map/Node/{map.nodes[0].node_id}/Arrive?lat=24.15&lng=120.70", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/Map/Node/{map.nodes[1].node_id}/Arrive?{Position(1)}", null)).StatusCode);

        // ── 7. 第 1 站：抵達 → GPS 定位、文化問答（先答錯一次、拿提示、再答對）、創意攝影 ──
        Dictionary<int, NodePlayTask> tasks = await ArriveAsync(client, map, 0);
        Assert.Equal(new[] { 1, 3, 6 }, tasks.Keys.OrderBy(k => k));
        Assert.Equal(new[] { "gps", "photo", "choice" }, new[] { 1, 3, 6 }.Select(k => tasks[k].answer_mode));
        Assert.Equal(new[] { "A", "B", "C" }, tasks[6].options.Select(o => o.option_key));

        map = await MapAsync(client, storyId);
        Assert.Equal("宮原眼科", map.nodes[0].place_name);                       // 抵達後才有真名與真實照片
        Assert.Contains("wikimedia.org", map.nodes[0].image_url);
        Assert.Equal(new[] { true, false, false, false }, map.nodes.Select(n => n.is_unlocked));   // 任務還沒過，下一站還是迷霧

        Assert.True((await AnswerAsync(client, 0, tasks[1].task_id, new { })).is_correct);

        TaskAnswerResponse wrong = await AnswerAsync(client, 0, tasks[6].task_id, new { selected_option_key = "A" });
        Assert.False(wrong.is_correct);
        Assert.Equal(0, wrong.pass);
        TaskHintResponse hint = await OkAsync<TaskHintResponse>(await client.GetAsync($"/api/Task/{tasks[6].task_id}/Hint"));
        Assert.Equal("薯光", hint.npc_name);
        Assert.True((await AnswerAsync(client, 0, tasks[6].task_id, new { selected_option_key = "B" })).is_correct);

        // 沒帶照片是輸入錯誤（400），不算答錯
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/Task/Answer",
            new { task_id = tasks[3].task_id, gps_lat = Stops[0].lat, gps_lon = Stops[0].lng })).StatusCode);

        string photo1 = await UploadAsync(client, "arch.jpg", "image/jpeg");
        TaskAnswerResponse cleared = await AnswerAsync(client, 0, tasks[3].task_id, new { photo_url = photo1 });
        Assert.True(cleared.node_completed);                                     // 這一站破關
        Assert.False(cleared.story_completed);
        Assert.Equal((3, 3), (cleared.node_progress.passed, cleared.node_progress.total));

        // ── 8. 破關後：第 2 站解鎖、變成目前這一關，路線多出下一段 ──
        map = await MapAsync(client, storyId);
        Assert.Equal(new[] { true, true, false, false }, map.nodes.Select(n => n.is_unlocked));
        Assert.Equal(map.nodes[1].node_id, map.current_node_id);
        Assert.True(map.nodes[0].is_completed);
        Assert.Equal(Stops[1].lat, map.nodes[1].lat, 6);

        plan = await OkAsync<RoutePlanResponse>(await client.PostAsJsonAsync("/api/Route/Plan", new { story_id = storyId, include_return = false, current_only = true }));
        leg = Assert.Single(plan.legs);
        Assert.Equal(("宮原眼科", "六角樓"), (leg.from_name, leg.to_name));     // 沒有定位：從上一站出發

        // ── 9. 第 2 站：景點猜猜樂、地方美食、商家知識問答（題目與選項來自商家題庫）──
        tasks = await ArriveAsync(client, map, 1);
        Assert.Equal(new[] { 4, 7, 9 }, tasks.Keys.OrderBy(k => k));
        Assert.Equal("茶棧的招牌古早味紅茶，是用哪一種方式煮的？", tasks[9].task_describe);
        Assert.Equal(3, tasks[9].options.Count);

        Assert.True((await AnswerAsync(client, 1, tasks[7].task_id, new { selected_option_key = "A" })).is_correct);
        string photo2 = await UploadAsync(client, "food.png", "image/png");
        Assert.True((await AnswerAsync(client, 1, tasks[4].task_id, new { photo_url = photo2 })).is_correct);
        Assert.True((await AnswerAsync(client, 1, tasks[9].task_id, new { selected_option_key = "B" })).node_completed);

        // ── 10. 第 3 站：圖像地理猜謎、e 人訪談（錄音）、協作解謎（答錯再答對）──
        map = await MapAsync(client, storyId);
        tasks = await ArriveAsync(client, map, 2);
        Assert.Equal(new[] { 5, 8, 10 }, tasks.Keys.OrderBy(k => k));
        Assert.Equal("你的線索：年份的前兩碼是 19。", tasks[5].clue_text);         // 沒有隊伍的擁有者是 1 號座位
        Assert.Equal("audio_or_video", tasks[8].answer_mode);

        Assert.True((await AnswerAsync(client, 2, tasks[10].task_id, new { selected_option_key = "C" })).is_correct);
        string audio = await UploadAsync(client, "interview.m4a", "audio/mp4");
        Assert.True((await AnswerAsync(client, 2, tasks[8].task_id, new { audio_url = audio })).is_correct);
        Assert.False((await AnswerAsync(client, 2, tasks[5].task_id, new { text_answer = "1900" })).is_correct);
        Assert.True((await AnswerAsync(client, 2, tasks[5].task_id, new { text_answer = "1913" })).node_completed);

        // ── 11. 第 4 站（最後一站）：跨關集結、文化問答 → 整份劇本完成 ──
        map = await MapAsync(client, storyId);
        tasks = await ArriveAsync(client, map, 3);
        Assert.Equal(new[] { 2, 6 }, tasks.Keys.OrderBy(k => k));

        Assert.False((await AnswerAsync(client, 3, tasks[2].task_id, new { text_answer = "因為沒有人記得它的故事" })).story_completed);
        TaskAnswerResponse last = await AnswerAsync(client, 3, tasks[6].task_id, new { selected_option_key = "A" });
        Assert.True(last.node_completed);
        Assert.True(last.story_completed);

        // ── 12. 全部破關：地圖全開、沒有目前這一關；路線回傳四站並規劃返程；遊玩紀錄標成完成 ──
        map = await MapAsync(client, storyId);
        Assert.All(map.nodes, n => Assert.True(n.is_unlocked && n.is_completed));
        Assert.Null(map.current_node_id);
        Assert.Equal(new[] { "宮原眼科", "第二市場", "臺中州廳", "臺中文學館" }, map.nodes.Select(n => n.place_name));

        plan = await OkAsync<RoutePlanResponse>(await client.PostAsJsonAsync("/api/Route/Plan", new { story_id = storyId, transportation = new[] { "步行" } }));
        Assert.Equal(new[] { "宮原眼科", "第二市場", "臺中州廳", "臺中文學館" }, plan.stops.Select(s => s.name));
        Assert.Equal(0, plan.locked_stop_count);
        Assert.Equal("返程", plan.legs[^1].direction);
        Assert.Equal("completed", await _api.QueryAsync<string>("SELECT ss_status FROM story_session WHERE s_id = @storyId AND au_id = @user;", new { storyId, user }));

        // ── 13. 勳章：完成後抽一枚，再抽還是同一枚 ──
        BadgeDrawResponse badge = await OkAsync<BadgeDrawResponse>(await client.PostAsJsonAsync("/api/Badge/Draw", new { story_id = storyId }));
        Assert.True(badge.is_new);
        Assert.NotNull(badge.badge);
        BadgeDrawResponse again = await OkAsync<BadgeDrawResponse>(await client.PostAsJsonAsync("/api/Badge/Draw", new { story_id = storyId }));
        Assert.False(again.is_new);

        // ── 14. 首頁總覽：完成 1 份劇本、1 枚勳章 ──
        HomeOverviewResponse home = await OkAsync<HomeOverviewResponse>(await client.GetAsync("/api/Home/Overview"));
        Assert.Equal((1, 1), (home.ho_completed_story, home.ho_badge_count));

        // ── 15. 過往紀錄：封面是第 1 站拍的照片；詳情有這趟拍的兩張照片（錄音不算）；回顧有每一站與作答統計 ──
        HistoryStoryItem history = Assert.Single(await OkAsync<List<HistoryStoryItem>>(await client.GetAsync("/api/History")));
        Assert.Equal((storyId, photo1), (history.story_id, history.cover_image_url));

        HistoryStoryItem past = await OkAsync<HistoryStoryItem>(await client.GetAsync($"/api/History/{storyId}"));
        Assert.Equal(new[] { photo1, photo2 }, past.photos.Select(p => p.image_url));
        Assert.Equal(new[] { "宮原眼科", "第二市場" }, past.photos.Select(p => p.place_name));
        Assert.Equal(new[] { "宮原眼科", "第二市場", "臺中州廳", "臺中文學館" }, past.spots);

        StoryRecapResponse recap = await OkAsync<StoryRecapResponse>(await client.GetAsync($"/api/History/{storyId}/Recap"));
        Assert.Equal((4, 11, 2), (recap.stats.node_count, recap.stats.tasks_answered, recap.stats.photo_count));
        Assert.Equal(badge.badge.b_id, recap.badge.badge_id);

        // ── 16. 明信片：這趟還沒生成過（生成要呼叫 AI 服務，另外有測試），清單是空的 ──
        Assert.Empty(await OkAsync<List<JsonElement>>(await client.GetAsync($"/api/PostcardCatalog/by-story/{storyId}")));

        // ── 17. 結束劇本：不再是進行中 ──
        await client.PostAsJsonAsync("/api/Story/EndStory", new { story_id = storyId });
        CurrentPlayingStory none = (await (await client.GetAsync("/api/Story/CurrentPlaying")).ReadResultAsync<CurrentPlayingStory>()).Result;
        Assert.True(none == null || none.story_id != storyId);
    }

    [Fact]
    public async Task 測試資料可以重複灌_每次都回到還沒開始玩()
    {
        int user = _api.NewUserId();
        int first = await SeedAsync(user);
        HttpClient client = _api.ClientFor(user);

        // 玩到第 1 站抵達、過一題
        MapResponse map = await MapAsync(client, first);
        Dictionary<int, NodePlayTask> tasks = await ArriveAsync(client, map, 0);
        await AnswerAsync(client, 0, tasks[1].task_id, new { });

        int second = await SeedAsync(user);

        Assert.Equal(1, await _api.QueryAsync<int>("SELECT COUNT(*) FROM story WHERE story_title = @StoryTitle AND au_id = @user;", new { StoryTitle, user }));
        Assert.Equal(0, await _api.QueryAsync<int>("SELECT COUNT(*) FROM story WHERE s_id = @first;", new { first }));
        Assert.Equal((4, 11, 10), await _api.QueryAsync<(int, int, int)>(@"
            SELECT (SELECT COUNT(*) FROM story_node WHERE s_id = @second),
                   (SELECT COUNT(*) FROM task WHERE story_id = @second),
                   (SELECT COUNT(DISTINCT task_type) FROM task WHERE story_id = @second);", new { second }));
        Assert.Equal(0, await _api.QueryAsync<int>("SELECT COUNT(*) FROM user_task_record WHERE au_id = @user;", new { user }));
        Assert.Equal(1, await _api.QueryAsync<int>("SELECT COUNT(*) FROM store WHERE store_uid = 'fullflow-store-01';"));

        map = await MapAsync(client, second);
        Assert.Equal(new[] { true, false, false, false }, map.nodes.Select(n => n.is_unlocked));
        Assert.All(map.nodes, n => Assert.False(n.is_arrived));
    }
}
