// 天氣提醒：中央氣象署（測站即時觀測 + 縣市預報）與備援的 Open-Meteo 的回應 → 天氣現象、要不要帶傘、NPC 對話泡泡的那句話
using Allure.Net.Commons.Attributes;
using backend.Models;
using backend.Services;

namespace TrafficSystem.Tests.Map;

[AllureSuiteHierarchy("解謎地圖與迷霧", "單元測試")]
[AllureBddHierarchy("解謎地圖與迷霧", "單元測試")]
public class WeatherTests
{
    private static readonly DateTime TenAm = new(2026, 10, 13, 10, 5, 0);

    #region 中央氣象署：測站即時觀測（O-A0003-001）

    /// <summary>
    /// 三個測站：臺中（正常）、梧棲（沒有觀測值，-99）、沒有座標的測站。
    /// 格式照氣象署實際的回應：數值都是字串、座標有 TWD67 與 WGS84 兩組
    /// </summary>
    private const string StationsJson = """
        {
          "success": "true",
          "records": {
            "Station": [
              {
                "StationName": "臺中", "StationId": "467490",
                "ObsTime": { "DateTime": "2026-10-13T10:00:00+08:00" },
                "GeoInfo": {
                  "Coordinates": [
                    { "CoordinateName": "TWD67", "CoordinateFormat": "decimal degrees", "StationLatitude": "24.1475", "StationLongitude": "120.6759" },
                    { "CoordinateName": "WGS84", "CoordinateFormat": "decimal degrees", "StationLatitude": "24.1457", "StationLongitude": "120.6841" }
                  ],
                  "StationAltitude": "84.0", "CountyName": "臺中市", "TownName": "北區", "CountyCode": "66000", "TownCode": "66000050"
                },
                "WeatherElement": { "Weather": "陰有雨", "VisibilityDescription": "5-10", "Now": { "Precipitation": "3.5" }, "WindDirection": "20.0",
                                    "WindSpeed": "2.0", "AirTemperature": "22.4", "RelativeHumidity": "90", "AirPressure": "1008.1" }
              },
              {
                "StationName": "梧棲", "StationId": "467770",
                "ObsTime": { "DateTime": "2026-10-13T10:00:00+08:00" },
                "GeoInfo": {
                  "Coordinates": [ { "CoordinateName": "WGS84", "CoordinateFormat": "decimal degrees", "StationLatitude": 24.2560, "StationLongitude": 120.5233 } ],
                  "CountyName": "臺中市", "TownName": "梧棲區"
                },
                "WeatherElement": { "Weather": "-99", "WindSpeed": "-99", "AirTemperature": "-99", "RelativeHumidity": -99 }
              },
              {
                "StationName": "沒有座標的測站",
                "GeoInfo": { "Coordinates": [], "CountyName": "臺中市" },
                "WeatherElement": { "Weather": "晴", "AirTemperature": 30.0 }
              }
            ]
          }
        }
        """;

    [Fact]
    public void 測站資料_座標取WGS84_沒有觀測值的欄位是null_沒有座標的測站略過()
    {
        List<WeatherService.CwaStation> stations = WeatherService.ParseCwaStations(StationsJson);

        Assert.Equal(new[] { "臺中", "梧棲" }, stations.Select(s => s.name));

        WeatherService.CwaStation taichung = stations[0];
        Assert.Equal((24.1457, 120.6841), (taichung.lat, taichung.lng));   // 不是 TWD67 那一組
        Assert.Equal("臺中市", taichung.county);
        Assert.Equal("陰有雨", taichung.weather);
        Assert.Equal((22.4, 90, 2.0), (taichung.temperature, taichung.humidity, taichung.wind_speed));
        Assert.Equal(new DateTime(2026, 10, 13, 10, 0, 0), taichung.observed_at);

        // -99（數字或字串）代表沒有觀測值；數值給數字而不是字串也讀得到
        WeatherService.CwaStation wuqi = stations[1];
        Assert.Equal((24.2560, 120.5233), (wuqi.lat, wuqi.lng));
        Assert.Null(wuqi.weather);
        Assert.Null(wuqi.temperature);
        Assert.Null(wuqi.humidity);
        Assert.Null(wuqi.wind_speed);
    }

