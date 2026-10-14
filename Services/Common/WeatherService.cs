// 檔案路徑：System\Services\Common\WeatherService.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using backend.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace backend.Services
{
    /// <summary>
    /// 目前位置的天氣（地圖上 NPC 的天氣提醒用，GET api/Map/Weather）。
    /// 1. 主要來源：中央氣象署開放資料（https://opendata.cwa.gov.tw，要有授權碼，設定 Weather:CwaApiKey）
    ///    - 氣象觀測站 10 分鐘綜觀氣象資料（O-A0003-001）：離使用者最近、有氣溫的測站 → 天氣現象、氣溫、濕度、風速
    ///    - 今明 36 小時天氣預報（F-C0032-001）：測站所在縣市目前這個時段的降雨機率
    ///    兩份都是全臺一次拿回來，各自放 10 分鐘、30 分鐘，所以不管多少玩家開著地圖，往外查的次數都很少。
    /// 2. 備援來源：Open-Meteo（https://open-meteo.com，不需要金鑰）。沒有設定授權碼、氣象署連不上、
    ///    或使用者附近沒有測站時才用，回傳的 source 會標示實際用了哪一個。
    /// </summary>
    public class WeatherService
    {
        public const string CwaSource = "中央氣象署";
        public const string OpenMeteoSource = "Open-Meteo";

        private const string DefaultCwaBaseUrl = "https://opendata.cwa.gov.tw";
        private const string DefaultOpenMeteoBaseUrl = "https://api.open-meteo.com";
        private const string CwaObservationPath = "/api/v1/rest/datastore/O-A0003-001";
        private const string CwaForecastPath = "/api/v1/rest/datastore/F-C0032-001";

        private static readonly TimeSpan ObservationCacheFor = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ForecastCacheFor = TimeSpan.FromMinutes(30);

        /// <summary>最近的測站超過這個距離（公里）就不用它（例如使用者不在臺灣），改用備援來源</summary>
        private const double MaxStationKm = 50;

        /// <summary>降雨機率到這個數字（%）就提醒帶傘</summary>
        private const int UmbrellaProbability = 50;

        /// <summary>「等一下會不會下雨」看多遠：這幾個小時內會進到下一個預報時段的話，降雨機率取兩個時段較高的</summary>
        private const int LookAheadHours = 3;
        private const double HotCelsius = 32;
        private const double CoolCelsius = 15;

        /// <summary>氣象署用 -99 這類數字代表「沒有觀測值」</summary>
        private const double MissingBelow = -90;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<WeatherService> _logger;
        private readonly string _cwaApiKey;
        private readonly string _cwaBaseUrl;
        private readonly string _openMeteoBaseUrl;

        // 整份換掉（指派參考是不可分割的），同時有很多請求進來也不會讀到換到一半的資料
        private Cached<List<CwaStation>> _stations;
        private Cached<Dictionary<string, CwaForecast>> _forecasts;
        private readonly ConcurrentDictionary<string, (DateTime fetchedAt, WeatherResponse weather)> _openMeteoCache = new();

        public WeatherService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<WeatherService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _cwaApiKey = configuration["Weather:CwaApiKey"]?.Trim();
            _cwaBaseUrl = BaseUrl(configuration["Weather:CwaBaseUrl"], DefaultCwaBaseUrl);
            _openMeteoBaseUrl = BaseUrl(configuration["Weather:OpenMeteoBaseUrl"], DefaultOpenMeteoBaseUrl);
        }

        private sealed class Cached<T>
        {
            public DateTime fetchedAt { get; init; }
            public T value { get; init; }
        }

        private static string BaseUrl(string configured, string fallback) =>
            (string.IsNullOrWhiteSpace(configured) ? fallback : configured).TrimEnd('/');

        /// <summary>這個位置目前的天氣；兩個來源都查不到時丟出錯誤</summary>
        public async Task<WeatherResponse> GetCurrentAsync(double lat, double lng)
        {
            if (lat < -90 || lat > 90 || lng < -180 || lng > 180 || (lat == 0 && lng == 0))
                throw new BadRequestException("請帶目前位置的經緯度（lat、lng）");

            if (!string.IsNullOrEmpty(_cwaApiKey))
            {
                try
                {
                    return await FromCwaAsync(lat, lng);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "向中央氣象署查天氣失敗（{Lat}, {Lng}），改用備援的 Open-Meteo", lat, lng);
                }
            }

            try
            {
                return await FromOpenMeteoAsync(lat, lng);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "查詢天氣失敗（{Lat}, {Lng}）", lat, lng);
                throw new Exception("天氣服務暫時查不到資料，請稍後再試");
            }
        }

        private async Task<string> GetStringAsync(string url)
        {
            HttpClient client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            using HttpResponseMessage response = await client.GetAsync(url);
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Status {(int)response.StatusCode}：{(body.Length > 200 ? body[..200] : body)}");

            return body;
        }

        #region 中央氣象署

        /// <summary>氣象站的一筆即時觀測</summary>
        internal class CwaStation
        {
            public string name { get; set; }
            public string county { get; set; }
            public double lat { get; set; }
            public double lng { get; set; }

            /// <summary>天氣現象（例如「陰有雨」）；測站沒有這一項時為 null</summary>
            public string weather { get; set; }

            public double? temperature { get; set; }

            /// <summary>相對濕度（%）</summary>
            public double? humidity { get; set; }

            /// <summary>風速（公尺／秒）</summary>
            public double? wind_speed { get; set; }

            /// <summary>觀測時間（台灣時間）</summary>
            public DateTime? observed_at { get; set; }
        }

        /// <summary>一個縣市目前這個預報時段的天氣</summary>
        internal class CwaForecast
        {
            public string weather { get; set; }
            public int? rain_probability { get; set; }
        }

        private async Task<WeatherResponse> FromCwaAsync(double lat, double lng)
        {
            DateTime now = DateTime.UtcNow;
            DateTime taiwanNow = now.AddHours(8);

            Cached<List<CwaStation>> stations = _stations;
            if (stations == null || now - stations.fetchedAt >= ObservationCacheFor)
                _stations = stations = new Cached<List<CwaStation>> { fetchedAt = now, value = ParseCwaStations(await GetStringAsync(CwaUrl(CwaObservationPath))) };

            CwaStation station = Nearest(stations.value, lat, lng)
                ?? throw new Exception($"附近 {MaxStationKm} 公里內沒有可用的氣象站");

            // 預報查不到只是少了降雨機率，觀測資料照樣回傳
            CwaForecast forecast = null;
            try
            {
                Cached<Dictionary<string, CwaForecast>> forecasts = _forecasts;
                if (forecasts == null || now - forecasts.fetchedAt >= ForecastCacheFor)
                    _forecasts = forecasts = new Cached<Dictionary<string, CwaForecast>> { fetchedAt = now, value = ParseCwaForecasts(await GetStringAsync(CwaUrl(CwaForecastPath)), taiwanNow) };

                forecast = forecasts.value.GetValueOrDefault(County(station.county));
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "向中央氣象署查縣市天氣預報失敗，這次不提供降雨機率");
            }

            return FromCwa(station, forecast, taiwanNow);
        }

        private string CwaUrl(string path) => $"{_cwaBaseUrl}{path}?Authorization={Uri.EscapeDataString(_cwaApiKey)}&format=JSON";

        /// <summary>離這個位置最近、有氣溫的測站；太遠（MaxStationKm）就當作沒有</summary>
        internal static CwaStation Nearest(IEnumerable<CwaStation> stations, double lat, double lng)
        {
            var nearest = (stations ?? Enumerable.Empty<CwaStation>())
                .Where(s => s.temperature.HasValue)
                .Select(s => (station: s, meters: Geo.DistanceMeters(lat, lng, s.lat, s.lng)))
                .OrderBy(x => x.meters)
                .FirstOrDefault();

            return nearest.station != null && nearest.meters <= MaxStationKm * 1000 ? nearest.station : null;
        }

        /// <summary>
        /// O-A0003-001 的回應 → 測站清單。每個測站：StationName、ObsTime.DateTime、
        /// GeoInfo（Coordinates 取 WGS84 那一組、CountyName）、WeatherElement（Weather、AirTemperature、RelativeHumidity、WindSpeed）。
        /// 沒有座標的測站略過；沒有觀測值（-99）的欄位為 null
        /// </summary>
        internal static List<CwaStation> ParseCwaStations(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            var stations = new List<CwaStation>();

            foreach (JsonElement s in doc.RootElement.GetProperty("records").GetProperty("Station").EnumerateArray())
            {
                if (!s.TryGetProperty("GeoInfo", out JsonElement geo) || !s.TryGetProperty("WeatherElement", out JsonElement element)) continue;

                JsonElement? coordinates = null;
                if (geo.TryGetProperty("Coordinates", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement c in list.EnumerateArray())
                    {
                        coordinates ??= c;
                        if (Text(c, "CoordinateName") == "WGS84") coordinates = c;
                    }
                }

                double? lat = coordinates.HasValue ? Number(coordinates.Value, "StationLatitude") : null;
                double? lng = coordinates.HasValue ? Number(coordinates.Value, "StationLongitude") : null;
                if (lat == null || lng == null) continue;

                string weather = Text(element, "Weather");
                string observed = s.TryGetProperty("ObsTime", out JsonElement obs) ? Text(obs, "DateTime") : null;

                stations.Add(new CwaStation
                {
                    name = Text(s, "StationName"),
                    county = Text(geo, "CountyName"),
                    lat = lat.Value,
                    lng = lng.Value,
                    weather = string.IsNullOrWhiteSpace(weather) || weather.StartsWith("-99") || weather == "X" ? null : weather.Trim(),
                    temperature = Number(element, "AirTemperature"),
                    humidity = Number(element, "RelativeHumidity"),
                    wind_speed = Number(element, "WindSpeed"),
                    observed_at = DateTimeOffset.TryParse(observed, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset time)
                        ? time.ToOffset(TimeSpan.FromHours(8)).DateTime : null
                });
            }

            return stations;
        }

        /// <summary>
        /// F-C0032-001 的回應 → 縣市 → 目前這個時段的天氣現象（Wx）與降雨機率（PoP）。
        /// 每個項目有三個時段（一段 12 小時），取包含現在的那一個，都不包含時取第一個。
        /// 降雨機率要回答「等一下會不會下雨」：LookAheadHours 小時內會進到下一個時段的話，取兩個時段較高的
        /// </summary>
        internal static Dictionary<string, CwaForecast> ParseCwaForecasts(string json, DateTime taiwanNow)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            var result = new Dictionary<string, CwaForecast>();

            foreach (JsonElement location in doc.RootElement.GetProperty("records").GetProperty("location").EnumerateArray())
            {
                string county = Text(location, "locationName");
                if (string.IsNullOrWhiteSpace(county) || !location.TryGetProperty("weatherElement", out JsonElement elements)) continue;

                var forecast = new CwaForecast();
                foreach (JsonElement element in elements.EnumerateArray())
                {
                    switch (Text(element, "elementName"))
                    {
                        case "Wx":
                            forecast.weather = PeriodValue(element, taiwanNow, orFirst: true);
                            break;
                        case "PoP":
                            forecast.rain_probability = new[]
                                {
                                    PeriodValue(element, taiwanNow, orFirst: true),
                                    PeriodValue(element, taiwanNow.AddHours(LookAheadHours), orFirst: false)
                                }
                                .Select(value => int.TryParse(value, out int pop) && pop >= 0 ? pop : (int?)null)
                                .Max();
                            break;
                    }
                }

                result[County(county)] = forecast;
            }

            return result;
        }

        /// <summary>包含這個時間的那個預報時段的值；都不包含時回傳第一個時段的（orFirst）或 null</summary>
        private static string PeriodValue(JsonElement element, DateTime time, bool orFirst)
        {
            if (!element.TryGetProperty("time", out JsonElement times) || times.ValueKind != JsonValueKind.Array) return null;

            string first = null;
            foreach (JsonElement period in times.EnumerateArray())
            {
                string value = period.TryGetProperty("parameter", out JsonElement parameter) ? Text(parameter, "parameterName") : null;
                first ??= value;

                if (DateTime.TryParse(Text(period, "startTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime start)
                    && DateTime.TryParse(Text(period, "endTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime end)
                    && time >= start && time < end)
                {
                    return value;
                }
            }

            return orFirst ? first : null;
        }

        /// <summary>縣市名稱比對用：氣象署一律寫「臺」，保險起見兩種寫法都接受</summary>
        private static string County(string name) => (name ?? "").Trim().Replace('台', '臺');

        /// <summary>
        /// 測站觀測 + 縣市預報 → 天氣。天氣現象用測站的；測站沒有這一項時用縣市預報的。
        /// 「正在下雨」只看測站現在的天氣現象有沒有「雨」（預報是一整個時段的，不能代表現在）
        /// </summary>
        internal static WeatherResponse FromCwa(CwaStation station, CwaForecast forecast, DateTime taiwanNow)
        {
            string description = station.weather ?? (string.IsNullOrWhiteSpace(forecast?.weather) ? "天氣狀況不明" : forecast.weather.Trim());
            double temperature = station.temperature.Value;
            double apparent = ApparentTemperature(temperature, station.humidity, station.wind_speed);
            bool raining = station.weather != null && station.weather.Contains('雨');

            return Compose(description, temperature, apparent, forecast?.rain_probability, raining,
                isDay: taiwanNow.Hour is >= 6 and < 18,
                observedAt: station.observed_at ?? taiwanNow,
                stationName: station.name,
                source: CwaSource);
        }

        /// <summary>
        /// 體感溫度（氣象署採用的公式）：1.04×氣溫 + 0.2×水氣壓 − 0.65×風速 − 2.7，
        /// 水氣壓 = 濕度/100 × 6.105 × e^(17.27×氣溫 / (237.7 + 氣溫))。缺濕度或風速時回傳氣溫
        /// </summary>
        internal static double ApparentTemperature(double temperature, double? humidity, double? windSpeed)
        {
            if (humidity == null || windSpeed == null) return temperature;

            double vaporPressure = humidity.Value / 100 * 6.105 * Math.Exp(17.27 * temperature / (237.7 + temperature));
            return 1.04 * temperature + 0.2 * vaporPressure - 0.65 * windSpeed.Value - 2.7;
        }

        private static string Text(JsonElement parent, string name) =>
            parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;

        /// <summary>數字欄位：接受數字或數字字串；沒有觀測值（-99）回傳 null</summary>
        private static double? Number(JsonElement parent, string name)
        {
            if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out JsonElement value)) return null;

            double? number = value.ValueKind switch
            {
                JsonValueKind.Number => value.GetDouble(),
                JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
                _ => null
            };

            return number > MissingBelow ? number : null;
        }

        #endregion

        #region 備援：Open-Meteo

        private async Task<WeatherResponse> FromOpenMeteoAsync(double lat, double lng)
        {
            // 同一帶（經緯度取到小數 2 位，約 1 公里）10 分鐘內只查一次
            string key = FormattableString.Invariant($"{lat:F2},{lng:F2}");
            DateTime now = DateTime.UtcNow;
            if (_openMeteoCache.TryGetValue(key, out var cached) && now - cached.fetchedAt < ObservationCacheFor)
                return cached.weather;

            string url = FormattableString.Invariant($"{_openMeteoBaseUrl}/v1/forecast?latitude={lat:F4}&longitude={lng:F4}")
                + "&current=temperature_2m,apparent_temperature,precipitation,weather_code,is_day"
                + "&hourly=precipitation_probability&forecast_hours=3&timezone=Asia%2FTaipei";

            WeatherResponse weather = ParseOpenMeteo(await GetStringAsync(url));
            _openMeteoCache[key] = (now, weather);

            // 順手清掉過期的，避免玩家到處走之後越存越多
            foreach (var old in _openMeteoCache.Where(c => now - c.Value.fetchedAt >= ObservationCacheFor).ToList())
                _openMeteoCache.TryRemove(old.Key, out _);

            return weather;
        }

        /// <summary>Open-Meteo 的回應 → 天氣；缺少目前天氣時丟出錯誤</summary>
        internal static WeatherResponse ParseOpenMeteo(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement current = doc.RootElement.GetProperty("current");

            double temperature = current.GetProperty("temperature_2m").GetDouble();
            double apparent = Number(current, "apparent_temperature") ?? temperature;
            double precipitation = Number(current, "precipitation") ?? 0;
            int code = current.GetProperty("weather_code").GetInt32();
            bool isDay = (Number(current, "is_day") ?? 1) == 1;

            // 未來幾小時裡最高的降雨機率
            int? rainProbability = null;
            if (doc.RootElement.TryGetProperty("hourly", out JsonElement hourly)
                && hourly.TryGetProperty("precipitation_probability", out JsonElement probabilities)
                && probabilities.ValueKind == JsonValueKind.Array)
            {
                var values = probabilities.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetDouble()).ToList();
                if (values.Count > 0) rainProbability = (int)Math.Round(values.Max());
            }

            DateTime observedAt = DateTime.TryParse(Text(current, "time"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time)
                ? time : DateTime.UtcNow.AddHours(8);

            return Compose(DescribeWmo(code), temperature, apparent, rainProbability,
                raining: precipitation > 0 || IsRainCode(code),
                isDay: isDay,
                observedAt: observedAt,
                stationName: null,
                source: OpenMeteoSource);
        }

        /// <summary>WMO 天氣代碼（Open-Meteo 用的）→ 天氣現象</summary>
        internal static string DescribeWmo(int code) => code switch
        {
            0 => "晴朗",
            1 => "大致晴朗",
            2 => "多雲",
            3 => "陰天",
            45 or 48 => "有霧",
            51 or 53 or 55 => "毛毛雨",
            56 or 57 or 66 or 67 => "凍雨",
            61 => "小雨",
            63 => "下雨",
            65 => "大雨",
            71 or 73 or 75 or 77 => "下雪",
            80 or 81 => "陣雨",
            82 => "強陣雨",
            85 or 86 => "陣雪",
            95 => "雷雨",
            96 or 99 => "雷雨夾冰雹",
            _ => "天氣狀況不明"
        };

        /// <summary>代碼本身就是在下雨（毛毛雨、雨、陣雨、雷雨）</summary>
        internal static bool IsRainCode(int code) => code is (>= 51 and <= 67) or (>= 80 and <= 82) or (>= 95 and <= 99);

        #endregion

        #region 提醒與對話（兩個來源共用，純函式）

        private static WeatherResponse Compose(string description, double temperature, double apparent, int? rainProbability, bool raining,
            bool isDay, DateTime observedAt, string stationName, string source)
        {
            string condition = ConditionOf(description);
            string advice = Advice(condition, raining, rainProbability, temperature, apparent);

            return new WeatherResponse
            {
                description = description,
                condition = condition,
                temperature_c = Math.Round(temperature, 1),
                apparent_temperature_c = Math.Round(apparent, 1),
                rain_probability = rainProbability,
                is_raining = raining,
                is_day = isDay,
                alert = AlertOf(condition, raining, rainProbability, temperature, apparent),
                advice = advice,
                message = BuildMessage(description, temperature, apparent, rainProbability, advice),
                observed_at = observedAt,
                station_name = stationName,
                source = source
            };
        }

        /// <summary>天氣現象 → 大類（前端換圖示用）。同時有好幾種時取最嚴重的：雷 → 雪 → 雨 → 霧 → 陰 → 多雲 → 晴</summary>
        internal static string ConditionOf(string description)
        {
            string text = description ?? "";
            if (text.Contains('雷')) return "thunder";
            if (text.Contains('雪')) return "snow";
            if (text.Contains('雨')) return "rain";
            if (text.Contains('霧') || text.Contains('靄') || text.Contains('霾')) return "fog";
            if (text.Contains('陰')) return "overcast";
            if (text.Contains('雲')) return "cloudy";
            if (text.Contains('晴')) return "clear";
            return "unknown";
        }

        /// <summary>
        /// 值得 NPC 主動提醒玩家的狀況，依嚴重程度：thunder 打雷下雨 → raining 正在下雨 → rain_soon 等一下可能下雨
        /// → hot 太熱 → cold 偏涼；都不是（天氣沒什麼要特別提醒的）回傳 null
        /// </summary>
        internal static string AlertOf(string condition, bool raining, int? rainProbability, double temperature, double apparent)
        {
            if (raining && condition == "thunder") return "thunder";
            if (raining) return "raining";
            if (rainProbability >= UmbrellaProbability) return "rain_soon";
            if (Math.Max(temperature, apparent) >= HotCelsius) return "hot";
            if (Math.Min(temperature, apparent) <= CoolCelsius) return "cold";
            return null;
        }

        /// <summary>給玩家的提醒：打雷 → 正在下雨 → 等一下可能下雨 → 太熱 → 偏涼 → 適合出門</summary>
        internal static string Advice(string condition, bool raining, int? rainProbability, double temperature, double apparent) =>
            AlertOf(condition, raining, rainProbability, temperature, apparent) switch
            {
                "thunder" => "外面在打雷下雨，先到室內躲一下，等雨小一點再走",
                "raining" => "外面正在下雨，記得帶傘，路滑小心走",
                "rain_soon" => "等一下可能會下雨，出門記得帶把傘",
                "hot" => "天氣很熱，記得多喝水、做好防曬",
                "cold" => "天氣偏涼，多加一件外套",
                _ => "天氣不錯，很適合到處走走"
            };

        /// <summary>NPC 對話泡泡用的一句話；體感跟氣溫差 2 度以上才另外講體感</summary>
        internal static string BuildMessage(string description, double temperature, double apparent, int? rainProbability, string advice)
        {
            string message = $"目前天氣：{description}，氣溫 {Math.Round(temperature)}°C";
            if (Math.Abs(apparent - temperature) >= 2) message += $"（體感 {Math.Round(apparent)}°C）";
            if (rainProbability.HasValue) message += $"，降雨機率 {rainProbability}%";
            return $"{message}。{advice}。";
        }

        #endregion
    }
}
