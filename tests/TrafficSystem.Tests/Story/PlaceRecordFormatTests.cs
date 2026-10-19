// 劇本存檔寫進 place 表的景點資料（PlaceLookupService.GetPlaceRecordsAsync）：Neo4j 的營業時段、地址、介紹轉成 place 欄位的格式
using backend.Services;
using backend.Services.Neo4j;
using Allure.Net.Commons.Attributes;

namespace TrafficSystem.Tests.Story;

[AllureSuiteHierarchy("劇本", "單元測試")]
[AllureBddHierarchy("劇本", "單元測試")]
public class PlaceRecordFormatTests
{
    private static List<NearbyFoodService.OperatingHour> Hours(params (string day, string open, string close)[] hours) =>
        hours.Select(h => new NearbyFoodService.OperatingHour { day_of_week = h.day, open_time = h.open, close_time = h.close }).ToList();

    private static readonly string[] Week = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

    [Fact]
    public void 每天營業時間一樣_只寫一個時段()
    {
        Assert.Equal("06:00-20:00", PlaceLookupService.ToOpenTimeText(Hours(Week.Select(d => (d, "06:00", "20:00")).ToArray())));
    }

    [Fact]
    public void 有公休日_時段後面註明哪幾天休()
    {
        var hours = Hours(Week.Where(d => d is not ("Tuesday" or "Wednesday")).Select(d => (d, "11:00", "19:00")).ToArray());

        Assert.Equal("11:00-19:00（週二、週三休）", PlaceLookupService.ToOpenTimeText(hours));
    }

    [Fact]
    public void 每天時段不同_連續相同的星期合併()
    {
        var hours = Hours(
            ("Tuesday", "10:00", "18:00"), ("Wednesday", "10:00", "18:00"), ("Thursday", "10:00", "18:00"), ("Friday", "10:00", "18:00"),
            ("Saturday", "10:00", "21:00"), ("Sunday", "10:00", "18:00"));

        string text = PlaceLookupService.ToOpenTimeText(hours);

        Assert.Equal("週二至週五 10:00-18:00；週六 10:00-21:00；週日 10:00-18:00（週一休）", text);
        Assert.NotEmpty(ItineraryRules.ParseHoursText(text));   // 行程規劃讀得懂
    }

    [Fact]
    public void 一天兩個時段_用頓號連起來()
    {
        var hours = Hours(Week.SelectMany(d => new[] { (d, "17:00", "21:00"), (d, "11:00", "14:00") }).ToArray());

        Assert.Equal("11:00-14:00、17:00-21:00", PlaceLookupService.ToOpenTimeText(hours));
    }

    [Fact]
    public void 沒有營業時間或看不懂_不填()
    {
        Assert.Null(PlaceLookupService.ToOpenTimeText(Hours()));
        Assert.Null(PlaceLookupService.ToOpenTimeText(Hours(("某天", "09:00", "17:00"))));
    }

    [Theory]
    [InlineData("安平路125號", "台南市", "安平區", "臺南市安平區安平路125號")]
    [InlineData("中西區民族路二段1號", "台南市", "中西區", "臺南市中西區民族路二段1號")]
    [InlineData("臺南市中西區民族路二段1號", "台南市", "中西區", "臺南市中西區民族路二段1號")]
    [InlineData("台南市中西區民族路二段1號", "台南市", "中西區", "台南市中西區民族路二段1號")]
    [InlineData("  ", "台南市", "中西區", null)]
    public void 地址沒有縣市區就補上(string address, string city, string town, string expected)
    {
        Assert.Equal(expected, PlaceLookupService.ToFullAddress(address, city, town));
    }

    [Fact]
    public void 簡短介紹取介紹的第一句_短的整段用()
    {
        string longText = "建於1937年的洋樓，是安平少見的西式建築。" + new string('字', 120);

        Assert.Equal("老屋 民宿", PlaceLookupService.ToSummary("老屋\n\n民宿"));
        Assert.Equal("建於1937年的洋樓，是安平少見的西式建築。", PlaceLookupService.ToSummary(longText));
        Assert.Null(PlaceLookupService.ToSummary(" "));
    }
}
