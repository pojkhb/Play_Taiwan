// 檔案路徑：System\Services\Merchant\StoreLocationService.cs
// 商家自建景點的位置：地址與座標最後都要有，商家只填一個時自動轉另一個（TDX 內政部門牌資料），
// 轉不了才請商家兩個都填；並依縣市＋鄉鎮市區把景點掛到 Neo4j 的 Town。
// 商家註冊、更新店家資料，以及註冊畫面自動帶出地址／座標的 API 共用。
using System;
using System.Threading.Tasks;
using backend.Models;
using backend.Services.Neo4j;
using backend.ViewModels;
using Microsoft.Extensions.Logging;

namespace backend.Services
{
    public class StoreLocationService
    {
        // 座標轉地址時，TDX 回傳的最近門牌離座標超過這個距離就當作轉不了
        // （TDX 一律回最近的門牌，座標在海上也會回幾十公里外的地址）
        private const double ReverseGeocodeMaxDistanceMeters = 300;

        private readonly PlaceVersionChainService _placeService;
        private readonly GeocodingService _geocoding;
        private readonly ILogger<StoreLocationService> _logger;

        public StoreLocationService(PlaceVersionChainService placeService, GeocodingService geocoding, ILogger<StoreLocationService> logger)
        {
            _placeService = placeService;
            _geocoding = geocoding;
            _logger = logger;
        }

        /// <summary>
        /// 送出註冊／更新時決定店家位置：
        /// - 地址、座標都有：照商家填的
        /// - 只有地址：地址轉座標（要對到門牌）
        /// - 只有座標：座標轉地址（300 公尺內要有門牌）
        /// 轉不了回 400，請商家兩個都填。縣市＋鄉鎮市區有填時要對得到 Neo4j 的鄉鎮市區，沒填時從地址判斷。
        /// </summary>
        public async Task<MerchantPlaceLocation> ResolveAsync(string city, string town, string address, double? lat, double? lng)
        {
            ValidateCoordinates(lat, lng, "lat、lng");

            bool hasAddress = !string.IsNullOrWhiteSpace(address);

            if (hasAddress && lat.HasValue) return await FromBothAsync(city, town, address, lat.Value, lng.Value);
            if (hasAddress) return await FromAddressAsync(city, town, address);
            if (lat.HasValue) return await FromCoordinatesAsync(city, town, lat.Value, lng.Value);

            throw new BadRequestException("請提供店家地址或座標（lat、lng），至少填一個");
        }

        /// <summary>座標選填，但要 lat、lng 一起帶，且落在臺灣（含離島）範圍內，避免經緯度填反</summary>
        public static void ValidateCoordinates(double? lat, double? lng, string fieldNames)
        {
            if (lat.HasValue != lng.HasValue)
            {
                throw new BadRequestException($"{fieldNames} 要一起帶");
            }
            if (lat.HasValue && (lat < 21 || lat > 27 || lng < 118 || lng > 123))
            {
                throw new BadRequestException($"{fieldNames} 不在臺灣範圍內，請確認經緯度是否填反");
            }
        }

        /// <summary>地址、座標都有：照商家填的，只補上鄉鎮市區</summary>
        private async Task<MerchantPlaceLocation> FromBothAsync(string city, string town, string address, double lat, double lng)
        {
            TownRef townRef = await FindGivenTownAsync(city, town)
                ?? await _placeService.FindTownByAddressAsync(address)
                ?? throw new BadRequestException("請提供店家所在的縣市與鄉鎮市區（store_city、store_town）");

            return Build(townRef, address, lat, lng);
        }

        /// <summary>
        /// 只有地址：用 TDX 轉座標，要對到門牌（對不到時 TDX 只回鄉鎮市區中心點，可能差好幾公里，玩家到店門口會無法抵達、作答）。
        /// </summary>
        public async Task<MerchantPlaceLocation> FromAddressAsync(string city, string town, string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new BadRequestException("請提供店家地址（store_address）");
            }

            TownRef given = await FindGivenTownAsync(city, town);

            // 地址開頭自己寫了縣市鄉鎮、又跟選的不同時，不用問 TDX 就知道不一致
            TownRef written = given == null ? null : await _placeService.FindTownByAddressAsync(address);
            if (written != null && written.town_id != given.town_id)
            {
                throw new BadRequestException($"地址「{address.Trim()}」不在{AreaName(given)}，請確認縣市、鄉鎮市區與地址是否一致");
            }

            string query = given == null ? address.Trim() : FullAddress(given, address);

            GeocodingService.AddressGeocodeResult geo = await CallTdxAsync(() => _geocoding.GeocodeAddressAsync(query), query);
            if (geo == null || !geo.is_door_level)
            {
                throw new BadRequestException($"地址「{query}」轉不到座標（對不到門牌），請確認地址，或地址與座標（lat、lng）兩個都填");
            }