    [Fact]
    public void 取最近而且有氣溫的測站_太遠就當作沒有()
    {
        List<WeatherService.CwaStation> stations = WeatherService.ParseCwaStations(StationsJson);

        // 人在梧棲測站旁邊，但梧棲沒有氣溫 → 用臺中
        Assert.Equal("臺中", WeatherService.Nearest(stations, 24.2561, 120.5234).name);
        // 人在東京：最近的測站超過 50 公里 → 沒有可用的測站（改用備援來源）
        Assert.Null(WeatherService.Nearest(stations, 35.6812, 139.7671));
        Assert.Null(WeatherService.Nearest(new List<WeatherService.CwaStation>(), 24.14, 120.68));
    }

    #endregion

    #region 中央氣象署：縣市天氣預報（F-C0032-001）

    private const string ForecastsJson = """
        {
          "success": "true",
          "records": {
            "datasetDescription": "三十六小時天氣預報",
            "location": [
              {
                "locationName": "臺中市",
                "weatherElement": [
                  { "elementName": "Wx", "time": [
                      { "startTime": "2026-10-13 06:00:00", "endTime": "2026-10-13 18:00:00", "parameter": { "parameterName": "多雲短暫陣雨", "parameterValue": "8" } },
                      { "startTime": "2026-10-13 18:00:00", "endTime": "2026-10-14 06:00:00", "parameter": { "parameterName": "多雲", "parameterValue": "4" } } ] },
                  { "elementName": "PoP", "time": [
                      { "startTime": "2026-10-13 06:00:00", "endTime": "2026-10-13 18:00:00", "parameter": { "parameterName": "80", "parameterUnit": "百分比" } },
                      { "startTime": "2026-10-13 18:00:00", "endTime": "2026-10-14 06:00:00", "parameter": { "parameterName": "20", "parameterUnit": "百分比" } } ] },
                  { "elementName": "MinT", "time": [
                      { "startTime": "2026-10-13 06:00:00", "endTime": "2026-10-13 18:00:00", "parameter": { "parameterName": "22", "parameterUnit": "C" } } ] }
                ]
              }
            ]
          }
        }
        """;

    [Fact]
    public void 縣市預報取包含現在的那個時段_都不包含時取第一個()
    {
        WeatherService.CwaForecast morning = WeatherService.ParseCwaForecasts(ForecastsJson, TenAm)["臺中市"];
        Assert.Equal(("多雲短暫陣雨", 80), (morning.weather, morning.rain_probability));

        WeatherService.CwaForecast night = WeatherService.ParseCwaForecasts(ForecastsJson, TenAm.AddHours(10))["臺中市"];
        Assert.Equal(("多雲", 20), (night.weather, night.rain_probability));

        WeatherService.CwaForecast stale = WeatherService.ParseCwaForecasts(ForecastsJson, TenAm.AddDays(3))["臺中市"];
        Assert.Equal(80, stale.rain_probability);
    }

    [Fact]
    public void 等一下會不會下雨_三小時內會進到下一個預報時段時_降雨機率取兩個時段較高的()
    {
        // 白天（06–18 點）降雨機率 10%，晚上（18 點之後）70%
        string json = ForecastsJson.Replace("\"parameterName\": \"80\"", "\"parameterName\": \"10\"").Replace("\"parameterName\": \"20\"", "\"parameterName\": \"70\"");

        // 中午：離晚上還很久，只看白天這一段
        Assert.Equal(10, WeatherService.ParseCwaForecasts(json, TenAm.AddHours(2))["臺中市"].rain_probability);

        // 下午四點：兩小時後就進到晚上那一段，要先提醒；天氣現象還是現在這一段的
        WeatherService.CwaForecast afternoon = WeatherService.ParseCwaForecasts(json, TenAm.AddHours(6))["臺中市"];
        Assert.Equal(("多雲短暫陣雨", 70), (afternoon.weather, afternoon.rain_probability));

        // 反過來，下一段的降雨機率比較低時不會把現在的拉低
        Assert.Equal(80, WeatherService.ParseCwaForecasts(ForecastsJson, TenAm.AddHours(6))["臺中市"].rain_probability);
    }

