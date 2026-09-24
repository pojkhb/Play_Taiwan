// 檔案路徑：System\Services\Bus\TdxClient.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace backend.Services
{
    /// <summary>
    /// 交通部 TDX 運輸資料流通服務的呼叫端（Singleton）：
    /// 1. client_credentials 換 Token，快取到過期前 5 分鐘
    /// 2. 全域限速（每次呼叫間隔至少 MinIntervalMs），遇到 429 自動退避重試
    /// 帳號設定在 appsettings.json 的 AppSettings:tdx_client_id / tdx_client_secret。
    /// </summary>
    public class TdxClient
    {
        public const string HttpClientName = "tdx";

        private const string TokenUrl = "https://tdx.transportdata.tw/auth/realms/TDXConnect/protocol/openid-connect/token";
        private const string ApiBaseUrl = "https://tdx.transportdata.tw/api/basic/";
        private const int MinIntervalMs = 1500;
        private const int MaxRetries = 5;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _clientId;
        private readonly string _clientSecret;

        private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _rateLock = new SemaphoreSlim(1, 1);
        private string _token;
        private DateTime _tokenExpiresAt = DateTime.MinValue;
        private DateTime _lastCallAt = DateTime.MinValue;

        public TdxClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _clientId = configuration["AppSettings:tdx_client_id"];
            _clientSecret = configuration["AppSettings:tdx_client_secret"];
        }


        /// <summary>
        /// GET TDX API，path 例如 "v2/Bus/Route/City/Taichung"（可帶 $filter 等查詢參數），自動加上 $format=JSON。
        /// </summary>
        public async Task<T> GetAsync<T>(string path)
        {
            string url = ApiBaseUrl + path.TrimStart('/');
            url += (url.Contains("?") ? "&" : "?") + "$format=JSON";

            for (int attempt = 1; ; attempt++)
            {
                await WaitForRateLimitAsync();

                HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await GetTokenAsync());

                HttpResponseMessage response = await client.SendAsync(request);

                if ((int)response.StatusCode == 429 && attempt < MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15 * attempt));
                    continue;
                }

                if ((int)response.StatusCode == 401 && attempt < MaxRetries)
                {
                    _tokenExpiresAt = DateTime.MinValue;   // Token 失效，重換一次
                    continue;
                }

                string body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new Exception($"TDX API 錯誤 (HTTP {(int)response.StatusCode}) {path}: {Truncate(body, 300)}");

                return JsonSerializer.Deserialize<T>(body, JsonOptions);
            }
        }


        private async Task WaitForRateLimitAsync()
        {
            await _rateLock.WaitAsync();
            try
            {
                double waitMs = MinIntervalMs - (DateTime.UtcNow - _lastCallAt).TotalMilliseconds;
                if (waitMs > 0) await Task.Delay((int)waitMs);
                _lastCallAt = DateTime.UtcNow;
            }
            finally
            {
                _rateLock.Release();
            }
        }


        private async Task<string> GetTokenAsync()
        {
            if (_token != null && DateTime.UtcNow < _tokenExpiresAt) return _token;

            await _tokenLock.WaitAsync();
            try
            {
                if (_token != null && DateTime.UtcNow < _tokenExpiresAt) return _token;

                if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
                    throw new Exception("找不到 TDX 帳號，請在 appsettings.json 的 AppSettings 設定 tdx_client_id / tdx_client_secret");

                HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "grant_type", "client_credentials" },
                    { "client_id", _clientId },
                    { "client_secret", _clientSecret }
                });

                HttpResponseMessage response = await client.PostAsync(TokenUrl, form);
                string body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new Exception($"TDX 取得 Token 失敗 (HTTP {(int)response.StatusCode}): {Truncate(body, 300)}");

                using JsonDocument doc = JsonDocument.Parse(body);
                _token = doc.RootElement.GetProperty("access_token").GetString();
                int expiresIn = doc.RootElement.TryGetProperty("expires_in", out JsonElement exp) ? exp.GetInt32() : 3600;
                _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 300);
                return _token;
            }
            finally
            {
                _tokenLock.Release();
            }
        }


        private static string Truncate(string s, int max) => s == null || s.Length <= max ? s : s.Substring(0, max);


        #region 縣市對照

        /// <summary>TDX 縣市參數 → 中文縣市名稱</summary>
        public static readonly Dictionary<string, string> CityNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Taipei", "臺北市" }, { "NewTaipei", "新北市" }, { "Taoyuan", "桃園市" }, { "Taichung", "臺中市" },
            { "Tainan", "臺南市" }, { "Kaohsiung", "高雄市" }, { "Keelung", "基隆市" }, { "Hsinchu", "新竹市" },
            { "HsinchuCounty", "新竹縣" }, { "MiaoliCounty", "苗栗縣" }, { "ChanghuaCounty", "彰化縣" },
            { "NantouCounty", "南投縣" }, { "YunlinCounty", "雲林縣" }, { "Chiayi", "嘉義市" }, { "ChiayiCounty", "嘉義縣" },
            { "PingtungCounty", "屏東縣" }, { "YilanCounty", "宜蘭縣" }, { "HualienCounty", "花蓮縣" },
            { "TaitungCounty", "臺東縣" }, { "KinmenCounty", "金門縣" }, { "PenghuCounty", "澎湖縣" }, { "LienchiangCounty", "連江縣" }
        };

        /// <summary>TDX 縣市代碼（StopUID / RouteUID 前三碼）→ TDX 縣市參數；THB 是公路客運（InterCity）</summary>
        public static readonly Dictionary<string, string> CityCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "TPE", "Taipei" }, { "NWT", "NewTaipei" }, { "TAO", "Taoyuan" }, { "TXG", "Taichung" }, { "TNN", "Tainan" },
            { "KHH", "Kaohsiung" }, { "KEE", "Keelung" }, { "HSZ", "Hsinchu" }, { "HSQ", "HsinchuCounty" },
            { "MIA", "MiaoliCounty" }, { "CHA", "ChanghuaCounty" }, { "NAN", "NantouCounty" }, { "YUN", "YunlinCounty" },
            { "CYI", "Chiayi" }, { "CYQ", "ChiayiCounty" }, { "PIF", "PingtungCounty" }, { "ILA", "YilanCounty" },
            { "HUA", "HualienCounty" }, { "TTT", "TaitungCounty" }, { "KIN", "KinmenCounty" }, { "PEN", "PenghuCounty" },
            { "LIE", "LienchiangCounty" }, { "THB", "InterCity" }
        };

        /// <summary>中文縣市名稱（臺/台皆可）→ TDX 縣市參數；本來就是英文參數時原樣回傳</summary>
        public static string ResolveCity(string city)
        {
            if (string.IsNullOrWhiteSpace(city)) return null;
            string key = city.Trim();
            if (CityNames.ContainsKey(key))
            {
                foreach (var pair in CityNames) if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) return pair.Key;
            }
            string zh = key.Replace("台", "臺");
            foreach (var pair in CityNames) if (pair.Value == zh) return pair.Key;
            return null;
        }

        #endregion
    }
}
