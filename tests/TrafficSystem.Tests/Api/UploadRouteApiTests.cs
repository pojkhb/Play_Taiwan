// 檔案上傳（自選地點的交通方式可用性在 RouteAvailabilityApiTests）
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("明信片與上傳", "API 整合測試")]
[AllureBddHierarchy("明信片與上傳", "API 整合測試")]
public class UploadRouteApiTests
{
    private readonly ApiFactory _api;

    public UploadRouteApiTests(ApiFactory api) => _api = api;

    private static MultipartFormDataContent File(string name, string contentType, byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", name } };
    }

    [Fact]
    public async Task 上傳照片回傳可以讀取的網址()
    {
        using var form = File("photo.jpg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 });

        var result = await (await _api.ClientFor(_api.NewUserId()).PostAsync("/api/Upload", form)).ReadResultAsync<string>();

        Assert.True(result.isSuccess, result.message);
        string path = new Uri(new Uri("http://localhost"), result.Result).AbsolutePath;
        Assert.StartsWith("/uploads/", path);

        string physical = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.True(System.IO.File.Exists(physical));
        System.IO.File.Delete(physical);
    }

    [Fact]
    public async Task 不允許的檔案類型會被擋下()
    {
        using var form = File("virus.exe", "application/octet-stream", new byte[] { 0x4D, 0x5A });

        var result = await (await _api.ClientFor(_api.NewUserId()).PostAsync("/api/Upload", form)).ReadResultAsync<string>();

        Assert.False(result.isSuccess);
    }

    [Fact]
    public async Task 沒登入也能上傳照片給商家註冊用()
    {
        using var form = File("store.png", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var result = await (await _api.ClientFor(null).PostAsync("/api/Upload", form)).ReadResultAsync<string>();

        Assert.True(result.isSuccess, result.message);
        string path = new Uri(new Uri("http://localhost"), result.Result).AbsolutePath;
        Assert.EndsWith(".png", path);

        string physical = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.True(System.IO.File.Exists(physical));
        System.IO.File.Delete(physical);
    }

    [Fact]
    public async Task 沒登入不能上傳影片()
    {
        using var form = File("clip.mp4", "video/mp4", new byte[] { 0, 0, 0, 0x18 });

        var response = await _api.ClientFor(null).PostAsync("/api/Upload", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await response.ReadResultAsync<string>()).isSuccess);
    }

    [Fact]
    public async Task 沒登入上傳標成照片的網頁會存成照片副檔名()
    {
        using var form = File("evil.html", "image/jpeg", "<script>alert(1)</script>"u8.ToArray());

        var result = await (await _api.ClientFor(null).PostAsync("/api/Upload", form)).ReadResultAsync<string>();

        Assert.True(result.isSuccess, result.message);
        string path = new Uri(new Uri("http://localhost"), result.Result).AbsolutePath;
        Assert.EndsWith(".jpg", path);

        System.IO.File.Delete(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
    }
}
