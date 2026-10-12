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
}
