// 天氣提醒：GET /api/Map/Weather（地圖上 NPC 的天氣提醒）。
// 主要來源是中央氣象署（要有授權碼），查不到時改用備援的 Open-Meteo；兩個外部服務這裡都由 FakeAi 模擬。
using System.Net;
using System.Net.Http.Headers;
using Allure.Net.Commons.Attributes;
using backend.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace TrafficSystem.Tests.Api;

[Collection(ApiCollection.Name)]
[AllureSuiteHierarchy("解謎地圖與迷霧", "API 整合測試")]
[AllureBddHierarchy("解謎地圖與迷霧", "API 整合測試")]
public class WeatherApiTests : IDisposable
{
    private const string ObservationPath = "/api/v1/rest/datastore/O-A0003-001";
    private const string ForecastPath = "/api/v1/rest/datastore/F-C0032-001";

    private readonly ApiFactory _api;

    public WeatherApiTests(ApiFactory api)
    {
        _api = api;
        _api.FakeAi.Reset();
    }

    public void Dispose() => _api.FakeAi.Reset();

    #region 模擬的外部服務

    /// <summary>氣象署的一個測站（數值跟正式服務一樣都是字串）</summary>
    private static object Station(string name, string county, double lat, double lng, string weather, string temperature) => new
    {
        StationName = name,
        ObsTime = new { DateTime = "2026-10-13T10:00:00+08:00" },
        GeoInfo = new
        {
            Coordinates = new[] { new { CoordinateName = "WGS84", StationLatitude = FormattableString.Invariant($"{lat:F4}"), StationLongitude = FormattableString.Invariant($"{lng:F4}") } },
            CountyName = county
        },
        WeatherElement = new { Weather = weather, WindSpeed = "2.0", AirTemperature = temperature, RelativeHumidity = "90" }
    };

    /// <summary>氣象署的測站觀測：臺中在下雨、臺北放晴</summary>
    private static object Observations() => new
    {
        success = "true",
        records = new
        {
            Station = new[]
            {
                Station("臺北", "臺北市", 25.0377, 121.5149, "晴", "30.1"),
                Station("臺中", "臺中市", 24.1457, 120.6841, "陰有雨", "22.4"),
            }
        }
    };

    /// <summary>氣象署的縣市預報：現在這個時段臺中市降雨機率 80%</summary>
    private static object Forecasts()
    {
        DateTime now = DateTime.UtcNow.AddHours(8);
        string Time(int hours) => now.AddHours(hours).ToString("yyyy-MM-dd HH:mm:ss");
        object[] Periods(string current, string next) => new object[]
        {
            new { startTime = Time(-1), endTime = Time(11), parameter = new { parameterName = current } },
            new { startTime = Time(11), endTime = Time(23), parameter = new { parameterName = next } },
        };

        return new
        {
            success = "true",
            records = new
            {
                location = new[]
                {
                    new
                    {
                        locationName = "臺中市",
                        weatherElement = new[]
                        {
                            new { elementName = "Wx", time = Periods("多雲短暫陣雨", "多雲") },
                            new { elementName = "PoP", time = Periods("80", "20") },
                        }
                    }
                }
            }
        };
    }

    /// <summary>備援來源（Open-Meteo 的格式）</summary>
    private static object OpenMeteo(double temperature, int code, double precipitation, params int[] probabilities) => new
    {
        current = new { time = "2026-10-13T12:15", temperature_2m = temperature, apparent_temperature = temperature, precipitation, weather_code = code, is_day = 1 },
        hourly = new { precipitation_probability = probabilities }
    };

    #endregion

