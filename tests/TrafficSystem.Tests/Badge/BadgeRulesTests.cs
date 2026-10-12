// 勳章類別規則（BadgeService.MatchPool）：依劇本內容決定完成後可以抽哪些勳章
using backend.dao;
using backend.Models;
using backend.Services;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Badge;

[AllureSuiteHierarchy("勳章", "單元測試")]
[AllureBddHierarchy("勳章", "單元測試")]
public class BadgeRulesTests
{
    // 從 badge 表挑出測試會用到的勳章，b_id、類別、對應物件都跟資料庫一致
    private static readonly List<BadgeInfo> Catalog = new()
    {
        B(1, "島嶼城市", "台北"), B(7, "島嶼城市", "台中"), B(11, "島嶼城市", "嘉義"), B(20, "島嶼城市", "連江"),
        B(21, "島嶼城市", "臺灣島本島總章"),
        B(22, "台灣味", "滷肉飯"), B(30, "台灣味", "珍珠奶茶"),
        B(31, "台灣印記", "台北101"), B(32, "台灣印記", "總統府"), B(33, "台灣印記", "中正紀念堂"),
        B(35, "台灣印記", "台南孔廟"), B(36, "台灣印記", "龍山寺"), B(38, "台灣印記", "九份"),
        B(39, "台灣印記", "野柳女王頭"), B(44, "台灣印記", "赤崁樓"), B(46, "台灣印記", "佛光山"),
        B(53, "島嶼生靈", "台灣黑熊"),
        B(61, "老台灣", "柑仔店"),
        B(64, "午夜台灣", "夜市招牌"), B(70, "午夜台灣", "滿月"),
    };

    private static BadgeInfo B(int id, string category, string thing) =>
        new() { b_id = id, b_fication = category, b_thing = thing, b_name = thing };

    private static BadgeDao.StoryFacts Story(
        string city = "臺中市", bool night = false, string[] places = null,
        string[] tags = null, int[] taskTypes = null, string placeCategory = "Attraction") => new()
    {
        city_name = city,
        is_night_mode = night ? 1 : 0,
        places = (places ?? Array.Empty<string>())
            .Select(p => new BadgeDao.StoryPlace { place_name = p, place_category = placeCategory })
            .ToList(),
        tags = (tags ?? Array.Empty<string>()).ToList(),
        task_types = (taskTypes ?? Array.Empty<int>()).ToList()
    };

    private static List<BadgeInfo> Pool(BadgeDao.StoryFacts facts) => BadgeService.MatchPool(facts, Catalog);

    private static List<string> Categories(BadgeDao.StoryFacts facts) =>
        Pool(facts).Select(b => b.b_fication).Distinct().ToList();

    private static List<string> Things(BadgeDao.StoryFacts facts, string category) =>
        Pool(facts).Where(b => b.b_fication == category).Select(b => b.b_thing).ToList();

    #region 分類圖示

    [Theory]
    [InlineData("午夜台灣", "🌙 午夜台灣")]
    [InlineData("台灣印記", "🏛️ 台灣印記")]
    [InlineData("台灣味", "🍜 台灣味")]
    [InlineData("島嶼城市", "🏝️ 島嶼城市")]
    [InlineData("島嶼生靈", "🐻 島嶼生靈")]
    [InlineData("老台灣", "🏮 老台灣")]
    [InlineData("新的分類", "🏆 新的分類")]   // 對照表沒有的分類用獎盃
    public void 分類前面加上對應的圖示(string category, string expected)
    {
        Assert.Equal(expected, BadgeService.CategoryLabel(category));
    }

    #endregion

    #region 島嶼城市

    [Theory]
    [InlineData("臺中市", "台中")]
    [InlineData("臺北市", "台北")]
    [InlineData("嘉義縣", "嘉義")]
    [InlineData("嘉義市", "嘉義")]
    [InlineData("連江縣", "連江")]
    public void 城市勳章只會是劇本所在的那個縣市(string city, string expected)
    {
        Assert.Equal(new[] { expected }, Things(Story(city: city), "島嶼城市"));
    }

    [Fact]
    public void 對不到縣市時沒有城市勳章()
    {
        Assert.Empty(Things(Story(city: null), "島嶼城市"));
    }

    [Fact]
    public void 臺灣島本島總章不會被抽到()
    {
        var facts = Story(night: true, places: new[] { "台北101", "逢甲夜市", "審計新村", "壽山動物園" }, tags: new[] { "美食" });

        Assert.DoesNotContain(Pool(facts), b => b.b_id == 21);
    }

    #endregion

    #region 午夜台灣

    [Fact]
    public void 夜間劇本可以抽整個午夜台灣類別()
    {
        Assert.Equal(new[] { "夜市招牌", "滿月" }, Things(Story(night: true), "午夜台灣"));
    }

