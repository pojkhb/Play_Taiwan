// 用餐時間推播：餐別判斷、營業時間解析、通知文字（純函式）
using Allure.Net.Commons.Attributes;
using backend.Models;
using backend.Services;
using Hour = backend.Services.NearbyFoodService.OperatingHour;

namespace TrafficSystem.Tests.Map;

[AllureSuiteHierarchy("解謎地圖與迷霧", "單元測試")]
[AllureBddHierarchy("解謎地圖與迷霧", "單元測試")]
public class NearbyFoodTests
{
    private static DateTime At(int hour, int minute, DayOfWeek day = DayOfWeek.Wednesday)
    {
        var d = new DateTime(2026, 10, 7, hour, minute, 0);   // 2026/10/7 是星期三
        return d.AddDays(((int)day - (int)d.DayOfWeek + 7) % 7);
    }

    [Theory]
    [InlineData(7, "早餐")]
    [InlineData(11, "午餐")]
    [InlineData(13, "午餐")]
    [InlineData(15, "下午茶")]
    [InlineData(18, "晚餐")]
    [InlineData(22, "宵夜")]
    [InlineData(2, "宵夜")]
    public void 依台灣時間判斷是哪一餐(int hour, string meal)
    {
        Assert.Equal(meal, NearbyFoodService.MealOf(At(hour, 0)));
    }

    [Theory]
    [InlineData("06:30-14:00", 12, 0, true)]
    [InlineData("06:30-14:00", 14, 0, false)]
    [InlineData("11:00-14:00、17:00-21:00", 15, 30, false)]
    [InlineData("11:00-14:00、17:00-21:00", 18, 0, true)]
    [InlineData("22:00-02:00", 1, 0, true)]                       // 跨夜
    [InlineData("22:00-02:00", 12, 0, false)]
    [InlineData("09:00-18:00（各店不同）", 10, 0, true)]
    [InlineData("24小時營業", 3, 0, true)]
    [InlineData("00:00-24:00", 23, 59, true)]
    public void 看得懂營業時間文字(string text, int hour, int minute, bool open)
    {
        Assert.Equal(open, NearbyFoodService.OpenStatus(text, At(hour, minute)).open);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("依現場公告")]
    public void 營業時間沒有資料或看不懂時是不確定(string text)
    {
        Assert.Null(NearbyFoodService.OpenStatus(text, At(12, 0)).open);
    }

    [Theory]
    [InlineData("Wednesday")]
    [InlineData("Wed")]
    [InlineData("3")]
    [InlineData("星期三")]
    [InlineData("週三")]
    public void Neo4j營業時間_星期幾的各種寫法都看得懂(string day)
    {
        var hours = new List<Hour> { new() { day_of_week = day, open_time = "11:00", close_time = "14:00" } };

        var (open, text) = NearbyFoodService.OpenStatus(hours, At(12, 0, DayOfWeek.Wednesday));

        Assert.True(open);
        Assert.Equal("11:00-14:00", text);
    }

    [Fact]
    public void Neo4j營業時間_今天沒有時段就是公休()
    {
        var hours = new List<Hour> { new() { day_of_week = "Monday", open_time = "11:00", close_time = "14:00" } };

        var (open, text) = NearbyFoodService.OpenStatus(hours, At(12, 0, DayOfWeek.Wednesday));

        Assert.False(open);
        Assert.Equal("今天公休", text);
    }

    [Fact]
    public void Neo4j營業時間_星期日用0或7都可以()
    {
        foreach (string day in new[] { "0", "7", "Sunday", "星期日", "週日" })
        {
            var hours = new List<Hour> { new() { day_of_week = day, open_time = "10:00:00", close_time = "20:00:00" } };
            Assert.True(NearbyFoodService.OpenStatus(hours, At(12, 0, DayOfWeek.Sunday)).open, day);
        }
    }

    [Fact]
    public void Neo4j營業時間_有看不懂的資料就當作不確定()
    {
        var hours = new List<Hour>
        {
            new() { day_of_week = "Wednesday", open_time = "11:00", close_time = "14:00" },
            new() { day_of_week = "假日", open_time = "11:00", close_time = "14:00" }
        };

        Assert.Null(NearbyFoodService.OpenStatus(hours, At(12, 0)).open);
    }

    [Fact]
    public void 通知文字_第一間有優惠券時會提到()
    {
        var places = new List<NearbyFoodPlace>
        {
            new() { name = "山河魯肉飯", distance_m = 350, coupons = new() { new NearbyFoodCoupon { coupon_id = 1 } } },
            new() { name = "老賴紅茶", distance_m = 120 }
        };

        Assert.Equal("午餐時間到了！附近 350 公尺有「山河魯肉飯」，還有優惠券可以領，另外還有 1 間美食",
            NearbyFoodService.BuildMessage("午餐", places, 800));
    }

    [Fact]
    public void 通知文字_附近沒有美食()
    {
        Assert.Equal("晚餐時間到了！附近 1.5 公里內暫時沒有找到美食，換個地方看看吧",
            NearbyFoodService.BuildMessage("晚餐", new List<NearbyFoodPlace>(), 1500));
    }
}
