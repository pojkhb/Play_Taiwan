// 商家營業時間規則（OperatingHoursRules.Normalize）：整理成 Neo4j (:OperatingHours) 的格式，格式不對回 400
using backend.Models;
using backend.Services;
using backend.ViewModels;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Merchant;

[AllureSuiteHierarchy("商家", "單元測試")]
[AllureBddHierarchy("商家", "單元測試")]
public class OperatingHoursRulesTests
{
    private static PlaceOperatingHourItem H(string day, string open, string close) =>
        new() { day_of_week = day, open_time = open, close_time = close };

    private static List<string> Normalize(params PlaceOperatingHourItem[] items) =>
        OperatingHoursRules.Normalize(items.ToList())
            .Select(h => $"{h.day_of_week} {h.open_time}-{h.close_time}")
            .ToList();

    private static string Error(params PlaceOperatingHourItem[] items) =>
        Assert.Throws<BadRequestException>(() => OperatingHoursRules.Normalize(items.ToList())).Message;

    #region 整理成 Neo4j 的寫法

    [Fact]
    public void 沒填營業時間回傳空清單()
    {
        Assert.Empty(OperatingHoursRules.Normalize(null));
        Assert.Empty(OperatingHoursRules.Normalize(new List<PlaceOperatingHourItem>()));
    }

    [Fact]
    public void 星期統一成首字大寫的英文_時間補成兩位數()
    {
        Assert.Equal(new[] { "Monday 09:00-17:30" }, Normalize(H(" monday ", "9:00", "17:30")));
    }

    [Fact]
    public void 依星期一到星期日_再依開始時間排序()
    {
        Assert.Equal(
            new[] { "Monday 11:00-14:00", "Monday 17:00-21:00", "Wednesday 10:00-18:00", "Sunday 08:00-12:00" },
            Normalize(
                H("Sunday", "08:00", "12:00"),
                H("Monday", "17:00", "21:00"),
                H("Wednesday", "10:00", "18:00"),
                H("Monday", "11:00", "14:00")));
    }

    [Theory]
    [InlineData("18:00", "02:00")]   // 跨夜
    [InlineData("17:00", "00:00")]   // 營業到半夜 12 點
    [InlineData("00:00", "23:59")]   // 全天營業
    public void 跨夜_營業到半夜_全天營業都可以(string open, string close)
    {
        Assert.Equal(new[] { $"Friday {open}-{close}" }, Normalize(H("Friday", open, close)));
    }

    [Fact]
    public void 中午休息_同一天兩個時段不重疊可以()
    {
        Assert.Equal(2, Normalize(H("Tuesday", "11:00", "14:00"), H("Tuesday", "14:00", "21:00")).Count);
    }

    #endregion

    #region 格式不對回 400

    [Theory]
    [InlineData("週一")]
    [InlineData("Mon")]
    [InlineData("TyphoonDay")]
    [InlineData("")]
    [InlineData(null)]
    public void 星期只能是英文全名(string day)
    {
        Assert.Contains("operating_hours[0].day_of_week", Error(H(day, "09:00", "17:00")));
    }

    [Theory]
    [InlineData("9點")]
    [InlineData("09:00:00")]
    [InlineData("0900")]
    [InlineData("25:00")]
    [InlineData("12:60")]
    [InlineData("０９:００")]   // 全形數字
    [InlineData("")]
    [InlineData(null)]
    public void 時間要是24小時制HHmm(string time)
    {
        Assert.Contains("operating_hours[0].open_time", Error(H("Monday", time, "17:00")));
    }

    [Fact]
    public void 營業到半夜12點要填0000不是2400()
    {
        Assert.Contains("00:00", Error(H("Monday", "18:00", "24:00")));
    }

    [Fact]
    public void 開始與結束時間相同不行()
    {
        Assert.Contains("全天營業請填 00:00～23:59", Error(H("Monday", "09:00", "09:00")));
    }

    [Fact]
    public void 錯誤訊息指出是第幾筆()
    {
        Assert.Contains("operating_hours[1].close_time", Error(H("Monday", "09:00", "12:00"), H("Monday", "13:00", "abc")));
    }

    [Fact]
    public void 空的一筆不行()
    {
        Assert.Contains("operating_hours[0]", Error(new PlaceOperatingHourItem[] { null }));
    }

    [Fact]
    public void 同一天時段重疊不行()
    {
        Assert.Contains("Monday 的營業時段重疊", Error(H("Monday", "09:00", "14:00"), H("Monday", "13:00", "18:00")));
    }

    [Fact]
    public void 同一天重複填同一個時段不行()
    {
        Assert.Contains("重疊", Error(H("Monday", "09:00", "17:00"), H("monday", "9:00", "17:00")));
    }

    [Fact]
    public void 跨夜時段跟同一天更晚的時段重疊不行()
    {
        Assert.Contains("重疊", Error(H("Saturday", "18:00", "02:00"), H("Saturday", "23:00", "23:30")));
    }

    [Fact]
    public void 不同天的時段不算重疊()
    {
        Assert.Equal(2, Normalize(H("Monday", "09:00", "17:00"), H("Tuesday", "09:00", "17:00")).Count);
    }

    [Fact]
    public void 同一天最多4個時段()
    {
        Assert.Contains("最多 4 個",
            Error(H("Monday", "01:00", "02:00"), H("Monday", "03:00", "04:00"), H("Monday", "05:00", "06:00"),
                  H("Monday", "07:00", "08:00"), H("Monday", "09:00", "10:00")));
    }

    #endregion
}
