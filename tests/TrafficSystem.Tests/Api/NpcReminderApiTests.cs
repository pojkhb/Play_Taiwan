// 地圖上 NPC 的主動提醒：GET /api/Map/Reminders。後端判斷現在有沒有值得 NPC 自己跳出來講的事（天氣、用餐時間），
// 沒有就回空陣列。現在幾點用固定的時鐘控制；天氣由 FakeAi 模擬（沒有設定氣象署授權碼，走備援的 Open-Meteo 格式）。
using System.Net;
using System.Net.Http.Headers;
using Allure.Net.Commons.Attributes;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("解謎地圖與迷霧", "API 整合測試")]
[AllureBddHierarchy("解謎地圖與迷霧", "API 整合測試")]
public class NpcReminderApiTests : IDisposable
{
    private readonly ApiFactory _api;

    public NpcReminderApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
        _api.FakeNeo4j.Reset();   // Neo4j 連不上 → 店家只用 MySQL
    }

    public void Dispose() => _api.FakeAi.Reset();

    private sealed class FixedClock : TaiwanClock
    {
        private readonly DateTime _now;
        public FixedClock(DateTime now) => _now = now;
        public override DateTime Now => _now;
    }

    /// <summary>台灣時間固定在 2026/10/13 這個時刻的後端（每次都是新的後端，天氣的暫存互不影響）</summary>
    private HttpClient ClientAt(int hour, int minute)
    {
        var clock = new FixedClock(new DateTime(2026, 10, 13, hour, minute, 0));
        HttpClient client = _api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TaiwanClock>(clock))).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token(_api.NewUserId()));
        return client;
    }

    // 每個測試用不同的中心點（彼此相距 20 公里以上，也遠離其他測試建立的店家）
    private static (double lat, double lng) Center(int n) => (26.3 + n * 0.2, 122.3);

    private const double MetersPerDegree = 111320.0;

    /// <summary>在中心點正北方 meters 公尺建立一間美食店（沒有營業時間資料 → 不管現在幾點都會列出）</summary>
    private async Task<string> AddFoodAsync((double lat, double lng) center, double meters)
    {
        string name = $"提醒測試店{Guid.NewGuid():N}";
        await _api.ExecuteAsync(@"
            INSERT INTO place (p_name, p_category, p_type, p_address, p_latitude, p_longitude)
            VALUES (@name, '飲食', '小吃', '測試路 1 號', @lat, @lng);",
            new { name, lat = center.lat + meters / MetersPerDegree, lng = center.lng });
        return name;
    }

    /// <summary>天氣服務的回應（Open-Meteo 的格式）</summary>
    private static object Weather(double temperature, int code, double precipitation, params int[] probabilities) => new
    {
        current = new { time = "2026-10-13T12:00", temperature_2m = temperature, apparent_temperature = temperature, precipitation, weather_code = code, is_day = 1 },
        hourly = new { precipitation_probability = probabilities }
    };

    private static async Task<ApiResult<NpcReminderResponse>> RemindersAsync(HttpClient client, (double lat, double lng) c, string extra = "")
    {
        HttpResponseMessage response = await client.GetAsync(FormattableString.Invariant($"/api/Map/Reminders?lat={c.lat}&lng={c.lng}") + extra);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadResultAsync<NpcReminderResponse>();
    }

    [Fact]
    public async Task 午餐時間而且等一下可能下雨_NPC先講天氣再推薦店家_劇本自己的站不會被推薦()
    {
        var c = Center(0);
        int user = _api.NewUserId();

        // 最近的那一間其實是劇本的站（還在迷霧中，不能從推薦裡洩漏）
        string station = $"提醒測試站{Guid.NewGuid():N}"[..18];
        int storyId = await _api.CreateStoryAsync(user, "臺北市", night: false, station);
        await _api.ExecuteAsync("UPDATE place SET p_category = '飲食', p_latitude = @lat, p_longitude = @lng WHERE p_name = @station;",
            new { station, lat = c.lat + 100 / MetersPerDegree, c.lng });
        string near = await AddFoodAsync(c, 200);
        string far = await AddFoodAsync(c, 400);
        await AddFoodAsync(c, 2000);   // 超過 800 公尺

        _api.FakeAi.WeatherResponse = Weather(27, code: 2, precipitation: 0, 40, 70, 60);   // 多雲，現在沒下，降雨機率最高 70%
        HttpClient client = ClientAt(12, 0);

        ApiResult<NpcReminderResponse> result = await RemindersAsync(client, c, $"&story_id={storyId}");

        Assert.Equal("有 2 則提醒", result.message);
        Assert.Equal(300, result.Result.check_after_seconds);
        Assert.Equal(new[] { "weather", "meal" }, result.Result.reminders.Select(r => r.type));

        NpcReminder weather = result.Result.reminders[0];
        Assert.Equal(("weather-20261013-12-rain_soon", "rain_soon"), (weather.key, weather.kind));
        Assert.Equal("目前天氣：多雲，氣溫 27°C，降雨機率 70%。等一下可能會下雨，出門記得帶把傘。", weather.message);
        Assert.Equal(("cloudy", false, 70), (weather.weather.condition, weather.weather.is_raining, weather.weather.rain_probability));
        Assert.Empty(weather.places);

        NpcReminder meal = result.Result.reminders[1];
        Assert.Equal(("meal-20261013-lunch", "lunch"), (meal.key, meal.kind));
        Assert.Equal(new[] { near, far }, meal.places.Select(p => p.name));
        Assert.StartsWith("午餐時間到了！附近 ", meal.message);
        Assert.Contains($"有「{near}」，另外還有 1 間美食", meal.message);
        Assert.StartsWith("https://www.google.com/maps/", meal.places[0].maps_deeplink_url);
        Assert.Null(meal.weather);

        // 沒帶劇本：只是一般的附近美食，最近的那一間照常推薦
        NpcReminder withoutStory = (await RemindersAsync(client, c)).Result.reminders.Single(r => r.type == "meal");
        Assert.Equal(new[] { station, near, far }, withoutStory.places.Select(p => p.name));
        Assert.Equal(meal.key, withoutStory.key);   // 同一餐同一個代號，前端跳過一次就不再跳
    }

    [Fact]
    public async Task 天氣好又不是用餐時間_沒有提醒_NPC不用跳出來()
    {
        var c = Center(1);
        await AddFoodAsync(c, 200);
        _api.FakeAi.WeatherResponse = Weather(25, code: 1, precipitation: 0, 0, 10, 20);

        ApiResult<NpcReminderResponse> result = await RemindersAsync(ClientAt(15, 0), c);

        Assert.True(result.isSuccess);
        Assert.Equal("現在沒有要提醒的事", result.message);
        Assert.Empty(result.Result.reminders);
        Assert.Equal(300, result.Result.check_after_seconds);
    }

    [Fact]
    public async Task 晚餐時間但附近沒有店_只提醒天氣太熱()
    {
        _api.FakeAi.WeatherResponse = Weather(33.5, code: 0, precipitation: 0, 0, 0, 0);

        NpcReminder only = Assert.Single((await RemindersAsync(ClientAt(18, 0), Center(2))).Result.reminders);

        Assert.Equal(("weather-20261013-18-hot", "weather", "hot"), (only.key, only.type, only.kind));
        Assert.EndsWith("天氣很熱，記得多喝水、做好防曬。", only.message);
    }

    [Fact]
    public async Task 正在下雨_提醒帶傘()
    {
        _api.FakeAi.WeatherResponse = Weather(22, code: 63, precipitation: 1.2, 90, 90, 80);

        NpcReminder only = Assert.Single((await RemindersAsync(ClientAt(10, 0), Center(3))).Result.reminders);

        Assert.Equal(("weather-20261013-09-raining", "raining"), (only.key, only.kind));
        Assert.Equal("目前天氣：下雨，氣溫 22°C，降雨機率 90%。外面正在下雨，記得帶傘，路滑小心走。", only.message);
        Assert.True(only.weather.is_raining);
    }

    [Fact]
    public async Task 天氣查不到時_用餐提醒照常()
    {
        var c = Center(4);
        string shop = await AddFoodAsync(c, 300);   // FakeAi 沒有設定天氣 → 天氣服務回 404

        NpcReminder only = Assert.Single((await RemindersAsync(ClientAt(8, 0), c)).Result.reminders);

        Assert.Equal(("meal-20261013-breakfast", "meal", "breakfast"), (only.key, only.type, only.kind));
        Assert.StartsWith("早餐時間到了！", only.message);
        Assert.Equal(shop, Assert.Single(only.places).name);
    }

    [Fact]
    public async Task 沒帶位置回400_沒登入回401()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.ClientFor(_api.NewUserId()).GetAsync("/api/Map/Reminders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.ClientFor(null).GetAsync("/api/Map/Reminders?lat=24.1&lng=120.6")).StatusCode);
    }
}
