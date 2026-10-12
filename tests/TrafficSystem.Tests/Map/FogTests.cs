// 地圖迷霧（MapService）：哪幾站在迷霧中、迷霧中心的偏移
using backend.Services;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Map;

[AllureSuiteHierarchy("解謎地圖與迷霧", "單元測試")]
[AllureBddHierarchy("解謎地圖與迷霧", "單元測試")]
public class FogTests
{
    private const double Lat = 24.1386, Lng = 120.6784;   // 臺中州廳

    private static double Meters(double lat1, double lng1, double lat2, double lng2)
    {
        double dLat = (lat2 - lat1) * 111320, dLng = (lng2 - lng1) * 111320 * Math.Cos(lat1 * Math.PI / 180);
        return Math.Sqrt(dLat * dLat + dLng * dLng);
    }

    // stageOrder：目前這一關是第幾站（依序第一個還沒破關的站）
    [Theory]
    [InlineData(1, 1, true)]    // 第一站固定開放
    [InlineData(2, 1, false)]   // 第一站還沒破關，第二站在迷霧中
    [InlineData(2, 2, true)]    // 破完第一站後開放第二站
    [InlineData(3, 2, false)]   // 不能跳站
    [InlineData(2, 4, true)]    // 已經破關的站保持開放
    [InlineData(3, 4, true)]    // 全部破關後（站序 + 1）每一站都開放
    public void 破完第N站後才開放第N加1站(int nodeOrder, int stageOrder, bool unlocked)
    {
        Assert.Equal(unlocked, MapService.IsUnlocked(nodeOrder, stageOrder));
    }

    [Fact]
    public void 迷霧中心偏移後真正的點仍在迷霧範圍內()
    {
        for (int nodeId = 1; nodeId <= 200; nodeId++)
        {
            var (lat, lng) = MapService.FogCenter(nodeId, Lat, Lng, 300, "secret");
            Assert.InRange(Meters(Lat, Lng, lat, lng), 55, 245);   // 半徑的 20%～80%，留一點誤差
        }
    }

    [Fact]
    public void 同一站每次的迷霧位置都一樣()
    {
        Assert.Equal(MapService.FogCenter(42, Lat, Lng, 300, "secret"), MapService.FogCenter(42, Lat, Lng, 300, "secret"));
    }

    [Fact]
    public void 不同站或不同密鑰偏移方向不同()
    {
        var baseline = MapService.FogCenter(42, Lat, Lng, 300, "secret");

        Assert.NotEqual(baseline, MapService.FogCenter(43, Lat, Lng, 300, "secret"));
        Assert.NotEqual(baseline, MapService.FogCenter(42, Lat, Lng, 300, "another-secret"));   // 沒有密鑰就推不回真正位置
    }
}
