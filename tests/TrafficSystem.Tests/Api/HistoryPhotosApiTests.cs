// 過往紀錄的照片：GET /api/History 卡片封面、GET /api/History/{story_id} 上方左右滑動的照片，是每一站的景點照片，
// 不回傳玩家上傳的照片；每張照片附景點的真實名稱（real_place_name）。
// 這裡 Neo4j 預設連不上，景點照片來自 MySQL place.p_image（Neo4j 的部分見 PlacePhotoApiTests）
using backend.Models;
using backend.utils;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("VLOG 與回顧", "API 整合測試")]
[AllureBddHierarchy("VLOG 與回顧", "API 整合測試")]
public class HistoryPhotosApiTests
{
    private readonly ApiFactory _api;

    public HistoryPhotosApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeNeo4j.Reset();
    }

    /// <summary>建立一個玩完的 3 站劇本，回傳 (story_id, 每站的 sn_id, 景點名稱)</summary>
    private async Task<(int storyId, int[] nodes, string[] places)> CreateFinishedStoryAsync(int user, DateTime completedAt)
    {
        string[] places = Enumerable.Range(1, 3).Select(i => $"過往景點{i}_{Guid.NewGuid():N}"[..14]).ToArray();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, places);
        int[] nodes = (await QueryAsync<int>("SELECT sn_id FROM story_node WHERE s_id = @storyId ORDER BY sn_order;", new { storyId })).ToArray();

        await _api.ExecuteAsync(@"
            INSERT INTO story_session (au_id, s_id, ss_current, ss_status, started_at, completed_at)
            VALUES (@user, @storyId, 3, 'completed', @startedAt, @completedAt);",
            new { user, storyId, startedAt = completedAt.AddHours(-2), completedAt });
        return (storyId, nodes, places);
    }

    private async Task<List<T>> QueryAsync<T>(string sql, object param)
    {
        using var conn = new MySql.Data.MySqlClient.MySqlConnection(_api.ConnectionString);
        return (await Dapper.SqlMapper.QueryAsync<T>(conn, sql, param)).ToList();
    }

    /// <summary>玩家在這一站上傳了一張照片（作答紀錄的 answer_media_url）</summary>
    private async Task UploadPhotoAsync(int user, int storyId, int nodeId, string url)
    {
        await _api.ExecuteAsync("INSERT INTO task (story_id, node_id, task_type, task_describe) VALUES (@storyId, @nodeId, 3, '和景點合照');",
            new { storyId, nodeId });
        int taskId = await _api.QueryAsync<int>("SELECT MAX(task_id) FROM task WHERE story_id = @storyId;", new { storyId });

        await _api.ExecuteAsync(@"
            INSERT INTO user_task_record (record_id, au_id, task_id, answer_media_url, answered_at)
            VALUES (@recordId, @user, @taskId, @url, NOW());",
            new { recordId = _api.NewUserId(), user, taskId, url });   // record_id 不是自動編號，借用遞增的號碼
    }

    private async Task<List<HistoryStoryItem>> ListAsync(int user) =>
        (await (await _api.ClientFor(user).GetAsync("/api/History")).ReadResultAsync<List<HistoryStoryItem>>()).Result;

    private async Task<HistoryStoryItem> DetailAsync(int user, int storyId) =>
        (await (await _api.ClientFor(user).GetAsync($"/api/History/{storyId}")).ReadResultAsync<HistoryStoryItem>()).Result;

    [Fact]
    public async Task 過往紀錄卡片封面用景點照片_玩家上傳的照片不算_景點都沒照片時是null()
    {
        int user = _api.NewUserId();
        var (withPlacePhoto, nodes, places) = await CreateFinishedStoryAsync(user, DateTime.Now.AddDays(-1));
        var (noPhotos, otherNodes, _) = await CreateFinishedStoryAsync(user, DateTime.Now);

        // 第 1 站玩家有上傳照片，但景點沒有照片；第 2 站的景點有照片
        await UploadPhotoAsync(user, withPlacePhoto, nodes[0], "https://img.test/mine.jpg");
        string second = await _api.GivePlacePhotoAsync(places[1]);
        // 景點都沒照片的劇本，玩家上傳的照片也不拿來當封面
        await UploadPhotoAsync(user, noPhotos, otherNodes[0], "https://img.test/mine-too.jpg");

        List<HistoryStoryItem> list = await ListAsync(user);

        Assert.Equal(new[] { noPhotos, withPlacePhoto }, list.Select(s => s.story_id));   // 最近玩完的在前面
        Assert.Equal(second, list[1].cover_image_url);
        Assert.Null(list[0].cover_image_url);
        Assert.All(list, s => Assert.Null(s.photos));
    }

    [Fact]
    public async Task 過往劇本詳情回傳每一站的景點照片_不回傳玩家上傳的照片_附景點真實名稱()
    {
        int user = _api.NewUserId();
        var (storyId, nodes, places) = await CreateFinishedStoryAsync(user, DateTime.Now);
        string first = await _api.GivePlacePhotoAsync(places[0]);
        string second = await _api.GivePlacePhotoAsync(places[1]);
        // 第 3 站的景點照片是相對路徑，補成完整網址
        await _api.ExecuteAsync("UPDATE place SET p_image = '/images/places/third.webp' WHERE p_name = @name;", new { name = places[2] });

        await UploadPhotoAsync(user, storyId, nodes[0], "https://img.test/dome.jpg");
        await UploadPhotoAsync(user, storyId, nodes[2], "https://img.test/lantern.jpg");

        HttpResponseMessage response = await _api.ClientFor(user).GetAsync($"/api/History/{storyId}");
        string json = await response.Content.ReadAsStringAsync();
        HistoryStoryItem detail = (await response.ReadResultAsync<HistoryStoryItem>()).Result;

        Assert.Equal(new[] { first, second, "http://localhost/images/places/third.webp" }, detail.photos.Select(p => p.image_url));
        Assert.Equal(new[] { 1, 2, 3 }, detail.photos.Select(p => p.node_order));
        Assert.Equal(nodes, detail.photos.Select(p => p.node_id));
        Assert.Equal(places, detail.photos.Select(p => p.real_place_name));
        Assert.Equal(places, detail.photos.Select(p => p.place_name));
        Assert.All(detail.photos, p => Assert.True(p.is_place_photo));
        Assert.Equal(first, detail.cover_image_url);
        Assert.Equal(places, detail.spots);
        Assert.DoesNotContain("img.test/dome.jpg", json);
        Assert.DoesNotContain("img.test/lantern.jpg", json);
    }

    [Fact]
    public async Task 景點對不到MySQL的place表_real_place_name還是景點的真實名稱_place_name是章節標題()
    {
        // AI 生成的劇本，景點多半不在 MySQL place 表：照片從 Neo4j 拿，名稱只剩 place_type 對得到
        int user = _api.NewUserId();
        var (storyId, _, places) = await CreateFinishedStoryAsync(user, DateTime.Now);
        List<string> uids = await QueryAsync<string>("SELECT place_id FROM story_node WHERE s_id = @storyId ORDER BY sn_order;", new { storyId });
        await _api.ExecuteAsync("DELETE FROM place WHERE p_name IN @places;", new { places });
        _api.FakeNeo4j.Rows = uids
            .Select(uid => new Dictionary<string, object> { ["uid"] = uid, ["image_url"] = $"https://neo4j.test/{uid}.jpg" })
            .ToList();

        HistoryStoryItem detail = await DetailAsync(user, storyId);

        Assert.Equal(uids.Select(uid => $"https://neo4j.test/{uid}.jpg"), detail.photos.Select(p => p.image_url));
        Assert.Equal(places, detail.photos.Select(p => p.real_place_name));
        Assert.Equal(new[] { "第1站", "第2站", "第3站" }, detail.photos.Select(p => p.place_name));
    }

    [Fact]
    public async Task 過往劇本詳情_景點都沒照片時photos是空陣列_玩家上傳的照片也不算()
    {
        int user = _api.NewUserId();
        var (storyId, nodes, _) = await CreateFinishedStoryAsync(user, DateTime.Now);
        await UploadPhotoAsync(user, storyId, nodes[0], "https://img.test/mine.jpg");

        HistoryStoryItem detail = await DetailAsync(user, storyId);

        Assert.Empty(detail.photos);
        Assert.Null(detail.cover_image_url);
    }

    [Fact]
    public async Task 還沒玩完的劇本不會拿到景點照片()
    {
        int user = _api.NewUserId();
        var (storyId, _, places) = await CreateFinishedStoryAsync(user, DateTime.Now);
        string photo = await _api.GivePlacePhotoAsync(places[0]);
        await _api.ExecuteAsync("UPDATE story_session SET ss_status = 'in_progress', ss_current = 0, completed_at = NULL WHERE s_id = @storyId AND au_id = @user;",
            new { storyId, user });

        string json = await (await _api.ClientFor(user).GetAsync($"/api/History/{storyId}")).Content.ReadAsStringAsync();

        // 沒抵達的站還在迷霧中，真實照片不能從這裡流出去
        Assert.DoesNotContain(photo, json);
    }
}