    /// <summary>後端有設定氣象署授權碼的 HttpClient（每次都是新的後端，天氣的暫存互不影響）</summary>
    private HttpClient ClientWithCwaKey()
    {
        HttpClient client = _api.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string> { ["Weather:CwaApiKey"] = FakeAiService.CwaKey }))).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token(_api.NewUserId()));
        return client;
    }

    private static async Task<WeatherResponse> WeatherAsync(HttpClient client, double lat, double lng)
    {
        HttpResponseMessage response = await client.GetAsync(FormattableString.Invariant($"/api/Map/Weather?lat={lat}&lng={lng}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadResultAsync<WeatherResponse>()).Result;
    }

    private int Calls(string path) => _api.FakeAi.Requests.Count(r => r.Path == path);

    [Fact]
    public async Task 用中央氣象署最近測站的觀測_下雨時提醒帶傘()
    {
        _api.FakeAi.CwaObservationResponse = Observations();
        _api.FakeAi.CwaForecastResponse = Forecasts();
        HttpClient client = ClientWithCwaKey();

        WeatherResponse weather = await WeatherAsync(client, 24.1378, 120.6835);   // 宮原眼科，最近的是臺中測站

        Assert.Equal("中央氣象署", weather.source);
        Assert.Equal("臺中", weather.station_name);
        Assert.Equal(("陰有雨", "rain"), (weather.description, weather.condition));
        Assert.Equal(22.4, weather.temperature_c);
        Assert.Equal(80, weather.rain_probability);
        Assert.True(weather.is_raining);
        Assert.Equal("目前天氣：陰有雨，氣溫 22°C，降雨機率 80%。外面正在下雨，記得帶傘，路滑小心走。", weather.message);

        // 人在臺北：同一份全臺資料，換成臺北測站；臺北市沒有預報資料，所以沒有降雨機率
        WeatherResponse taipei = await WeatherAsync(client, 25.0338, 121.5645);
        Assert.Equal(("臺北", "晴", false), (taipei.station_name, taipei.description, taipei.is_raining));
        Assert.Null(taipei.rain_probability);

        // 全臺的觀測與預報各只查一次，之後都用暫存；氣象署有回應就不會去問備援來源
        Assert.Equal((1, 1, 0), (Calls(ObservationPath), Calls(ForecastPath), Calls("/v1/forecast")));
    }

    [Fact]
    public async Task 氣象署的預報查不到時_照樣回傳觀測_只是沒有降雨機率()
    {
        _api.FakeAi.CwaObservationResponse = Observations();   // 沒有設定預報 → 404

        WeatherResponse weather = await WeatherAsync(ClientWithCwaKey(), 24.1378, 120.6835);

        Assert.Equal(("中央氣象署", "臺中", "陰有雨"), (weather.source, weather.station_name, weather.description));
        Assert.Null(weather.rain_probability);
        Assert.Equal("目前天氣：陰有雨，氣溫 22°C。外面正在下雨，記得帶傘，路滑小心走。", weather.message);
    }

    [Fact]
    public async Task 氣象署連不上_或附近沒有測站時_改用備援來源()
    {
        _api.FakeAi.WeatherResponse = OpenMeteo(26.4, 2, 0, 10, 30, 20);

        // 氣象署連不上（沒有設定觀測資料 → 404）
        WeatherResponse down = await WeatherAsync(ClientWithCwaKey(), 24.1378, 120.6835);
        Assert.Equal(("Open-Meteo", "多雲"), (down.source, down.description));
        Assert.Null(down.station_name);

        // 氣象署正常，但人在東京，50 公里內沒有測站
        _api.FakeAi.CwaObservationResponse = Observations();
        WeatherResponse abroad = await WeatherAsync(ClientWithCwaKey(), 35.6812, 139.7671);
        Assert.Equal("Open-Meteo", abroad.source);
    }

    [Fact]
    public async Task 後端沒有設定授權碼時_直接用備援來源()
    {
        _api.FakeAi.CwaObservationResponse = Observations();
        _api.FakeAi.WeatherResponse = OpenMeteo(26.4, 2, 0, 10, 30, 20);

        WeatherResponse weather = await WeatherAsync(_api.ClientFor(_api.NewUserId()), 23.1011, 120.3011);

        Assert.Equal("Open-Meteo", weather.source);
        Assert.Equal(("多雲", "cloudy", 30), (weather.description, weather.condition, weather.rain_probability));
        Assert.Equal("目前天氣：多雲，氣溫 26°C，降雨機率 30%。天氣不錯，很適合到處走走。", weather.message);
        Assert.Equal(0, Calls(ObservationPath));   // 沒有授權碼就不會去問氣象署
    }

    [Fact]
    public async Task 備援來源同一帶十分鐘內只往外查一次()
    {
        _api.FakeAi.WeatherResponse = OpenMeteo(20, 61, 0.4, 80);
        HttpClient client = _api.ClientFor(_api.NewUserId());

        WeatherResponse first = await WeatherAsync(client, 23.3021, 120.5021);
        _api.FakeAi.WeatherResponse = OpenMeteo(35, 0, 0, 0);   // 天氣服務之後回不一樣的內容
        WeatherResponse again = await WeatherAsync(client, 23.3029, 120.5018);   // 走了幾十公尺

        Assert.Equal("小雨", first.description);
        Assert.True(first.is_raining);
        Assert.Equal(first.message, again.message);
        Assert.Equal(1, Calls("/v1/forecast"));
    }

    [Fact]
    public async Task 兩個來源都查不到時回502_不會整支壞掉()
    {
        HttpResponseMessage response = await ClientWithCwaKey().GetAsync("/api/Map/Weather?lat=24.1378&lng=120.6835");   // FakeAi 什麼都沒設定 → 都是 404

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        ApiResult<WeatherResponse> result = await response.ReadResultAsync<WeatherResponse>();
        Assert.False(result.isSuccess);
        Assert.Contains("天氣", result.message);
    }

    [Fact]
    public async Task 沒帶位置回400_沒登入回401()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.ClientFor(_api.NewUserId()).GetAsync("/api/Map/Weather")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.ClientFor(null).GetAsync("/api/Map/Weather?lat=23.7&lng=120.9")).StatusCode);
    }
}
