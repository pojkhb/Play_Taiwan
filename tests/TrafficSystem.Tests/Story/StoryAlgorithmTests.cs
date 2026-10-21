// 劇本生成挑景點的演算法（StoryService）：交通方式分組、景點去重、各圈層輪流抽、多組不重複、參觀順序、台灣好行景點規劃
using backend.dao;
using backend.Services;
using backend.ViewModels;
using PlaceInfo = backend.Services.Neo4j.PlaceLookupService.PlaceInfo;
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

    #region 台灣好行景點規劃 BuildTripperAttractions

    private static readonly List<int> TripperMinutes = new() { 10, 20, 30 };

    /// <summary>沒有站牌座標：乘車時間只用站數估（每站 2 分鐘）</summary>
    private static readonly Dictionary<(int, int), double> NoRouteMeters = new();

    private static StoryService.TripperBoarding Board(int brp, int seq, double walk, string route = "台灣好行 測試線") => new(brp, seq, walk, route);

    private static StoryDao.TripperRoutePlaceRow AtStop(int brp, int seq, string placeId, double walk) =>
        new() { brp_id = brp, stop_sequence = seq, place_id = placeId, walk_minutes = walk };

    /// <summary>景點彼此相距約 1 公里以上，不會被當成重複</summary>
    private static Dictionary<string, PlaceInfo> Places(params string[] ids) =>
        ids.Select((id, i) => new PlaceInfo { uid = id, name = $"景點{id}", lat = 24 + i * 0.01, lng = 120 })
            .ToDictionary(p => p.uid);

    [Theory]
    [InlineData(new[] { "台灣好行", "公車" }, true)]
    [InlineData(new[] { " 台灣好行 " }, true)]
    [InlineData(new[] { "公車", "客運" }, false)]
    public void 有選台灣好行就用台灣好行規劃(string[] transportation, bool expected)
    {
        Assert.Equal(expected, StoryService.IsTripper(transportation.ToList()));
        Assert.False(StoryService.IsTripper(null));
    }

    [Fact]
    public void 台灣好行只能往後搭_上車站之前的景點不算()
    {
        var result = StoryService.BuildTripperAttractions(24, 120,
            new() { Board(1, 3, 2) },
            new() { AtStop(1, 1, "before", 1), AtStop(1, 3, "here", 1), AtStop(1, 5, "after", 1) },
            NoRouteMeters, Places("before", "here", "after"), TripperMinutes);

        Assert.Equal(new[] { "after", "here" }, result.Select(a => a.uid).OrderBy(u => u));
    }

    [Fact]
    public void 交通時間是走到站牌加搭車加下車走路_多種搭法取最快()
    {
        var result = StoryService.BuildTripperAttractions(24, 120,
            new() { Board(1, 1, 5, "甲線"), Board(2, 1, 2, "乙線") },
            new() { AtStop(1, 4, "x", 3), AtStop(2, 6, "x", 1) },   // 甲線 5 + 3 站×2 + 3 = 14；乙線 2 + 5 站×2 + 1 = 13
            NoRouteMeters, Places("x"), TripperMinutes);

        ReachableAttractionNode x = Assert.Single(result);
        Assert.Equal(13, x.travel_minutes);
        Assert.Equal("乙線", x.reachable_by);
    }

    [Fact]
    public void 超過玩家選的分鐘數不算_其他依交通時間分圈()
    {
        var result = StoryService.BuildTripperAttractions(24, 120,
            new() { Board(1, 1, 2) },
            new() { AtStop(1, 3, "near", 2), AtStop(1, 11, "far", 3), AtStop(1, 16, "tooFar", 3) },   // 8、25、35 分鐘
            NoRouteMeters, Places("near", "far", "tooFar"), TripperMinutes);

        Assert.Equal(new[] { ("far", 3, 30), ("near", 1, 10) },
            result.Select(a => (a.uid, a.ring, a.reachable_minutes)).OrderBy(a => a.uid));
    }

    [Fact]
    public void 查不到名稱座標的景點不算()
    {
        var result = StoryService.BuildTripperAttractions(24, 120,
            new() { Board(1, 1, 2) },
            new() { AtStop(1, 2, "known", 1), AtStop(1, 2, "unknown", 1) },
            NoRouteMeters, Places("known"), TripperMinutes);

        Assert.Equal("known", Assert.Single(result).uid);
    }

    private static StoryDao.TripperPatternStopRow Stop(int brp, int seq, double lat) => new() { brp_id = brp, stop_sequence = seq, lat = lat, lng = 120 };

    [Fact]
    public void 市區線站距短_乘車時間用站數估()
    {
        var meters = StoryService.RouteMeters(new[] { Stop(1, 1, 24.000), Stop(1, 2, 24.001), Stop(1, 3, 24.002) });   // 每站約 111 公尺

        Assert.Equal(4, StoryService.RideMinutes(1, 1, 3, meters));
    }

    [Fact]
    public void 長途線站距大_乘車時間改用沿途距離估_遠的景點不會算進30分鐘內()
    {
        // 每站相隔約 11 公里：只用站數估是 4 分鐘，用沿途距離估是 22.2 km × 1.2 ÷ 500 m/分 ≈ 53 分鐘
        var meters = StoryService.RouteMeters(new[] { Stop(1, 1, 24.0), Stop(1, 2, 24.1), Stop(1, 3, 24.2) });

        Assert.InRange(StoryService.RideMinutes(1, 1, 3, meters), 53, 54);

        var result = StoryService.BuildTripperAttractions(24, 120,
            new() { Board(1, 1, 2) },
            new() { AtStop(1, 3, "far", 1) },
            meters, Places("far"), TripperMinutes);

        Assert.Empty(result);
    }

    #endregion
}