    #endregion

    #region 中央氣象署：觀測 + 預報 → 天氣

    [Fact]
    public void 下雨的時候_NPC提醒帶傘()
    {
        WeatherService.CwaStation station = WeatherService.ParseCwaStations(StationsJson)[0];
        WeatherService.CwaForecast forecast = WeatherService.ParseCwaForecasts(ForecastsJson, TenAm)["臺中市"];

        WeatherResponse weather = WeatherService.FromCwa(station, forecast, TenAm);

        Assert.Equal("陰有雨", weather.description);
        Assert.Equal("rain", weather.condition);
        Assert.True(weather.is_raining);
        Assert.True(weather.is_day);
        Assert.Equal(22.4, weather.temperature_c);
        Assert.InRange(weather.apparent_temperature_c, 24.0, 24.4);   // 濕度 90%、風速 2 公尺：體感約 24.2 度
        Assert.Equal(80, weather.rain_probability);
        Assert.Equal("raining", weather.alert);
        Assert.Equal("外面正在下雨，記得帶傘，路滑小心走", weather.advice);
        Assert.Equal("目前天氣：陰有雨，氣溫 22°C，降雨機率 80%。外面正在下雨，記得帶傘，路滑小心走。", weather.message);
        Assert.Equal(new DateTime(2026, 10, 13, 10, 0, 0), weather.observed_at);
        Assert.Equal("臺中", weather.station_name);
        Assert.Equal("中央氣象署", weather.source);
    }

    [Fact]
    public void 測站沒有天氣現象時用縣市預報的_預報有雨不代表現在正在下()
    {
        var station = new WeatherService.CwaStation { name = "大坑", county = "臺中市", lat = 24.18, lng = 120.73, temperature = 26.0 };
        WeatherService.CwaForecast forecast = WeatherService.ParseCwaForecasts(ForecastsJson, TenAm)["臺中市"];

        WeatherResponse weather = WeatherService.FromCwa(station, forecast, TenAm.AddHours(9));   // 晚上七點

        Assert.Equal("多雲短暫陣雨", weather.description);
        Assert.False(weather.is_raining);
        Assert.False(weather.is_day);
        Assert.Equal(26.0, weather.apparent_temperature_c);   // 沒有濕度、風速 → 跟氣溫一樣
        Assert.Equal("目前天氣：多雲短暫陣雨，氣溫 26°C，降雨機率 80%。等一下可能會下雨，出門記得帶把傘。", weather.message);
    }

    [Fact]
    public void 查不到縣市預報時_照樣回傳觀測_只是沒有降雨機率()
    {
        var station = new WeatherService.CwaStation { name = "臺南", county = "臺南市", lat = 23.0, lng = 120.2, weather = "晴", temperature = 24.0 };

        WeatherResponse weather = WeatherService.FromCwa(station, null, TenAm);

        Assert.Null(weather.rain_probability);
        Assert.Equal("clear", weather.condition);
        Assert.Null(weather.alert);   // 天氣好：沒有要主動提醒的事
        Assert.Equal("目前天氣：晴，氣溫 24°C。天氣不錯，很適合到處走走。", weather.message);
    }

