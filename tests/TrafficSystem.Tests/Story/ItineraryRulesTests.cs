// 行程規劃的規則（ItineraryRules）：等時圈分鐘數、抵達時間、營業時間、依抵達時間換景點（夜間站）
using backend.Services;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;
using OpenHours = backend.Services.ItineraryRules.OpenHours;

namespace TrafficSystem.Tests.Story;

[AllureSuiteHierarchy("劇本", "單元測試")]
[AllureBddHierarchy("劇本", "單元測試")]
public class ItineraryRulesTests
{
    private const double MetersPerDegree = 111194.93;
    private const double Speed = 100;   // 每分鐘 100 公尺，1000 公尺剛好 10 分鐘

    /// <summary>出發點正北方 meters 公尺的景點；travel_minutes 是從出發點過去的真實交通時間</summary>
    private static ReachableAttractionNode P(string uid, string name, double meters) => new()
    {
        uid = uid,
        name = name,
        lat = 24 + meters / MetersPerDegree,
        lon = 120,
        distance_m = meters,
        travel_minutes = meters / Speed
    };

    private static DateTime At(string time) => DateTime.Parse($"2026-10-14T{time}:00");

    private static List<OpenHours> Hours(params (string open, string close)[] slots) => ItineraryRules.ParseHours(slots);

    private static readonly Dictionary<string, List<OpenHours>> NoHours = new();

    private static string[] Times(IEnumerable<ItineraryRules.PlannedStop> stops) =>
        stops.Select(s => s.arrival.ToString("HH:mm")).ToArray();

    #region 等時圈分鐘數 ContourMinutes

    [Fact]
    public void 沒選時間時用預設的10_20_30()
    {
        Assert.Equal(new[] { 10, 20, 30 }, ItineraryRules.ContourMinutes(null));
        Assert.Equal(new[] { 10, 20, 30 }, ItineraryRules.ContourMinutes(0));
    }

    [Theory]
    [InlineData(45, new[] { 15, 30, 45 })]
    [InlineData(20, new[] { 7, 13, 20 })]
    [InlineData(60, new[] { 20, 40, 60 })]
    public void 玩家選的時間切成近中遠三圈(int max, int[] expected)
    {
        Assert.Equal(expected, ItineraryRules.ContourMinutes(max));
    }

    [Fact]
    public void 超出範圍的時間會收進10到60分鐘()
    {
        Assert.Equal(10, ItineraryRules.ContourMinutes(3).Last());
        Assert.Equal(60, ItineraryRules.ContourMinutes(200).Last());
    }

    #endregion

    #region 抵達時間

    [Theory]
    [InlineData("17:59", false)]
    [InlineData("18:00", true)]
    [InlineData("23:30", true)]
    [InlineData("04:59", true)]
    [InlineData("05:00", false)]
    public void 晚上六點到清晨五點算天黑(string time, bool night)
    {
        Assert.Equal(night, ItineraryRules.IsNight(At(time)));
    }

    [Fact]
    public void 平均速度用真實交通時間反推()
    {
        var reachable = new List<ReachableAttractionNode>
        {
            new() { distance_m = 1000, travel_minutes = 10 },
            new() { distance_m = 3000, travel_minutes = 10 },
            new() { distance_m = 0, travel_minutes = 5 },      // 就在出發點上的不算
        };

        Assert.Equal(200, ItineraryRules.AverageSpeed(reachable));
    }

    [Fact]
    public void 沒有任何交通時間可以參考時_用步行的速度()
    {
        Assert.Equal(60, ItineraryRules.AverageSpeed(new List<ReachableAttractionNode>()));
    }

    [Fact]
    public void 出發點到景點用真實交通時間_景點到景點用距離除以平均速度()
    {
        ReachableAttractionNode a = P("a", "A", 1000), b = P("b", "B", 3000);
        a.travel_minutes = 17;   // 真實路程比直線估的久

        Assert.Equal(17, ItineraryRules.LegMinutes(null, a, Speed));
        Assert.Equal(20, ItineraryRules.LegMinutes(a, b, Speed), 1);
    }

    #endregion

    #region 營業時間

    [Fact]
    public void 營業時間看不懂的那一筆略過()
    {
        var hours = Hours(("09:00", "17:00"), ("18:00:00", "02:00:00"), ("公休", ""), ("25:00", "26:00"));

        Assert.Equal(new[] { new OpenHours(TimeSpan.FromHours(9), TimeSpan.FromHours(17)), new OpenHours(TimeSpan.FromHours(18), TimeSpan.FromHours(2)) }, hours);
    }

