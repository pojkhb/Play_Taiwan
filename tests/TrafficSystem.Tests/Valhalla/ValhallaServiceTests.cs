// Valhalla 相關的純邏輯（ValhallaService）：交通方式對應、等時圈判斷、路線形狀解碼。不需要真的開 Valhalla。
using backend.Services;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Valhalla;

[AllureSuiteHierarchy("交通路網", "單元測試")]
[AllureBddHierarchy("交通路網", "單元測試")]
public class ValhallaServiceTests
{
    #region 交通方式 → Valhalla costing

    [Theory]
    [InlineData("步行", "pedestrian")]
    [InlineData("腳踏車", "bicycle")]
    [InlineData("自行車", "bicycle")]
    [InlineData("騎車", "bicycle")]
    [InlineData("機車", "motor_scooter")]
    [InlineData(" 機車 ", "motor_scooter")]
    [InlineData("汽車", "auto")]
    [InlineData("自駕", "auto")]
    [InlineData("開車", "auto")]
    [InlineData("其他", "auto")]
    [InlineData("公車", "bus")]
    [InlineData("捷運", "bus")]
    [InlineData("飛機", "pedestrian")]   // 不認得的一律當步行
    [InlineData(null, "pedestrian")]
    public void 交通方式對應到正確的路網(string transport, string expected)
    {
        Assert.Equal(expected, ValhallaService.ToCosting(transport));
    }

    #endregion

    #region 等時圈

    /// <summary>經度 120.0~120.1、緯度 24.0~24.1 的正方形，中間挖一個 120.04~120.06 / 24.04~24.06 的洞</summary>
    private static IsochroneBand SquareWithHole() => new()
    {
        minutes = 10,
        polygons = new List<List<List<double[]>>>
        {
            new()
            {
                Ring(120.0, 24.0, 120.1, 24.1),
                Ring(120.04, 24.04, 120.06, 24.06),
            }
        }
    };

    /// <summary>GeoJSON 環，點是 [經度, 緯度]</summary>
    private static List<double[]> Ring(double minLon, double minLat, double maxLon, double maxLat) => new()
    {
        new[] { minLon, minLat }, new[] { maxLon, minLat }, new[] { maxLon, maxLat }, new[] { minLon, maxLat }, new[] { minLon, minLat },
    };

    [Theory]
    [InlineData(24.02, 120.02, true)]    // 圈內
    [InlineData(24.05, 120.05, false)]   // 在洞裡
    [InlineData(24.20, 120.20, false)]   // 圈外
    public void 判斷座標是否落在等時圈內(double lat, double lng, bool expected)
    {
        Assert.Equal(expected, ValhallaService.Contains(SquareWithHole(), lat, lng));
    }

    [Fact]
    public void 多個等時圈的外框取最大範圍()
    {
        var small = new IsochroneBand { polygons = new() { new() { Ring(120.0, 24.0, 120.1, 24.1) } } };
        var large = new IsochroneBand { polygons = new() { new() { Ring(119.9, 23.95, 120.05, 24.2) } } };

        var (minLat, maxLat, minLon, maxLon) = ValhallaService.GetBoundingBox(new[] { small, large });

        Assert.Equal((23.95, 24.2, 119.9, 120.1), (minLat, maxLat, minLon, maxLon));
    }

    #endregion

    #region 路線形狀解碼

    [Fact]
    public void 解碼精度6位的路線形狀_回傳經度緯度()
    {
        // Google Polyline 官方範例字串；用 6 位精度解碼時，座標是 5 位精度結果的十分之一
        var points = ValhallaService.DecodePolyline6("_p~iF~ps|U_ulLnnqC_mqNvxq`@");

        Assert.Equal(3, points.Count);
        AssertPoint(-12.02, 3.85, points[0]);
        AssertPoint(-12.095, 4.07, points[1]);
        AssertPoint(-12.6453, 4.3252, points[2]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 空的路線形狀回傳空清單(string encoded)
    {
        Assert.Empty(ValhallaService.DecodePolyline6(encoded));
    }

    private static void AssertPoint(double lon, double lat, double[] actual)
    {
        Assert.Equal(lon, actual[0], 6);
        Assert.Equal(lat, actual[1], 6);
    }

    #endregion
}
