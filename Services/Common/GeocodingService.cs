// 檔案路徑：System\Services\Common\GeocodingService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace backend.Services
{
    /// <summary>
    /// 共用的地理編碼服務。
    /// ResolveTaiwanAreaAsync：座標 → 縣市/鄉鎮（反向地理編碼，MapService 回報定位、Story GPS 生成劇本使用）。
    /// SearchPlaceCoordinatesAsync：地名 → 座標（正向地理編碼，AI 生成劇本的地點要畫在地圖上時使用）。
    /// GeocodeAddressAsync：台灣地址 → 座標（TDX 地址定位，商家自建景點只填地址時使用）。
    /// ReverseGeocodeAddressAsync：座標 → 最近的門牌地址（TDX，商家自建景點只填座標時使用）。
    /// </summary>
    public class GeocodingService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TdxClient _tdxClient;

        public GeocodingService(IHttpClientFactory httpClientFactory, TdxClient tdxClient)
        {
            _httpClientFactory = httpClientFactory;
            _tdxClient = tdxClient;
        }

        /// <summary>TDX 地址定位的回傳項目</summary>
        private class TdxGeocodeItem
        {
            public string Address { get; set; }
            public string Geometry { get; set; }
        }

        public class AddressGeocodeResult
        {
            /// <summary>TDX 實際對到的地址，例如「台中市西區安龍里英才路600號」</summary>
            public string matched_address { get; set; }
            public double lat { get; set; }
            public double lng { get; set; }

            /// <summary>
            /// 是否對到門牌。對不到門牌時 TDX 會退回鄉鎮區的中心點（matched_address 只到「台中市西區」），
            /// 門牌號碼不存在但路名對時會給同一條路上最近的門牌（仍算對到）。
            /// </summary>
            public bool is_door_level => matched_address?.Contains('號') == true;
        }

        private static readonly Regex WktPointRegex = new Regex(@"POINT\s*\(\s*([-\d.]+)\s+([-\d.]+)\s*\)", RegexOptions.IgnoreCase);

        /// <summary>
        /// 台灣地址 → 座標（TDX 地址定位，使用內政部門牌資料）。查不到時回傳 null。
        /// 呼叫端用 is_door_level 判斷是否對到門牌。
        /// </summary>
        public async Task<AddressGeocodeResult> GeocodeAddressAsync(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;

            return ToGeocodeResult(await _tdxClient.GetAdvancedAsync<List<TdxGeocodeItem>>(
                "V3/Map/GeoCode/Coordinate/Address/" + Uri.EscapeDataString(address.Trim())));
        }

        /// <summary>
        /// 座標 → 最近的門牌地址（TDX 地址反查，使用內政部門牌資料），回傳的 lat/lng 是該門牌的座標。
        /// TDX 一律回最近的門牌（座標在海上也會回幾十公里外的地址），呼叫端要自己檢查門牌離座標多遠。
        /// </summary>
        public async Task<AddressGeocodeResult> ReverseGeocodeAddressAsync(double lat, double lng)
        {
            string x = lng.ToString(CultureInfo.InvariantCulture);
            string y = lat.ToString(CultureInfo.InvariantCulture);

            return ToGeocodeResult(await _tdxClient.GetAdvancedAsync<List<TdxGeocodeItem>>(
                $"V3/Map/GeoLocating/Address/LocationX/{x}/LocationY/{y}"));
        }

        /// <summary>TDX 回傳第一筆的地址與 WKT 座標「POINT (經度 緯度)」，沒有結果時回傳 null</summary>
        private static AddressGeocodeResult ToGeocodeResult(List<TdxGeocodeItem> items)
        {
            TdxGeocodeItem first = items?.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Geometry));
            Match point = first == null ? null : WktPointRegex.Match(first.Geometry);
            if (point == null || !point.Success) return null;

            return new AddressGeocodeResult
            {
                matched_address = first.Address,
                lng = double.Parse(point.Groups[1].Value, CultureInfo.InvariantCulture),
                lat = double.Parse(point.Groups[2].Value, CultureInfo.InvariantCulture)
            };
        }

        /// <summary>座標 → 縣市/鄉鎮區名稱（反向地理編碼）。</summary>
        public async Task<(string city, string district)> ResolveTaiwanAreaAsync(double lat, double lng)
        {
            string url =
                "https://nominatim.openstreetmap.org/reverse" +
                $"?format=json&lat={lat}&lon={lng}&accept-language=zh-TW";

            HttpClient http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PlayTaiwan/1.0 (local-dev)");

            string json = await http.GetStringAsync(url);
            JObject root = JObject.Parse(json);
            JToken address = root["address"];

            if (address == null)
            {
                return (null, null);
            }

            string city =
                address.Value<string>("city") ??
                address.Value<string>("county") ??
                address.Value<string>("state");

            string district =
                address.Value<string>("suburb") ??
                address.Value<string>("city_district") ??
                address.Value<string>("town") ??
                address.Value<string>("municipality") ??
                address.Value<string>("village");

            return (city, district);
        }

        /// <summary>
        /// 地名 → 座標（正向地理編碼）。用於 AI 生成劇本的地點名稱轉成實際經緯度，
        /// 讓地圖頁面能正確標示出這個節點的位置。查無結果時回傳 (null, null)。
        /// </summary>
        public async Task<(double? lat, double? lng)> SearchPlaceCoordinatesAsync(string placeName, string cityName)
        {
            if (string.IsNullOrWhiteSpace(placeName)) return (null, null);

            string query = string.IsNullOrWhiteSpace(cityName) ? placeName : $"{cityName}{placeName}";
            string url =
                "https://nominatim.openstreetmap.org/search" +
                $"?format=json&q={Uri.EscapeDataString(query)}&countrycodes=tw&limit=1&accept-language=zh-TW";

            HttpClient http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PlayTaiwan/1.0 (local-dev)");

            string json = await http.GetStringAsync(url);
            var results = JArray.Parse(json);

            if (results.Count == 0) return (null, null);

            var first = results[0];
            double lat = first.Value<double>("lat");
            double lng = first.Value<double>("lon");

            return (lat, lng);
        }
    }
}