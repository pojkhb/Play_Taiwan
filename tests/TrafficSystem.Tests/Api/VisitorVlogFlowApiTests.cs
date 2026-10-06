// 遊客 VLOG 送出合成（AI 服務由 FakeAiService 模擬）：
// 玩家沒拍照的站改用景點照片、一定附上背景音樂、景點資料一起送出
using System.IO.Compression;
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
