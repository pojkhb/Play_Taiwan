// 周邊好去：GET /api/Map/{story_id}/Nearby（依使用者目前位置推薦，沒帶位置不推薦，劇本自己的站不列）
using System.Globalization;
using System.Net;
using Allure.Net.Commons.Attributes;
using backend.Models;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("解謎地圖與迷霧", "API 整合測試")]
[AllureBddHierarchy("解謎地圖與迷霧", "API 整合測試")]
public class NearbyPlacesApiTests
{
    private readonly ApiFactory _api;

    public NearbyPlacesApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
    }

    // 每個測試用不同的中心點（彼此相距 20 公里以上），互不干擾；附近也沒有其他測試建立的景點
    private static (double lat, double lng) Center(int n) => (23.3 + n * 0.2, 121.4);

    private const double MetersPerDegree = 111320.0;

    /// <summary>在中心點正北方 meters 公尺建立一個地點；category：飲食、其他，或 null（景點）</summary>
    private async Task<string> AddPlaceAsync((double lat, double lng) center, double meters, string category)
    {
        string name = $"周邊測試{Guid.NewGuid():N}";
        await _api.ExecuteAsync(@"
            INSERT INTO place (p_name, p_category, p_type, p_address, p_latitude, p_longitude, p_image)
            VALUES (@name, @category, '古蹟', '花蓮縣測試路 1 號', @lat, @lng, '/images/places/sample.jpg');",
            new { name, category, lat = center.lat + meters / MetersPerDegree, lng = center.lng });
        return name;
    }

    private async Task<(List<NearbyPlaceResponse> places, string message)> NearbyAsync(int storyId, (double lat, double lng)? at, string query = "")
    {
        string position = at == null
            ? ""
            : $"lat={at.Value.lat.ToString(CultureInfo.InvariantCulture)}&lng={at.Value.lng.ToString(CultureInfo.InvariantCulture)}";
        string url = $"/api/Map/{storyId}/Nearby?{position}{(position != "" && query != "" ? "&" : "")}{query}";

        var result = await (await _api.ClientFor(_api.NewUserId()).GetAsync(url)).ReadResultAsync<List<NearbyPlaceResponse>>();
        Assert.True(result.isSuccess, result.message);
        return (result.Result, result.message);
    }

    private Task<int> AnyStoryAsync() => _api.CreateStoryAsync(_api.NewUserId(), "花蓮縣", night: false, $"周邊劇本站{Guid.NewGuid():N}");

    [Fact]
    public async Task 沒帶目前位置時不推薦()
    {
        int storyId = await AnyStoryAsync();
        await AddPlaceAsync(Center(0), 100, "其他");

        var (places, message) = await NearbyAsync(storyId, null);

        Assert.Empty(places);
        Assert.Contains("目前位置", message);
    }

    [Fact]
    public async Task 依目前位置由近到遠推薦_太遠的不列_可以用分類篩選()
    {
        var center = Center(1);
        int storyId = await AnyStoryAsync();
        string spot = await AddPlaceAsync(center, 100, "其他");
        string food = await AddPlaceAsync(center, 300, "飲食");
        string sight = await AddPlaceAsync(center, 600, null);
        string far = await AddPlaceAsync(center, 2000, "其他");

        var (all, _) = await NearbyAsync(storyId, center);

        Assert.Equal(new[] { spot, food, sight }, all.Select(p => p.name));
        Assert.Equal(new[] { "其他", "飲食", "其他" }, all.Select(p => p.category));
        Assert.InRange(all[0].distance_m, 95, 105);
        Assert.InRange(all[2].distance_m, 590, 610);
        Assert.DoesNotContain(all, p => p.name == far);

        // 照片是完整網址，並附導航連結
        Assert.Equal("http://localhost/images/places/sample.jpg", Assert.Single(all[0].photo_urls));
        Assert.StartsWith("https://www.google.com/maps/search/", all[0].maps_deeplink_url);
        Assert.StartsWith("place-", all[0].place_uid);
        Assert.NotEqual(0, all[0].place_id);

        Assert.Equal(new[] { food }, (await NearbyAsync(storyId, center, "category=飲食")).places.Select(p => p.name));
        Assert.Equal(new[] { spot, sight }, (await NearbyAsync(storyId, center, "category=其他")).places.Select(p => p.name));

        // 半徑放大就找得到遠的；limit 限制筆數
        Assert.Contains((await NearbyAsync(storyId, center, "radius_m=2500")).places, p => p.name == far);
        Assert.Equal(new[] { spot }, (await NearbyAsync(storyId, center, "limit=1")).places.Select(p => p.name));
    }

    [Fact]
    public async Task 使用者移動後_推薦跟著換成新位置附近的()
    {
        var here = Center(2);
        var there = Center(3);
        int storyId = await AnyStoryAsync();
        string nearHere = await AddPlaceAsync(here, 150, "其他");
        string nearThere = await AddPlaceAsync(there, 150, "其他");

        Assert.Equal(new[] { nearHere }, (await NearbyAsync(storyId, here)).places.Select(p => p.name));
        Assert.Equal(new[] { nearThere }, (await NearbyAsync(storyId, there)).places.Select(p => p.name));
    }

    [Fact]
    public async Task 這份劇本自己的站不會出現在周邊好去()
    {
        var center = Center(4);
        string[] stations = Enumerable.Range(1, 3).Select(i => $"周邊劇本站{i}_{Guid.NewGuid():N}").ToArray();
        int storyId = await _api.CreateStoryAsync(_api.NewUserId(), "花蓮縣", night: false, stations);
        int otherStory = await AnyStoryAsync();
        string nearby = await AddPlaceAsync(center, 200, "其他");

        // 把三站都搬到使用者附近（第 2、3 站還在迷霧中）
        await _api.ExecuteAsync("UPDATE place SET p_latitude = @lat, p_longitude = @lng WHERE p_name IN @stations;",
            new { lat = center.lat + 100 / MetersPerDegree, center.lng, stations });

        var (places, _) = await NearbyAsync(storyId, center);

        Assert.Equal(new[] { nearby }, places.Select(p => p.name));

        // 換成別的劇本來看，這些就只是一般的景點
        var (forOtherStory, _) = await NearbyAsync(otherStory, center);
        Assert.Equal(4, forOtherStory.Count);
        Assert.All(stations, station => Assert.Contains(forOtherStory, p => p.name == station));
    }

    [Fact]
    public async Task 位置不合理時回傳400()
    {
        int storyId = await AnyStoryAsync();

        HttpResponseMessage response = await _api.ClientFor(_api.NewUserId()).GetAsync($"/api/Map/{storyId}/Nearby?lat=0&lng=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