    [Fact]
    public void 體感溫度_濕熱時比氣溫高_缺濕度或風速時等於氣溫()
    {
        Assert.InRange(WeatherService.ApparentTemperature(30, 70, 1), 33.5, 34.0);   // 30 度、濕度 70%、微風：體感約 33.8 度
        Assert.True(WeatherService.ApparentTemperature(30, 70, 8) < WeatherService.ApparentTemperature(30, 70, 1));   // 風大比較涼
        Assert.Equal(30, WeatherService.ApparentTemperature(30, null, 1));
        Assert.Equal(30, WeatherService.ApparentTemperature(30, 70, null));
    }

    #endregion

    #region 備援：Open-Meteo

    /// <summary>Open-Meteo 回應的樣子（欄位跟正式服務一樣）</summary>
    private static string OpenMeteoJson(double temperature, double apparent, double precipitation, int code, int isDay, string probabilities) => $$"""
        {
          "latitude": 24.147627, "longitude": 120.70138, "timezone": "Asia/Taipei",
          "current": { "time": "2026-10-13T09:30", "interval": 900, "temperature_2m": {{temperature}}, "apparent_temperature": {{apparent}},
                       "precipitation": {{precipitation}}, "weather_code": {{code}}, "is_day": {{isDay}} },
          "hourly": { "time": ["2026-10-13T09:00", "2026-10-13T10:00", "2026-10-13T11:00"], "precipitation_probability": {{probabilities}} }
        }
        """;

    [Fact]
    public void 備援來源_晴天很熱_降雨機率取未來幾小時最高的()
    {
        WeatherResponse weather = WeatherService.ParseOpenMeteo(OpenMeteoJson(29.2, 32.6, 0, 0, 1, "[0, 10, 20]"));

        Assert.Equal(("晴朗", "clear"), (weather.description, weather.condition));
        Assert.Equal((29.2, 32.6), (weather.temperature_c, weather.apparent_temperature_c));
        Assert.Equal(20, weather.rain_probability);
        Assert.False(weather.is_raining);
        Assert.True(weather.is_day);
        Assert.Equal("目前天氣：晴朗，氣溫 29°C（體感 33°C），降雨機率 20%。天氣很熱，記得多喝水、做好防曬。", weather.message);
        Assert.Equal(new DateTime(2026, 10, 13, 9, 30, 0), weather.observed_at);
        Assert.Null(weather.station_name);
        Assert.Equal("Open-Meteo", weather.source);
    }

    [Fact]
    public void 備援來源_正在下雨與沒有降雨機率資料()
    {
        WeatherResponse rain = WeatherService.ParseOpenMeteo(OpenMeteoJson(22.4, 23.1, 1.8, 80, 0, "[90, 85, 60]"));
        Assert.Equal(("陣雨", "rain", true, false), (rain.description, rain.condition, rain.is_raining, rain.is_day));
        Assert.Equal("目前天氣：陣雨，氣溫 22°C，降雨機率 90%。外面正在下雨，記得帶傘，路滑小心走。", rain.message);   // 體感差不到 2 度就不另外講

        WeatherResponse cloudy = WeatherService.ParseOpenMeteo(OpenMeteoJson(24, 24.5, 0, 2, 1, "[null, null]"));
        Assert.Null(cloudy.rain_probability);
        Assert.Equal("目前天氣：多雲，氣溫 24°C。天氣不錯，很適合到處走走。", cloudy.message);

        WeatherResponse thunder = WeatherService.ParseOpenMeteo(OpenMeteoJson(27, 27, 4.2, 95, 1, "[100]"));
        Assert.Equal("thunder", thunder.condition);
        Assert.Equal("外面在打雷下雨，先到室內躲一下，等雨小一點再走", thunder.advice);
    }

    [Fact]
    public void 回應裡沒有目前天氣時當作失敗()
    {
        Assert.ThrowsAny<Exception>(() => WeatherService.ParseOpenMeteo("""{ "error": true, "reason": "Latitude must be in range of -90 to 90" }"""));
        Assert.ThrowsAny<Exception>(() => WeatherService.ParseCwaStations("""{ "message": "Unauthorized" }"""));
    }