    [Fact]
    public void 文字的營業時間可以有多個時段()
    {
        var hours = ItineraryRules.ParseHoursText("11:00-14:00、17:00～21:00");

        Assert.Equal(2, hours.Count);
        Assert.True(ItineraryRules.IsOpenAt(hours, At("12:00")));
        Assert.False(ItineraryRules.IsOpenAt(hours, At("15:00")));
        Assert.True(ItineraryRules.IsOpenAt(hours, At("20:00")));
    }

    [Fact]
    public void 文字的營業時間_24小時是全天_看不懂是空的()
    {
        Assert.True(ItineraryRules.IsOpenAt(ItineraryRules.ParseHoursText("24小時開放"), At("03:00")));
        Assert.Empty(ItineraryRules.ParseHoursText("週一公休"));
        Assert.Empty(ItineraryRules.ParseHoursText(null));
    }

    [Fact]
    public void 跨夜的時段_半夜還算有開()
    {
        var hours = Hours(("18:00", "02:00"));

        Assert.True(ItineraryRules.IsOpenAt(hours, At("23:00")));
        Assert.True(ItineraryRules.IsOpenAt(hours, At("01:30")));
        Assert.False(ItineraryRules.IsOpenAt(hours, At("02:00")));
        Assert.False(ItineraryRules.IsOpenAt(hours, At("12:00")));
    }

    [Fact]
    public void 零點到二十四點是全天()
    {
        Assert.True(ItineraryRules.IsOpenAt(Hours(("00:00", "24:00")), At("03:00")));
    }

    #endregion

    #region 景點排在這個時間合不合適 Fits

    [Theory]
    [InlineData("16:00", true)]
    [InlineData("16:45", false)]   // 到的時候有開，但 15 分鐘後就關了
    [InlineData("18:30", false)]
    [InlineData("08:30", false)]   // 還沒開
    public void 有營業時間的景點_到的時候要有開而且還能再待30分鐘(string arrival, bool fits)
    {
        Assert.Equal(fits, ItineraryRules.Fits("國立自然科學博物館", Hours(("09:00", "17:00")), At(arrival)));
    }

    [Fact]
    public void 有營業時間就看營業時間_不看名稱()
    {
        var hours = Hours(("17:00", "00:00"));

        Assert.False(ItineraryRules.Fits("一中商圈小店", hours, At("10:00")));
        Assert.True(ItineraryRules.Fits("一中商圈小店", hours, At("19:00")));
    }

    [Theory]
    [InlineData("逢甲夜市", false, true)]        // 只適合晚上
    [InlineData("望高寮夜景公園", false, true)]
    [InlineData("高美濕地觀景台", true, true)]    // 白天晚上都可以
    [InlineData("鹿港老街", true, true)]
    [InlineData("臺中公園", true, false)]         // 沒有線索，天黑後就不優先排
    public void 沒有營業時間的景點_看名稱(string name, bool fitsDay, bool fitsNight)
    {
        Assert.Equal(fitsDay, ItineraryRules.Fits(name, null, At("14:00")));
        Assert.Equal(fitsNight, ItineraryRules.Fits(name, new List<OpenHours>(), At("19:00")));
    }

    #endregion

    #region 依抵達時間調整景點 ArrangeByArrival

    [Fact]
    public void 白天出發_景點都合適時不換_每站抵達時間是上一站加停留40分鐘加車程()
    {
        var ordered = new[] { P("a", "A公園", 1000), P("b", "B公園", 2000), P("c", "C公園", 4000) };
        var spare = new List<ReachableAttractionNode> { P("x", "X老街", 1500) };

        var stops = ItineraryRules.ArrangeByArrival(ordered, spare, NoHours, At("09:00"), Speed);

        Assert.Equal(new[] { "a", "b", "c" }, stops.Select(s => s.place.uid));
        Assert.Equal(new[] { "09:10", "10:00", "11:00" }, Times(stops));   // 10 分、+40+10、+40+20
        Assert.All(stops, s => Assert.False(s.is_night));
        Assert.Equal(new[] { "x" }, spare.Select(s => s.uid));
    }

    [Fact]
    public void 下午出發_天黑後才到的站換成適合晚上的景點_挑離上一站最近的()
    {
        var ordered = new[] { P("a", "A公園", 1000), P("b", "B公園", 2000), P("c", "C公園", 3000) };
        var spare = new List<ReachableAttractionNode>
        {
            P("far", "遠遠夜市", 9000),
            P("near", "近近老街", 2500),
            P("park", "D公園", 2100),      // 最近，但天黑後不適合
        };

        var stops = ItineraryRules.ArrangeByArrival(ordered, spare, NoHours, At("16:30"), Speed);

        Assert.Equal(new[] { "a", "b", "near" }, stops.Select(s => s.place.uid));
        Assert.Equal(new[] { "16:40", "17:30", "18:15" }, Times(stops));   // 第三站：17:30 + 40 + 5 分
        Assert.Equal(new[] { false, false, true }, stops.Select(s => s.is_night));

        // 用掉的從備用拿走，被換下來的放回備用
        Assert.Equal(new[] { "c", "far", "park" }, spare.Select(s => s.uid).OrderBy(u => u));
    }