    [Fact]
    public void 白天劇本不能抽午夜台灣()
    {
        Assert.Empty(Things(Story(night: false), "午夜台灣"));
    }

    #endregion

    #region 台灣味

    [Theory]
    [InlineData(4, null, "臺中公園", "Attraction")]     // 有地方美食型任務
    [InlineData(0, "美食", "臺中公園", "Attraction")]   // 偏好選了美食
    [InlineData(0, null, "逢甲夜市", "Attraction")]     // 經過夜市
    [InlineData(0, null, "春水堂", "Restaurant")]       // 經過餐廳
    public void 有吃東西的劇本可以抽台灣味(int taskType, string tag, string place, string placeCategory)
    {
        var facts = Story(
            places: new[] { place },
            tags: tag == null ? null : new[] { tag },
            taskTypes: taskType == 0 ? null : new[] { taskType },
            placeCategory: placeCategory);

        Assert.Contains("台灣味", Categories(facts));
    }

    [Fact]
    public void 沒有任何美食內容就不能抽台灣味()
    {
        var facts = Story(city: "臺北市", places: new[] { "台北101", "國立臺灣博物館" }, taskTypes: new[] { 6, 7 });

        Assert.DoesNotContain("台灣味", Categories(facts));
    }

    #endregion

    #region 台灣印記

    [Theory]
    [InlineData("台北101", "台北101")]
    [InlineData("中華民國總統府", "總統府")]
    [InlineData("國立中正紀念堂", "中正紀念堂")]
    [InlineData("孔廟文化園區「臺南孔子廟」", "台南孔廟")]
    [InlineData("艋舺龍山寺", "龍山寺")]
    [InlineData("九份老街", "九份")]
    [InlineData("野柳地質公園", "野柳女王頭")]
    [InlineData("赤嵌樓", "赤崁樓")]
    [InlineData("佛光山佛陀紀念館", "佛光山")]
    public void 經過地標可以抽該地標的勳章(string place, string expected)
    {
        Assert.Equal(new[] { expected }, Things(Story(places: new[] { place }), "台灣印記"));
    }

    [Theory]
    [InlineData("臺北孔廟")]              // 不是台南孔廟
    [InlineData("富岡地質公園 (小野柳)")]  // 台東的小野柳，不是野柳女王頭
    [InlineData("龍山寺地下街")]
    [InlineData("九份二山地震紀念園區")]    // 南投，不是九份
    [InlineData("佛光山台北道場")]         // 分院，不是佛光山本山
    [InlineData("佛光山惠中寺")]
    public void 名字很像的地方不算地標(string place)
    {
        Assert.Empty(Things(Story(places: new[] { place }), "台灣印記"));
    }

    #endregion

    #region 島嶼生靈、老台灣

    [Theory]
    [InlineData("壽山動物園")]
    [InlineData("秋紅谷景觀生態公園")]
    [InlineData("太魯閣國家公園")]
    public void 經過自然生態景點可以抽島嶼生靈(string place)
    {
        Assert.Contains("島嶼生靈", Categories(Story(places: new[] { place })));
    }

    [Theory]
    [InlineData("審計新村")]
    [InlineData("第二市場")]
    [InlineData("大溪老街")]
    public void 經過老街市場眷村可以抽老台灣(string place)
    {
        Assert.Contains("老台灣", Categories(Story(places: new[] { place })));
    }

    #endregion

    #region 用資料庫裡的真實劇本驗證

    [Fact]
    public void 劇本1臺中中區_城市加台灣味加老台灣()
    {
        var facts = Story(
            city: "臺中市",
            places: new[] { "宮原眼科", "臺中公園", "第二市場", "臺中市役所", "臺中州廳", "臺中文學館", "審計新村", "國立台灣美術館" },
            tags: new[] { "歷史人文", "美食" },
            taskTypes: new[] { 3, 4, 5, 6, 7, 8 });

        Assert.Equal(new[] { "島嶼城市", "台灣味", "老台灣" }, Categories(facts));
    }

    [Fact]
    public void 劇本3臺北中正_城市加三個地標()
    {
        var facts = Story(
            city: "臺北市",
            places: new[] { "國立臺灣博物館", "國立中正紀念堂", "艋舺龍山寺", "西門町", "華山1914文化創意產業園區", "松山文創園區", "國立國父紀念館", "台北101" },
            tags: new[] { "城市探索", "歷史人文" },
            taskTypes: new[] { 3, 5, 6, 7, 8 });

        Assert.Equal(new[] { "島嶼城市", "台灣印記" }, Categories(facts));
        Assert.Equal(new[] { "台北101", "中正紀念堂", "龍山寺" }, Things(facts, "台灣印記"));
    }

    #endregion
}
