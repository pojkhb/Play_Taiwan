// 過往紀錄的照片：素材 → 每份劇本的照片（只留照片、同一劇本同一網址只留一次、保留順序）
using Allure.Net.Commons.Attributes;
using backend.dao;
using backend.Services;

namespace TrafficSystem.Tests.History;

[AllureSuiteHierarchy("VLOG 與回顧", "單元測試")]
[AllureBddHierarchy("VLOG 與回顧", "單元測試")]
public class HistoryPhotoTests
{
    private static HistoryDao.MediaRow Row(int story, int order, string url) =>
        new() { story_id = story, node_id = story * 10 + order, node_order = order, place_name = $"景點{order}", url = url };

    [Fact]
    public void 只留照片_影片錄音不算_同一劇本重複的網址只留一次()
    {
        var photos = HistoryService.ToPhotos(new[]
        {
            Row(1, 1, "https://img.test/a.jpg"),
            Row(1, 1, "https://img.test/clip.mp4"),
            Row(1, 2, "https://img.test/voice.m4a"),
            Row(1, 2, "https://img.test/a.jpg"),
            Row(1, 3, "https://img.test/b.heic"),
        });

        Assert.Equal(new[] { "https://img.test/a.jpg", "https://img.test/b.heic" }, photos[1].Select(p => p.image_url));
        Assert.Equal(new[] { 1, 3 }, photos[1].Select(p => p.node_order));
    }

    [Fact]
    public void 不同劇本用到同一張照片_各自都保留()
    {
        var photos = HistoryService.ToPhotos(new[]
        {
            Row(1, 1, "https://img.test/same.png"),
            Row(2, 1, "https://img.test/same.png"),
        });

        Assert.Single(photos[1]);
        Assert.Single(photos[2]);
    }

    [Fact]
    public void 沒有任何照片的劇本不會出現在結果裡()
    {
        var photos = HistoryService.ToPhotos(new[] { Row(1, 1, "https://video.test/only.mp4") });

        Assert.False(photos.ContainsKey(1));
    }

    private static HistoryDao.StationRow Station(int story, int order) =>
        new() { story_id = story, node_id = story * 10 + order, node_order = order, place_name = $"景點{order}", place_id = $"place-{story}-{order}" };

    [Fact]
    public void 玩家沒拍照片的站補上景點照片_依站的順序排好()
    {
        // 玩家在第 2 站拍了兩張；第 1、3 站的景點有照片，第 4 站的景點沒有照片
        var taken = HistoryService.ToPhotos(new[] { Row(1, 2, "https://img.test/mine-1.jpg"), Row(1, 2, "https://img.test/mine-2.jpg") });
        var stations = Enumerable.Range(1, 4).Select(order => Station(1, order));
        var placeImages = new Dictionary<string, string>
        {
            ["place-1-1"] = "https://place.test/1.jpg",
            ["place-1-2"] = "https://place.test/2.jpg",
            ["place-1-3"] = "https://place.test/3.jpg",
        };

        var photos = HistoryService.AddPlacePhotos(taken, stations, placeImages)[1];

        Assert.Equal(new[] { "https://place.test/1.jpg", "https://img.test/mine-1.jpg", "https://img.test/mine-2.jpg", "https://place.test/3.jpg" },
            photos.Select(p => p.image_url));
        Assert.Equal(new[] { true, false, false, true }, photos.Select(p => p.is_place_photo));
        Assert.Equal(new[] { "景點1", "景點2", "景點2", "景點3" }, photos.Select(p => p.place_name));
        Assert.Equal("https://img.test/mine-1.jpg", HistoryService.Cover(photos));   // 封面優先用玩家拍的
    }

    [Fact]
    public void 沒拍照片的劇本_封面是第一張景點照片_景點也沒照片就沒有封面()
    {
        var none = new Dictionary<int, List<backend.Models.HistoryPhoto>>();
        var stations = new[] { Station(1, 1), Station(1, 2), Station(2, 1) };
        var placeImages = new Dictionary<string, string> { ["place-1-2"] = "https://place.test/2.jpg" };

        var photos = HistoryService.AddPlacePhotos(none, stations, placeImages);

        Assert.Equal("https://place.test/2.jpg", HistoryService.Cover(photos[1]));
        Assert.False(photos.ContainsKey(2));
        Assert.Null(HistoryService.Cover(new List<backend.Models.HistoryPhoto>()));
    }
}
