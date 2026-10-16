// 遊客 VLOG（VisitorVlogService）：zip 裡照片的命名、遊玩時長
using backend.dao;
using backend.Services;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Vlog;

[AllureSuiteHierarchy("VLOG 與回顧", "單元測試")]
[AllureBddHierarchy("VLOG 與回顧", "單元測試")]
public class VisitorVlogServiceTests
{
    #region zip 照片命名 + 每張照片的地點資料

    private static VisitorVlogSpot Spot(int order) => new() { order = order, spot_name = $"第{order}站", images = new List<string>() };

    [Fact]
    public void 照片依景點順序命名_沒掛景點的排在最後()
    {
        VisitorVlogSpot first = Spot(1), third = Spot(3);
        var photos = new List<(VisitorVlogSpot spot, string url)>
        {
            (third, "https://cdn/a.png"),
            (first, "https://cdn/b.jpg"),
            (null, "https://cdn/c.jpg"),
            (first, "https://cdn/d.webp"),
        };

        var named = VisitorVlogService.NamePhotos(photos);

        Assert.Equal(new[]
        {
            ("01_01.jpg", "https://cdn/b.jpg"),
            ("01_02.webp", "https://cdn/d.webp"),
            ("03_01.png", "https://cdn/a.png"),
            ("99_01.jpg", "https://cdn/c.jpg"),
        }, named.Select(p => (p.meta.file_name, p.url)));
    }

    [Fact]
    public void 每張照片各一筆地點資料_沒掛景點的顯示旅程回憶()
    {
        VisitorVlogSpot first = Spot(1);
        first.location_codename = "紅磚眼科";
        first.visit_time = "2026-10-12 21:48";

        var named = VisitorVlogService.NamePhotos(new List<(VisitorVlogSpot, string)>
        {
            (first, "b.jpg"), (null, "c.jpg"), (first, "d.webp"),
        });

        Assert.Equal(new[] { "第1站", "第1站", VisitorVlogService.UnassignedSpotName }, named.Select(p => p.meta.spot_name));
        Assert.All(named.Take(2), p =>
        {
            Assert.Equal("紅磚眼科", p.meta.location_codename);
            Assert.Equal("2026-10-12 21:48", p.meta.visit_time);
        });
        Assert.Null(named[2].meta.location_codename);
    }

    #endregion

    #region 遊玩時長

    private static readonly DateTime Start = new(2026, 9, 29, 10, 0, 0);

    private static VisitorVlogDao.SpotRow Row(DateTime? first, DateTime? last) => new() { first_at = first, last_at = last };

    [Fact]
    public void 超過一小時顯示小時()
    {
        var session = new VisitorVlogDao.SessionRow { started_at = Start, completed_at = Start.AddMinutes(150) };

        Assert.Equal("2.5小時", VisitorVlogService.PlayTime(session, new List<VisitorVlogDao.SpotRow>()));
    }

    [Fact]
    public void 不到一小時顯示分鐘()
    {
        var session = new VisitorVlogDao.SessionRow { started_at = Start, completed_at = Start.AddMinutes(45) };

        Assert.Equal("45分鐘", VisitorVlogService.PlayTime(session, new List<VisitorVlogDao.SpotRow>()));
    }

    [Fact]
    public void 還沒完成時用最後互動時間()
    {
        var session = new VisitorVlogDao.SessionRow { started_at = Start, last_played_at = Start.AddMinutes(90) };

        Assert.Equal("1.5小時", VisitorVlogService.PlayTime(session, new List<VisitorVlogDao.SpotRow>()));
    }

    [Fact]
    public void 沒有遊玩紀錄時用作答時間推算()
    {
        var rows = new List<VisitorVlogDao.SpotRow>
        {
            Row(Start.AddHours(3), Start.AddHours(3.5)),
            Row(Start.AddHours(2), Start.AddHours(2.2)),
        };

        Assert.Equal("1.5小時", VisitorVlogService.PlayTime(null, rows));
    }

    [Fact]
    public void 完全沒有時間資料時用預設值()
    {
        Assert.Equal("2.5小時", VisitorVlogService.PlayTime(null, new List<VisitorVlogDao.SpotRow> { Row(null, null) }));
    }

    [Fact]
    public void 結束時間早於開始時間時用預設值()
    {
        var session = new VisitorVlogDao.SessionRow { started_at = Start, completed_at = Start.AddMinutes(-5) };

        Assert.Equal("2.5小時", VisitorVlogService.PlayTime(session, new List<VisitorVlogDao.SpotRow>()));
    }

    #endregion
}
