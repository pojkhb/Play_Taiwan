// 地圖剪影 API：劇本節點 → 景點照片 → 去背實心剪影（靜態 PNG）→ 地圖回傳 silhouette_image_url
// 景點照片由 FakeAiService 提供（不連網路）
using System.Net;
using backend.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TrafficSystem.Tests.Silhouette;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
public class SilhouetteApiTests : IDisposable
{
    private readonly ApiFactory _api;
    private readonly List<string> _files = new();

    public SilhouetteApiTests(ApiFactory api) => _api = api;

    /// <summary>刪掉測試產生在 wwwroot/images/silhouettes/generated 的剪影檔</summary>
    public void Dispose()
    {
        foreach (string url in _files.Distinct())
        {
            string path = Path.Combine(ApiFactory.RepoRoot, "wwwroot", url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>準備一個有照片的景點：FakeAiService 提供照片，並寫進 place.p_image</summary>
    private async Task<(string place, string photoUrl)> PlaceWithPhotoAsync()
    {
        string id = Guid.NewGuid().ToString("N")[..8];
        string place = $"剪影測試塔{id}", photoUrl = $"https://img.test/tower-{id}.png";

        using var photo = SilhouetteImageHelperTests.TowerPhoto(240, 200);
        using var ms = new MemoryStream();
        await photo.SaveAsPngAsync(ms);
        _api.FakeAi.Images[photoUrl] = ms.ToArray();
        return (place, photoUrl);
    }

    private async Task<SilhouetteGenerateResult> GenerateAsync(HttpClient client, int storyId)
    {
        var result = (await (await client.PostAsync($"/api/Silhouette/Story/{storyId}/Generate", null)).ReadResultAsync<SilhouetteGenerateResult>()).Result;
        _files.AddRange(result.nodes.Where(n => n.silhouette_image_url != null).Select(n => n.silhouette_image_url));
        return result;
    }

    [Fact]
    public async Task 產生剪影後地圖節點帶剪影網址_圖片可以直接讀取()
    {
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);
        var (place, photoUrl) = await PlaceWithPhotoAsync();
        string noPhoto = $"沒有照片的景點{Guid.NewGuid():N}";
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, place, noPhoto);
        await _api.ExecuteAsync("UPDATE place SET p_image = @photoUrl WHERE p_name = @place;", new { photoUrl, place });

        // ── 產生剪影 ──
        SilhouetteGenerateResult result = await GenerateAsync(client, storyId);

        Assert.Equal(1, result.generated);
        SilhouetteNodeResult first = result.nodes[0], second = result.nodes[1];
        Assert.Equal("generated", first.status);
        Assert.StartsWith("/images/silhouettes/generated/", first.silhouette_image_url);
        Assert.InRange(first.area_ratio.Value, 0.25, 0.35);
        Assert.Equal("no_photo", second.status);

        // ── 地圖：有剪影的節點帶網址，沒照片的是 null ──
        MapResponse map = (await (await client.GetAsync($"/api/Map/{storyId}")).ReadResultAsync<MapResponse>()).Result;
        Assert.Equal(new[] { first.silhouette_image_url, null }, map.nodes.Select(n => n.silhouette_image_url));

        // ── 前端直接用網址讀圖：透明背景的 PNG ──
        HttpResponseMessage image = await _api.CreateClient().GetAsync(first.silhouette_image_url);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType.MediaType);
        using var png = Image.Load<Rgba32>(await image.Content.ReadAsByteArrayAsync());
        Assert.Equal(0, png[5, 5].A);                          // 天空是透明的

        // ── 再跑一次不會重做 ──
        Assert.Equal("already_linked", (await GenerateAsync(client, storyId)).nodes[0].status);
    }

    [Fact]
    public async Task 其他劇本用到同一張照片會沿用同一張剪影()
    {
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);
        var (place, photoUrl) = await PlaceWithPhotoAsync();
        int story1 = await _api.CreateStoryAsync(user, "臺北市", night: false, place);
        int story2 = await _api.CreateStoryAsync(user, "臺北市", night: true, place);
        await _api.ExecuteAsync("UPDATE place SET p_image = @photoUrl WHERE p_name = @place;", new { photoUrl, place });

        SilhouetteNodeResult a = (await GenerateAsync(client, story1)).nodes[0];
        SilhouetteNodeResult b = (await GenerateAsync(client, story2)).nodes[0];

        Assert.Equal("generated", a.status);
        Assert.Equal("reused", b.status);
        Assert.Equal(a.silhouette_image_url, b.silhouette_image_url);
        Assert.Equal(1, await _api.QueryAsync<int>("SELECT COUNT(*) FROM silhouette WHERE si_image_url = @photoUrl;", new { photoUrl }));
    }

    [Fact]
    public async Task 照片下載失敗時標記失敗_其他節點照常處理()
    {
        int user = _api.NewUserId();
        HttpClient client = _api.ClientFor(user);
        var (good, goodUrl) = await PlaceWithPhotoAsync();
        string broken = $"照片壞掉的景點{Guid.NewGuid():N}";
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, broken, good);
        await _api.ExecuteAsync("UPDATE place SET p_image = 'https://img.test/not-found.png' WHERE p_name = @broken;", new { broken });
        await _api.ExecuteAsync("UPDATE place SET p_image = @goodUrl WHERE p_name = @good;", new { goodUrl, good });

        SilhouetteGenerateResult result = await GenerateAsync(client, storyId);

        Assert.Equal("failed", result.nodes[0].status);
        Assert.Contains("下載景點照片失敗", result.nodes[0].message);
        Assert.Equal("generated", result.nodes[1].status);
    }

    [Fact]
    public async Task 不存在的劇本回傳錯誤()
    {
        var result = await (await _api.ClientFor(_api.NewUserId()).PostAsync("/api/Silhouette/Story/999999/Generate", null)).ReadResultAsync<string>();

        Assert.False(result.isSuccess);
        Assert.Contains("找不到劇本", result.message);
    }
}
