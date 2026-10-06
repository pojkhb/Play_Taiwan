// 劇本生成挑景點的演算法（StoryService）：交通方式分組、景點去重、各圈層輪流抽、多組不重複、參觀順序
using backend.Services;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Story;

[AllureSuiteHierarchy("劇本", "單元測試")]
[AllureBddHierarchy("劇本", "單元測試")]
public class StoryAlgorithmTests
{
    private static ReachableAttractionNode P(string uid, string name, double lat, double lon, int ring = 1) =>
        new() { uid = uid, name = name, lat = lat, lon = lon, ring = ring };

    /// <summary>三個圈層各 n 個景點，uid 形如 r2-3</summary>
    private static List<ReachableAttractionNode> Rings(int perRing, int ringCount = 3) =>
        Enumerable.Range(1, ringCount)
            .SelectMany(r => Enumerable.Range(1, perRing).Select(i => P($"r{r}-{i}", $"景點{r}-{i}", 24 + r * 0.01, 120 + i * 0.001, r)))
            .ToList();

    #region 交通方式分組 GroupTransports

    [Fact]
    public void 公車捷運不算交通圈_只選它們時預設步行()
    {
        var groups = StoryService.GroupTransports(new List<string> { "公車", "捷運" });

        Assert.Equal(new[] { ("pedestrian", "步行") }, groups);
    }

    [Fact]
    public void 沒選交通方式時預設步行()
    {
        Assert.Equal(new[] { ("pedestrian", "步行") }, StoryService.GroupTransports(null));
    }

    [Fact]
    public void 同一種路網的交通方式合併成一組_並去掉空白與重複()
    {
        var groups = StoryService.GroupTransports(new List<string> { "腳踏車", "自行車", " 步行 ", "步行", "" });

        Assert.Equal(new[] { ("bicycle", "腳踏車/自行車"), ("pedestrian", "步行") }, groups);
    }

    #endregion

    #region 景點去重 RemoveDuplicateAttractions

    [Fact]
    public void 同名景點在300公尺內視為同一個_台臺視為相同()
    {
        var result = StoryService.RemoveDuplicateAttractions(new List<ReachableAttractionNode>
        {
            P("a", "臺中公園", 24.1441, 120.6841),
            P("b", "台中公園", 24.1450, 120.6845),   // 約 100 公尺
        });

        Assert.Equal(new[] { "a" }, result.Select(r => r.uid));
    }

    [Fact]
    public void 不同名但幾乎同座標視為同一個()
    {
        var result = StoryService.RemoveDuplicateAttractions(new List<ReachableAttractionNode>
        {
            P("a", "臺中驛鐵道文化園區", 24.13700, 120.68600),
            P("b", "臺中火車站ˍ舊站", 24.13705, 120.68603),   // 約 6 公尺
        });

        Assert.Equal(new[] { "a" }, result.Select(r => r.uid));
    }

    [Fact]
    public void 同名但相距很遠的是不同景點()
    {
        var result = StoryService.RemoveDuplicateAttractions(new List<ReachableAttractionNode>
        {
            P("a", "龍山寺", 25.0372, 121.4999),
            P("b", "龍山寺", 25.0372, 121.5099),   // 約 1 公里
        });

        Assert.Equal(2, result.Count);
    }

    #endregion

    #region 各圈層輪流抽 PickRandomAcrossRings

    [Fact]
    public void 各圈層輪流抽_抽三個時三圈各一個()
    {
        for (int round = 0; round < 20; round++)   // 有隨機成分，多跑幾次
        {
            var picked = StoryService.PickRandomAcrossRings(Rings(perRing: 3), 3);

            Assert.Equal(new[] { 1, 2, 3 }, picked.Select(p => p.ring).OrderBy(r => r));
        }
    }

    [Fact]
    public void 抽出的數量不超過上限且不重複()
    {
        var picked = StoryService.PickRandomAcrossRings(Rings(perRing: 4), 8);

        Assert.Equal(8, picked.Count);
        Assert.Equal(8, picked.Select(p => p.uid).Distinct().Count());
    }

    [Fact]
    public void 景點不夠時全部回傳()
    {
        Assert.Equal(5, StoryService.PickRandomAcrossRings(Rings(perRing: 5, ringCount: 1), 8).Count);
    }

    #endregion

    #region 一次挑多組 PickPlaceSets

    [Fact]
    public void 景點夠多時三組互不重複()
    {
        var sets = StoryService.PickPlaceSets(Rings(perRing: 8), setCount: 3, perSet: 8);

        Assert.Equal(3, sets.Count);
        Assert.All(sets, s => Assert.Equal(8, s.Count));
        Assert.Equal(24, sets.SelectMany(s => s).Select(p => p.uid).Distinct().Count());
    }

    [Fact]
    public void 景點不夠時允許跟前面的組重複_但每組都湊滿且組內不重複()
    {
        var sets = StoryService.PickPlaceSets(Rings(perRing: 5, ringCount: 2), setCount: 3, perSet: 8);

        Assert.All(sets, s =>
        {
            Assert.Equal(8, s.Count);
            Assert.Equal(8, s.Select(p => p.uid).Distinct().Count());
        });
    }

    #endregion

    #region 參觀順序 OrderByVisit

    [Fact]
    public void 從中心點出發由近到遠排順路()
    {
        var shuffled = new List<ReachableAttractionNode>
        {
            P("c", "C", 24.03, 120.0), P("a", "A", 24.01, 120.0), P("d", "D", 24.04, 120.0), P("b", "B", 24.02, 120.0),
        };

        var ordered = StoryService.OrderByVisit(shuffled, 24.0, 120.0);

        Assert.Equal(new[] { "a", "b", "c", "d" }, ordered.Select(p => p.uid));
    }

    [Fact]
    public void 排順序不會少掉或多出景點()
    {
        var places = Rings(perRing: 3);

        var ordered = StoryService.OrderByVisit(places, 24.0, 120.0);

        Assert.Equal(places.Select(p => p.uid).OrderBy(u => u), ordered.Select(p => p.uid).OrderBy(u => u));
    }

    #endregion
}
