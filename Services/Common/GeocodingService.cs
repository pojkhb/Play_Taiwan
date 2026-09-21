// 檔案路徑：System\Services\Common\GeocodingService.cs
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace backend.Services
{
    /// <summary>
    /// 共用的地理編碼服務。
    /// ResolveTaiwanAreaAsync：座標 → 縣市/鄉鎮（反向地理編碼，MapService 回報定位、Story GPS 生成劇本使用）。
    /// SearchPlaceCoordinatesAsync：地名 → 座標（正向地理編碼，AI 生成劇本的地點要畫在地圖上時使用）。
    /// </summary>
    public class GeocodingService
    {
        /// <summary>座標 → 縣市/鄉鎮區名稱（反向地理編碼）。</summary>
        public async Task<(string city, string district)> ResolveTaiwanAreaAsync(double lat, double lng)
        {
            string url =
                "https://nominatim.openstreetmap.org/reverse" +
                $"?format=json&lat={lat}&lon={lng}&accept-language=zh-TW";

            using var http = new HttpClient();
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

            using var http = new HttpClient();
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