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
    #region zip 照片命名

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

        var entries = VisitorVlogService.NameZipEntries(photos);

        Assert.Equal(new[]
        {
            ("01_01.jpg", "https://cdn/b.jpg"),
            ("01_02.webp", "https://cdn/d.webp"),
            ("03_01.png", "https://cdn/a.png"),
            ("99_01.jpg", "https://cdn/c.jpg"),
        }, entries);
    }

    [Fact]
    public void 檔名會寫回各景點_讓AI對得上照片()
    {
        VisitorVlogSpot first = Spot(1), third = Spot(3);

        VisitorVlogService.NameZipEntries(new List<(VisitorVlogSpot, string)>
        {
            (third, "a.png"), (first, "b.jpg"), (first, "d.webp"),
        });

        Assert.Equal(new[] { "01_01.jpg", "01_02.webp" }, first.images);
        Assert.Equal(new[] { "03_01.png" }, third.images);
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
