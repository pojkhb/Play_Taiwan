// 地圖上 NPC 的主動提醒：什麼時候算用餐時間、天氣好就不提醒、提醒的代號（同一個代號前端只跳一次）
using Allure.Net.Commons.Attributes;
using backend.Models;
using backend.Services;

namespace TrafficSystem.Tests.Map;

[AllureSuiteHierarchy("解謎地圖與迷霧", "單元測試")]
[AllureBddHierarchy("解謎地圖與迷霧", "單元測試")]
public class NpcReminderTests
{
    private static DateTime At(int hour, int minute) => new(2026, 10, 13, hour, minute, 0);

    [Theory]
    [InlineData(6, 59, null)]
    [InlineData(7, 0, "breakfast")]
    [InlineData(8, 59, "breakfast")]
    [InlineData(9, 0, null)]
    [InlineData(11, 29, null)]
    [InlineData(11, 30, "lunch")]
    [InlineData(13, 29, "lunch")]
    [InlineData(13, 30, null)]
    [InlineData(15, 0, null)]      // 下午茶、宵夜不算用餐時間，NPC 不會跳出來
    [InlineData(17, 29, null)]
    [InlineData(17, 30, "dinner")]
    [InlineData(19, 29, "dinner")]
    [InlineData(19, 30, null)]
    [InlineData(23, 0, null)]
    public void 用餐時間_早餐七到九點_午餐十一點半到一點半_晚餐五點半到七點半(int hour, int minute, string kind)
    {
        Assert.Equal(kind, NpcReminderService.MealTimeOf(At(hour, minute))?.kind);
    }

    [Fact]
    public void 用餐時間附上餐別名稱()
    {
        Assert.Equal(("breakfast", "早餐"), NpcReminderService.MealTimeOf(At(8, 0)));
        Assert.Equal(("lunch", "午餐"), NpcReminderService.MealTimeOf(At(12, 0)));
        Assert.Equal(("dinner", "晚餐"), NpcReminderService.MealTimeOf(At(18, 0)));
    }

    private static WeatherResponse Weather(string alert) => new()
    {
        alert = alert,
        description = "多雲",
        message = "目前天氣：多雲，氣溫 27°C，降雨機率 70%。等一下可能會下雨，出門記得帶把傘。"
    };

    [Fact]
    public void 天氣好就沒有提醒()
    {
        Assert.Null(NpcReminderService.WeatherReminder(Weather(null), At(12, 0)));
        Assert.Null(NpcReminderService.WeatherReminder(null, At(12, 0)));
    }

    [Fact]
    public void 天氣提醒_同一種狀況三小時內代號一樣_換了時段或狀況才是新的一則()
    {
        WeatherResponse weather = Weather("rain_soon");

        NpcReminder noon = NpcReminderService.WeatherReminder(weather, At(12, 5));

        Assert.Equal(("weather-20261013-12-rain_soon", "weather", "rain_soon"), (noon.key, noon.type, noon.kind));
        Assert.Equal(weather.message, noon.message);
        Assert.Same(weather, noon.weather);
        Assert.Empty(noon.places);

        // 12:00–14:59 是同一個時段：代號一樣，前端不會再跳
        Assert.Equal(noon.key, NpcReminderService.WeatherReminder(weather, At(14, 59)).key);
        // 過了三小時還是可能下雨：再提醒一次
        Assert.Equal("weather-20261013-15-rain_soon", NpcReminderService.WeatherReminder(weather, At(15, 0)).key);
        // 同一個時段裡真的下起來了：是另一則提醒
        Assert.Equal("weather-20261013-12-raining", NpcReminderService.WeatherReminder(Weather("raining"), At(12, 30)).key);
    }

    [Fact]
    public void 用餐提醒_推薦第一間_同一餐一天只有一個代號()
    {
        var places = new List<NearbyFoodPlace>
        {
            new() { name = "山河魯肉飯", distance_m = 350, coupons = new() { new NearbyFoodCoupon { coupon_id = 1 } } },
            new() { name = "阿婆冰店", distance_m = 600 },
        };

        NpcReminder lunch = NpcReminderService.MealReminder("lunch", "午餐", places, 800, At(11, 40));

        Assert.Equal(("meal-20261013-lunch", "meal", "lunch"), (lunch.key, lunch.type, lunch.kind));
        Assert.Equal("午餐時間到了！附近 350 公尺有「山河魯肉飯」，還有優惠券可以領，另外還有 1 間美食", lunch.message);
        Assert.Same(places, lunch.places);
        Assert.Null(lunch.weather);

        Assert.Equal(lunch.key, NpcReminderService.MealReminder("lunch", "午餐", places, 800, At(13, 20)).key);
        Assert.Equal("meal-20261013-dinner", NpcReminderService.MealReminder("dinner", "晚餐", places, 800, At(18, 0)).key);
        Assert.Equal("meal-20261014-lunch", NpcReminderService.MealReminder("lunch", "午餐", places, 800, At(12, 0).AddDays(1)).key);
    }
}