            TownRef matched = await _placeService.FindTownByAddressAsync(geo.matched_address);
            if (given != null && matched != null && matched.town_id != given.town_id)
            {
                throw new BadRequestException($"地址「{query}」不在{AreaName(given)}，請確認縣市、鄉鎮市區與地址是否一致");
            }

            TownRef townRef = given ?? matched
                ?? throw new BadRequestException("無法判斷店家所在的鄉鎮市區，請提供縣市與鄉鎮市區（store_city、store_town）");

            return Build(townRef, address, geo.lat, geo.lng);
        }

        /// <summary>
        /// 只有座標：用 TDX 找最近的門牌當地址，門牌要在 300 公尺內；座標維持商家填的（地址只是文字，位置以座標為準）。
        /// </summary>
        public async Task<MerchantPlaceLocation> FromCoordinatesAsync(string city, string town, double lat, double lng)
        {
            ValidateCoordinates(lat, lng, "lat、lng");

            GeocodingService.AddressGeocodeResult geo = await CallTdxAsync(() => _geocoding.ReverseGeocodeAddressAsync(lat, lng), $"{lat},{lng}");

            if (geo == null || Geo.DistanceMeters(lat, lng, geo.lat, geo.lng) > ReverseGeocodeMaxDistanceMeters)
            {
                throw new BadRequestException($"座標轉不到地址（{ReverseGeocodeMaxDistanceMeters:0} 公尺內沒有門牌），請地址與座標兩個都填");
            }

            TownRef matched = await _placeService.FindTownByAddressAsync(geo.matched_address)
                ?? throw new BadRequestException($"找不到「{geo.matched_address}」所在鄉鎮市區的資料，請確認縣市與鄉鎮市區");

            TownRef given = await FindGivenTownAsync(city, town);
            if (given != null && given.town_id != matched.town_id)
            {
                throw new BadRequestException($"座標不在{AreaName(given)}，請確認縣市、鄉鎮市區與座標是否一致");
            }

            return Build(matched, geo.matched_address, lat, lng);
        }

        /// <summary>商家有填縣市＋鄉鎮市區時，要對得到 Neo4j 的鄉鎮市區；沒填時回傳 null</summary>
        private async Task<TownRef> FindGivenTownAsync(string city, string town)
        {
            if (string.IsNullOrWhiteSpace(city) && string.IsNullOrWhiteSpace(town)) return null;

            if (string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(town))
            {
                throw new BadRequestException("縣市與鄉鎮市區（store_city、store_town）要一起填");
            }

            return await _placeService.FindTownAsync(city, town)
                ?? throw new BadRequestException($"找不到「{city}{town}」，請確認縣市與鄉鎮市區的名稱");
        }

        private async Task<GeocodingService.AddressGeocodeResult> CallTdxAsync(
            Func<Task<GeocodingService.AddressGeocodeResult>> call, string input)
        {
            try
            {
                return await call();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TDX 地址／座標轉換失敗：{Input}", input);
                throw new Exception("地址與座標轉換服務暫時無法使用，請稍後再試，或地址與座標兩個都填", ex);
            }
        }

        /// <summary>組出位置：縣市、鄉鎮市區用 Neo4j 的寫法，地址去掉開頭重複的縣市、鄉鎮市區</summary>
        private static MerchantPlaceLocation Build(TownRef townRef, string address, double lat, double lng)
        {
            (string city, string town) = SplitTownId(townRef);
            string rest = StripArea(city, town, address);

            return new MerchantPlaceLocation
            {
                store_city = city,
                store_town = town,
                store_address = rest,
                full_address = city + town + rest,
                lat = lat,
                lng = lng,
                town_id = townRef.town_id
            };
        }

        private static string FullAddress(TownRef townRef, string address)
        {
            (string city, string town) = SplitTownId(townRef);
            return city + town + StripArea(city, town, address);
        }

        private static string AreaName(TownRef townRef)
        {
            (string city, string town) = SplitTownId(townRef);
            return city + town;
        }

        /// <summary>Town.id 是「縣市_鄉鎮市區」</summary>
        private static (string city, string town) SplitTownId(TownRef townRef)
        {
            string[] parts = (townRef.town_id ?? "").Split('_');
            return parts.Length == 2 ? (parts[0], parts[1]) : (townRef.city_name, townRef.town_name);
        }

        /// <summary>地址本身已經寫了縣市或鄉鎮市區時去掉，避免組完整地址時重複</summary>
        private static string StripArea(string city, string town, string address)
        {
            string rest = (address ?? "").Trim();

            foreach (string prefix in new[] { city, town })
            {
                if (!string.IsNullOrEmpty(prefix) && SameTaiwanName(rest).StartsWith(SameTaiwanName(prefix)))
                {
                    rest = rest.Substring(prefix.Length).TrimStart();
                }
            }

            return rest;
        }

        /// <summary>比對地名時統一「台／臺」（字數不變，可以直接用來算 Substring 位置）</summary>
        private static string SameTaiwanName(string name) => (name ?? "").Replace('臺', '台');
    }
}
