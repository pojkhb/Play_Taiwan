// 遊客 VLOG（AI 服務由 FakeAiService 模擬）：
// 旁白草稿回傳每一站會放進影片的照片網址；送出合成時玩家沒拍照的站改用景點照片、一定附上背景音樂、景點資料一起送出
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("VLOG 與回顧", "API 整合測試")]
[AllureBddHierarchy("VLOG 與回顧", "API 整合測試")]
public class VisitorVlogFlowApiTests
{
    private readonly ApiFactory _api;

    public VisitorVlogFlowApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
    }

    [Fact]
    public async Task 旁白草稿回傳每一站的照片網址_沒拍照的站給景點照片_還沒抵達的站不給真實照片()
    {
        int user = _api.NewUserId();
        string[] places = Enumerable.Range(1, 3).Select(i => $"草稿景點{i}_{Guid.NewGuid():N}"[..14]).ToArray();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, places);
        string[] placePhotos = { await _api.GivePlacePhotoAsync(places[0]), await _api.GivePlacePhotoAsync(places[1]), await _api.GivePlacePhotoAsync(places[2]) };
        HttpClient client = _api.ClientFor(user);

        // 走到第 2 站（第 3 站還沒抵達）；玩家只在第 1 站拍了一張
        int[] nodes = { await NodeAsync(storyId, 1), await NodeAsync(storyId, 2) };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/Map/Node/{nodes[0]}/Arrive?lat=25.03&lng=121.5", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/Map/Node/{nodes[1]}/Arrive?lat=25.031&lng=121.5", null)).StatusCode);
        string selfie = $"/uploads/selfie-{Guid.NewGuid():N}.jpg";
        await AddPlayerPhotoAsync(user, storyId, nodes[0], selfie);

        List<VisitorVlogSpot> spots = await PreviewAsync(client, storyId);

        Assert.Equal(places, spots.Select(s => s.spot_name));
        // 第 1 站：玩家拍的（站內路徑會組成完整網址），有自己的照片就不用景點照片
        Assert.Equal(new[] { "http://localhost" + selfie }, spots[0].images);
        Assert.Equal((1, false), (spots[0].photo_count, spots[0].uses_place_photo));
        // 第 2 站：抵達過但沒拍照 → 景點照片
        Assert.Equal(new[] { placePhotos[1] }, spots[1].images);
        Assert.Equal((1, true), (spots[1].photo_count, spots[1].uses_place_photo));
        // 第 3 站：還沒抵達，迷霧還在 → 不給景點的真實照片（影片素材的張數照算）
        Assert.Empty(spots[2].images);
        Assert.Equal((1, true), (spots[2].photo_count, spots[2].uses_place_photo));

        // 整份劇本玩完之後迷霧都散開，第 3 站的景點照片也回傳
        await _api.ExecuteAsync(@"
            INSERT INTO story_session (au_id, s_id, ss_current, ss_status, started_at, completed_at)
            VALUES (@user, @storyId, 2, 'completed', NOW() - INTERVAL 90 MINUTE, NOW());", new { user, storyId });

        spots = await PreviewAsync(client, storyId);

        Assert.Equal(new[] { placePhotos[2] }, spots[2].images);
        Assert.Equal(new[] { "http://localhost" + selfie }, spots[0].images);
    }

    private Task<int> NodeAsync(int storyId, int order) =>
        _api.QueryAsync<int>("SELECT sn_id FROM story_node WHERE s_id = @storyId AND sn_order = @order;", new { storyId, order });

    /// <summary>玩家在這一站的拍照任務交了一張照片</summary>
    private async Task AddPlayerPhotoAsync(int user, int storyId, int nodeId, string url)
    {
        await _api.ExecuteAsync("INSERT INTO task (story_id, node_id, task_type, task_describe) VALUES (@storyId, @nodeId, 3, '和景點合照');", new { storyId, nodeId });
        int taskId = await _api.QueryAsync<int>("SELECT MAX(task_id) FROM task WHERE story_id = @storyId;", new { storyId });
        await _api.ExecuteAsync(@"
            INSERT INTO user_task_record (record_id, au_id, task_id, is_correct, answer_media_url)
            VALUES (@recordId, @user, @taskId, 1, @url);", new { recordId = _api.NewUserId(), user, taskId, url });   // record_id 不是自動編號，借用遞增的號碼
    }

    private static async Task<List<VisitorVlogSpot>> PreviewAsync(HttpClient client, int storyId)
    {
        ApiResult<VisitorVlogPreviewResponse> result = await (await client.PostAsJsonAsync("/api/VisitorVlog/Preview", new { story_id = storyId }))
            .ReadResultAsync<VisitorVlogPreviewResponse>();
        Assert.True(result.isSuccess, result.message);
        Assert.Equal("今天跟著鐘聲走了一趟舊城。", result.Result.script);
        Assert.Equal(3, result.Result.photo_count);
        return result.Result.spots;
    }

    [Fact]
    public async Task 玩家沒拍照時用景點照片合成_並附上背景音樂()
    {
        int user = _api.NewUserId();
        string withPhoto = $"有照片的景點{Guid.NewGuid():N}", noPhoto = $"沒照片的景點{Guid.NewGuid():N}";
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, withPhoto, noPhoto);
        string photoUrl = await _api.GivePlacePhotoAsync(withPhoto);

        HttpResponseMessage response = await _api.ClientFor(user)
            .PostAsJsonAsync("/api/VisitorVlog/CreateFinal", new { story_id = storyId, final_script = "確認後的旁白" });
        ApiResult<VisitorVlogTaskResponse> result = await response.ReadResultAsync<VisitorVlogTaskResponse>();

        Assert.True(result.isSuccess, result.message);
        Assert.Equal(1, result.Result.photo_count);

        FakeAiService.Received sent = _api.FakeAi.Last("/api/visitor/vlog/create_final");
        using (var zip = new ZipArchive(new MemoryStream(sent.Files["image_zip"])))
            Assert.Equal(new[] { "01_01.png" }, zip.Entries.Select(e => e.FullName));   // 第 1 站的景點照片

        byte[] bgm = File.ReadAllBytes(Path.Combine(ApiFactory.RepoRoot, "wwwroot", "bgm", "default_bgm.mp3"));
        Assert.Equal(bgm, sent.Files["bgm_file"]);

        JsonElement spots = JsonDocument.Parse(sent.Fields["spot_meta_json"]).RootElement;
        Assert.True(spots[0].GetProperty("uses_place_photo").GetBoolean());
        Assert.Equal("01_01.png", spots[0].GetProperty("images")[0].GetString());
        Assert.Equal(0, spots[1].GetProperty("photo_count").GetInt32());
    }

    [Fact]
    public async Task 玩家和景點都沒有照片時不送出合成()
    {
        int user = _api.NewUserId();
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, $"沒照片的景點{Guid.NewGuid():N}");

        ApiResult<string> result = await (await _api.ClientFor(user)
            .PostAsJsonAsync("/api/VisitorVlog/CreateFinal", new { story_id = storyId, final_script = "旁白" })).ReadResultAsync<string>();

        Assert.False(result.isSuccess);
        Assert.Contains("無法合成影片", result.message);
        Assert.Null(_api.FakeAi.Last("/api/visitor/vlog/create_final"));
    }
}