    [Fact]
    public void 夜市不排在白天_換下來之後晚上那一站還可以用它()
    {
        var ordered = new[] { P("market", "中央夜市", 1000), P("b", "B公園", 2000), P("c", "C公園", 3000) };
        var spare = new List<ReachableAttractionNode> { P("park", "D公園", 1200) };

        var stops = ItineraryRules.ArrangeByArrival(ordered, spare, NoHours, At("16:30"), Speed);

        Assert.Equal(new[] { "park", "b", "market" }, stops.Select(s => s.place.uid));
        Assert.Equal(new[] { false, false, true }, stops.Select(s => s.is_night));
    }

    [Fact]
    public void 到的時候已經關門的景點會換掉_換上來的也要用它自己的抵達時間算有開()
    {
        var ordered = new[] { P("museum", "博物館", 1000), P("b", "B公園", 2000) };
        var spare = new List<ReachableAttractionNode>
        {
            P("closed", "美術館", 900),     // 更近，但一樣關了
            P("open", "文創園區", 1500),
        };
        var hours = new Dictionary<string, List<OpenHours>>
        {
            ["museum"] = Hours(("09:00", "17:00")),
            ["closed"] = Hours(("09:00", "17:00")),
            ["open"] = Hours(("10:00", "21:00")),
        };

        var stops = ItineraryRules.ArrangeByArrival(ordered, spare, hours, At("16:45"), Speed);

        Assert.Equal(new[] { "open", "b" }, stops.Select(s => s.place.uid));
        Assert.Equal("17:00", stops[0].arrival.ToString("HH:mm"));
    }

    [Fact]
    public void 備用景點沒有合適的就維持原本的景點_站數與順序不變()
    {
        var ordered = new[] { P("a", "A公園", 1000), P("b", "B公園", 2000), P("c", "C公園", 3000) };
        var spare = new List<ReachableAttractionNode> { P("park", "D公園", 2100) };

        var stops = ItineraryRules.ArrangeByArrival(ordered, spare, NoHours, At("19:00"), Speed);

        Assert.Equal(new[] { "a", "b", "c" }, stops.Select(s => s.place.uid));
        Assert.All(stops, s => Assert.True(s.is_night));
        Assert.Equal(new[] { "park" }, spare.Select(s => s.uid));
    }

    [Fact]
    public void 同一份劇本已經有的景點不會再被換進來()
    {
        // 景點不夠時，備用清單裡可能有這份劇本後面才會到的站
        ReachableAttractionNode street = P("street", "C老街", 3000);
        var ordered = new[] { P("a", "A公園", 1000), P("b", "B公園", 2000), street };
        var spare = new List<ReachableAttractionNode> { street };

        var stops = ItineraryRules.ArrangeByArrival(ordered, spare, NoHours, At("18:30"), Speed);

        Assert.Equal(new[] { "a", "b", "street" }, stops.Select(s => s.place.uid));
    }

    [Fact]
    public void 三份劇本共用備用景點_前一份用掉的後一份不會再用()
    {
        var spare = new List<ReachableAttractionNode> { P("street", "近近老街", 1100) };
        var first = new[] { P("a", "A公園", 1000) };
        var second = new[] { P("b", "B公園", 1200) };

        var one = ItineraryRules.ArrangeByArrival(first, spare, NoHours, At("19:00"), Speed);
        var two = ItineraryRules.ArrangeByArrival(second, spare, NoHours, At("19:00"), Speed);

        Assert.Equal("street", one.Single().place.uid);
        Assert.Equal("b", two.Single().place.uid);   // 老街被第一份用掉了，備用只剩第一份換下來的公園
    }

    #endregion

    #region 整份劇本算不算夜間 MostlyNight

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void 過半數的站天黑後才到才算夜間劇本(int nightStops, bool expected)
    {
        var stops = Enumerable.Range(0, 8)
            .Select(i => new ItineraryRules.PlannedStop(P($"p{i}", "景點", 1000), At(i < nightStops ? "19:00" : "14:00")));

        Assert.Equal(expected, StoryService.MostlyNight(stops));
    }

    [Fact]
    public void 沒有任何站時不算夜間劇本()
    {
        Assert.False(StoryService.MostlyNight(new List<ItineraryRules.PlannedStop>()));
    }

    #endregion
}