    #endregion

    #region 提醒與天氣大類（兩個來源共用）

    [Theory]
    [InlineData("rain", true, 0, 34.0, 36.0, "外面正在下雨，記得帶傘，路滑小心走")]              // 下雨優先於天氣熱
    [InlineData("thunder", true, 0, 25.0, 25.0, "外面在打雷下雨，先到室內躲一下，等雨小一點再走")]
    [InlineData("cloudy", false, 70, 25.0, 25.0, "等一下可能會下雨，出門記得帶把傘")]            // 現在沒下，但降雨機率高
    [InlineData("cloudy", false, 49, 25.0, 25.0, "天氣不錯，很適合到處走走")]
    [InlineData("clear", false, 0, 30.0, 33.0, "天氣很熱，記得多喝水、做好防曬")]                // 氣溫還好但體感很熱
    [InlineData("overcast", false, 0, 14.0, 12.0, "天氣偏涼，多加一件外套")]
    public void 提醒的優先順序_打雷_下雨_可能下雨_太熱_偏涼(string condition, bool raining, int probability, double temperature, double apparent, string expected)
    {
        Assert.Equal(expected, WeatherService.Advice(condition, raining, probability, temperature, apparent));
    }

    [Theory]
    [InlineData("thunder", true, 0, 25.0, 25.0, "thunder")]
    [InlineData("thunder", false, 0, 25.0, 25.0, null)]      // 只聽到雷聲、沒下雨：不提醒
    [InlineData("rain", true, 0, 34.0, 36.0, "raining")]
    [InlineData("cloudy", false, 50, 25.0, 25.0, "rain_soon")]
    [InlineData("cloudy", false, 49, 25.0, 25.0, null)]
    [InlineData("clear", false, 0, 32.0, 32.0, "hot")]
    [InlineData("clear", false, 0, 30.0, 33.0, "hot")]       // 體感很熱也算
    [InlineData("overcast", false, 0, 15.0, 15.0, "cold")]
    [InlineData("clear", false, 0, 24.0, 25.0, null)]        // 天氣好：NPC 不用跳出來
    public void 值得NPC主動提醒的天氣狀況_天氣好時沒有(string condition, bool raining, int probability, double temperature, double apparent, string alert)
    {
        Assert.Equal(alert, WeatherService.AlertOf(condition, raining, probability, temperature, apparent));
    }

    [Theory]
    [InlineData("晴", "clear")]
    [InlineData("晴時多雲", "cloudy")]
    [InlineData("多雲", "cloudy")]
    [InlineData("陰", "overcast")]
    [InlineData("陰有靄", "fog")]
    [InlineData("晴有霾", "fog")]
    [InlineData("陰有雨", "rain")]
    [InlineData("多雲短暫陣雨", "rain")]
    [InlineData("陰大雷雨", "thunder")]
    [InlineData("多雲有雷聲", "thunder")]
    [InlineData("天氣狀況不明", "unknown")]
    [InlineData(null, "unknown")]
    public void 天氣現象對應到換圖示用的大類_同時有好幾種時取最嚴重的(string description, string condition)
    {
        Assert.Equal(condition, WeatherService.ConditionOf(description));
    }

    [Theory]
    [InlineData(1, "大致晴朗", false)]
    [InlineData(3, "陰天", false)]
    [InlineData(45, "有霧", false)]
    [InlineData(53, "毛毛雨", true)]
    [InlineData(63, "下雨", true)]
    [InlineData(82, "強陣雨", true)]
    [InlineData(95, "雷雨", true)]
    [InlineData(1234, "天氣狀況不明", false)]
    public void 備援來源的天氣代碼對應天氣現象與是否下雨(int code, string description, bool rain)
    {
        Assert.Equal(description, WeatherService.DescribeWmo(code));
        Assert.Equal(rain, WeatherService.IsRainCode(code));
    }

    #endregion
}
