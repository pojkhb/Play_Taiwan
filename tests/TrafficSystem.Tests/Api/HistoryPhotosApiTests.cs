// 過往紀錄的照片：GET /api/History 卡片封面、GET /api/History/{story_id} 上方左右滑動的照片，都是玩家在劇本裡拍的照片
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

    public HistoryPhotosApiTests(ApiFactory api) => _api = api;

    /// <summary>建立一個玩完的 3 站劇本，回傳 (story_id, 每站的 sn_id, 景點名稱)</summary>
    private async Task<(int storyId, int[] nodes, string[] places)> CreateFinishedStoryAsync(int user, DateTime completedAt)
    {
        string[] places = Enumerable.Range(1, 3).Select(i => $"過往景點{i}_{Guid.NewGuid():N}"[..14]).ToArray();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, places);
        int[] nodes = (await QueryIntsAsync("SELECT sn_id FROM story_node WHERE s_id = @storyId ORDER BY sn_order;", new { storyId })).ToArray();

        await _api.ExecuteAsync(@"
            INSERT INTO story_session (au_id, s_id, ss_current, ss_status, started_at, completed_at)
            VALUES (@user, @storyId, 3, 'completed', @startedAt, @completedAt);",
            new { user, storyId, startedAt = completedAt.AddHours(-2), completedAt });
        return (storyId, nodes, places);
    }

    private async Task<List<int>> QueryIntsAsync(string sql, object param)
    {
        using var conn = new MySql.Data.MySqlClient.MySqlConnection(_api.ConnectionString);
        return (await Dapper.SqlMapper.QueryAsync<int>(conn, sql, param)).ToList();
    }

    private async Task<int> InsertTaskAsync(int storyId, int nodeId, int type)
    {
        await _api.ExecuteAsync("INSERT INTO task (story_id, node_id, task_type, task_describe) VALUES (@storyId, @nodeId, @type, '和景點合照');",
            new { storyId, nodeId, type });
        return await _api.QueryAsync<int>("SELECT MAX(task_id) FROM task WHERE story_id = @storyId;", new { storyId });
    }

    /// <summary>一筆作答紀錄：第一個素材存 answer_media_url，其餘存 record_media（跟正式作答一樣）</summary>
    private async Task InsertRecordAsync(int user, int taskId, int minutesAgo, params string[] media)
    {
        int recordId = _api.NewUserId();   // record_id 不是自動編號，借用遞增的號碼
        await _api.ExecuteAsync(@"
            INSERT INTO user_task_record (record_id, au_id, task_id, answer_media_url, answered_at)
            VALUES (@recordId, @user, @taskId, @first, NOW() - INTERVAL @minutesAgo MINUTE);",
            new { recordId, user, taskId, first = media.FirstOrDefault(), minutesAgo });
        foreach (string url in media.Skip(1))
            await _api.ExecuteAsync("INSERT INTO record_media (media_id, record_id, media_url) VALUES (@id, @recordId, @url);",
                new { id = _api.NewUserId(), recordId, url });
    }

    private async Task<List<HistoryStoryItem>> ListAsync(int user) =>
        (await (await _api.ClientFor(user).GetAsync("/api/History")).ReadResultAsync<List<HistoryStoryItem>>()).Result;

    private async Task<HistoryStoryItem> DetailAsync(int user, int storyId) =>
        (await (await _api.ClientFor(user).GetAsync($"/api/History/{storyId}")).ReadResultAsync<HistoryStoryItem>()).Result;

    [Fact]
    public async Task 過往紀錄卡片用玩家拍的第一張照片當封面_沒拍照片時是null()
    {
        int user = _api.NewUserId();
        var (withPhotos, nodes, _) = await CreateFinishedStoryAsync(user, DateTime.Now.AddDays(-1));
        var (noPhotos, _, _) = await CreateFinishedStoryAsync(user, DateTime.Now);

        // 第 3 站比第 1 站早拍，封面還是取第 1 站的照片（依站的順序）
        await InsertRecordAsync(user, await InsertTaskAsync(withPhotos, nodes[2], 3), 90, "https://img.test/third.jpg");
        await InsertRecordAsync(user, await InsertTaskAsync(withPhotos, nodes[0], 3), 30, "https://img.test/first.jpg");

        List<HistoryStoryItem> list = await ListAsync(user);

        Assert.Equal(new[] { noPhotos, withPhotos }, list.Select(s => s.story_id));   // 最近玩完的在前面
        Assert.Equal("https://img.test/first.jpg", list[1].cover_image_url);
        Assert.Null(list[0].cover_image_url);
        Assert.All(list, s => Assert.Null(s.photos));
    }

    [Fact]
    public async Task 過往劇本詳情回傳這趟拍的所有照片_依站的順序並附景點名稱()
    {
        int user = _api.NewUserId();
        int other = _api.NewUserId();
        var (storyId, nodes, places) = await CreateFinishedStoryAsync(user, DateTime.Now);

        int photo1 = await InsertTaskAsync(storyId, nodes[0], 3);
        int photo3 = await InsertTaskAsync(storyId, nodes[2], 3);
        int quiz2 = await InsertTaskAsync(storyId, nodes[1], 6);

        // 第 1 站：一次上傳照片、第二張照片、影片；重拍一次同一個網址
        await InsertRecordAsync(user, photo1, 60, "https://img.test/dome.jpg", "https://img.test/dome-2.png", "https://img.test/clip.mp4");
        await InsertRecordAsync(user, photo1, 50, "https://img.test/dome.jpg");
        await InsertRecordAsync(user, quiz2, 40);
        // 第 3 站：相對路徑補成完整網址
        await InsertRecordAsync(user, photo3, 20, "/uploads/lantern.webp");
        // 別人在同一個劇本拍的照片不算
        await InsertRecordAsync(other, photo1, 10, "https://img.test/someone-else.jpg");

        HistoryStoryItem detail = await DetailAsync(user, storyId);

        Assert.Equal(new[] { "https://img.test/dome.jpg", "https://img.test/dome-2.png", "http://localhost/uploads/lantern.webp" },
            detail.photos.Select(p => p.image_url));
        Assert.Equal(new[] { 1, 1, 3 }, detail.photos.Select(p => p.node_order));
        Assert.Equal(new[] { nodes[0], nodes[0], nodes[2] }, detail.photos.Select(p => p.node_id));
        Assert.Equal(new[] { places[0], places[0], places[2] }, detail.photos.Select(p => p.place_name));
        Assert.Equal("https://img.test/dome.jpg", detail.cover_image_url);
        Assert.Equal(places, detail.spots);
    }

    [Fact]
    public async Task 過往劇本詳情_沒拍照片時photos是空陣列()
    {
        int user = _api.NewUserId();
        var (storyId, _, _) = await CreateFinishedStoryAsync(user, DateTime.Now);

        HistoryStoryItem detail = await DetailAsync(user, storyId);

        Assert.Empty(detail.photos);
        Assert.Null(detail.cover_image_url);
    }
}
