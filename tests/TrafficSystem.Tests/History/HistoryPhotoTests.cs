// 過往紀錄的照片：每一站的景點照片（依站的順序、景點沒有照片的站不列出），附景點的真實名稱
using Allure.Net.Commons.Attributes;
using backend.dao;
using backend.Services;

namespace TrafficSystem.Tests.History;

[AllureSuiteHierarchy("VLOG 與回顧", "單元測試")]
[AllureBddHierarchy("VLOG 與回顧", "單元測試")]
public class HistoryPhotoTests
{
    private static HistoryDao.StationRow Station(int story, int order) =>
        new()
        {
            story_id = story, node_id = story * 10 + order, node_order = order,
            place_name = $"第{order}站", real_place_name = $"景點{order}", place_id = $"place-{story}-{order}"
        };

    [Fact]
    public void 每一站一張景點照片_依站的順序排好_附景點的真實名稱()
    {
        // 第 4 站的景點沒有照片；站的順序故意打亂
        var stations = new[] { Station(1, 3), Station(1, 1), Station(1, 4), Station(1, 2) };
        var placeImages = new Dictionary<string, string>
        {
            ["place-1-1"] = "https://place.test/1.jpg",
            ["place-1-2"] = "https://place.test/2.jpg",
            ["place-1-3"] = "https://place.test/3.jpg",
        };

        var photos = HistoryService.PlacePhotos(stations, placeImages)[1];

        Assert.Equal(new[] { "https://place.test/1.jpg", "https://place.test/2.jpg", "https://place.test/3.jpg" }, photos.Select(p => p.image_url));
        Assert.Equal(new[] { 1, 2, 3 }, photos.Select(p => p.node_order));
        Assert.Equal(new[] { "景點1", "景點2", "景點3" }, photos.Select(p => p.real_place_name));
        Assert.Equal(new[] { "第1站", "第2站", "第3站" }, photos.Select(p => p.place_name));
        Assert.All(photos, p => Assert.True(p.is_place_photo));
        Assert.Equal("https://place.test/1.jpg", HistoryService.Cover(photos));
    }

    [Fact]
    public void 封面是第一張有照片的景點_景點都沒照片的劇本不會出現在結果裡()
    {
        var stations = new[] { Station(1, 1), Station(1, 2), Station(2, 1) };
        var placeImages = new Dictionary<string, string> { ["place-1-2"] = "https://place.test/2.jpg" };

        var photos = HistoryService.PlacePhotos(stations, placeImages);

        Assert.Equal("https://place.test/2.jpg", HistoryService.Cover(photos[1]));
        Assert.False(photos.ContainsKey(2));
        Assert.Null(HistoryService.Cover(new List<backend.Models.HistoryPhoto>()));
    }

    [Fact]
    public void 對不到景點的站不列出()
    {
        var noPlace = Station(1, 1);
        noPlace.place_id = null;

        var photos = HistoryService.PlacePhotos(new[] { noPlace }, new Dictionary<string, string>());

        Assert.Empty(photos);
    }
}
