// 檔案路徑：System\Services\Merchant\MerchantService.cs
// 商家帳號、店家資料與商家影音；原 MerchantAccountService 的功能也併入這裡。
// 商家身分一律由 Controller 從 JWT（au_id / s_id Claim）取得後傳入。
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.Services.Neo4j;
using backend.utils;
using backend.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Services
{
    public class MerchantService
    {
        private readonly MerchantDao _dao;
        private readonly PlaceVersionChainService _placeService;
        private readonly StoreLocationService _storeLocation;
        private readonly AppSettings _appSettings;
        private readonly ILogger<MerchantService> _logger;

        public MerchantService(
            MerchantDao dao,
            PlaceVersionChainService placeService,
            StoreLocationService storeLocation,
            IOptions<AppSettings> appSettings,
            ILogger<MerchantService> logger)
        {
            _dao = dao;
            _placeService = placeService;
            _storeLocation = storeLocation;
            _appSettings = appSettings.Value;
            _logger = logger;
        }

        #region 商家註冊
        public async Task<MerchantRegisterResponse> RegisterAsync(MerchantRegisterRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.auth_email) || string.IsNullOrWhiteSpace(req.auth_pswd))
            {
                throw new BadRequestException("請提供 Email 與密碼");
            }

            // 沒帶 place_uid 就是選了「都沒有，我要建立新的」
            bool hasNewPlace = string.IsNullOrWhiteSpace(req.place_uid);

            // 自建景點：地址、座標只填一個時自動轉另一個（轉不了回 400），在建立任何資料前先算好
            MerchantPlaceLocation newPlaceLocation = null;
            if (hasNewPlace)
            {
                // 新景點的名稱沿用店名，沒有店名 Neo4j 的景點就沒有名字
                if (string.IsNullOrWhiteSpace(req.store_name))
                {
                    throw new BadRequestException("建立新景點時請填寫店名（store_name）");
                }

                ValidateNewPlace(req);

                newPlaceLocation = await _storeLocation.ResolveAsync(req.store_city, req.store_town, req.store_address, req.lat, req.lng);

                // 商家沒填的由轉出來的位置補上，店家資料與景點版本節點的地址才會跟座標一致
                req.store_city = FirstNonBlank(req.store_city, newPlaceLocation.store_city);
                req.store_town = FirstNonBlank(req.store_town, newPlaceLocation.store_town);
                req.store_address = FirstNonBlank(req.store_address, newPlaceLocation.store_address);
            }

            string storeUid;
            if (hasNewPlace)
            {
                // 景點的名稱、地址、介紹跟店家資料同一份（之後更新商家資料也是用 store_* 寫入版本鏈）
                var fields = new MerchantPlaceFields
                {
                    name = req.store_name,
                    address = req.store_address,
                    description = req.store_dec,
                    phone = req.phone,
                    website = req.website
                };
                storeUid = await _placeService.CreateMerchantPlaceAsync(fields, req.operating_hours, newPlaceLocation, req.store_name);
            }
            else
            {
                PlaceCurrentInfo existing = await _placeService.GetCurrentVersionAsync(req.place_uid);
                if (existing == null)
                {
                    throw new NotFoundException($"找不到 uid={req.place_uid} 的景點，請重新搜尋");
                }
                storeUid = req.place_uid;
            }

            var hashTool = new sha256Hash();
            string passwordHash = hashTool.getSha256(req.auth_pswd, _appSettings.hash_key);

            int auId, sId;
            try
            {
                (auId, sId) = _dao.RegisterMerchant(req, passwordHash, storeUid, hasNewPlace);
            }
            catch when (hasNewPlace)
            {
                // Neo4j 沒有跟 MySQL 同一個交易：MySQL 寫入失敗（例如 Email 重複）時，把剛建立的新景點刪掉，不留孤兒節點
                try
                {
                    await _placeService.DeleteUnusedMerchantPlaceAsync(storeUid);
                }
                catch (System.Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "商家註冊失敗，清理 Neo4j 新景點 uid={Uid} 也失敗", storeUid);
                }
                throw;
            }

            return new MerchantRegisterResponse { au_id = auId, s_id = sId, store_uid = storeUid };
        }

        // 商家自建景點可選的分類（對應 place_type.place_category），沒帶時預設 Restaurant
        private static readonly string[] NewPlaceCategories = { "Attraction", "Restaurant", "Hotel", "Event" };
        private const string DefaultNewPlaceCategory = "Restaurant";

        /// <summary>
        /// 自建景點的座標選填（地址、座標至少填一個，見 StoreLocationService），有帶時要落在臺灣（含離島）範圍內，
        /// 避免經緯度填反；營業時間整理成 Neo4j 的格式；分類統一成 place_type 使用的寫法。
        /// </summary>
        private static void ValidateNewPlace(MerchantRegisterRequest req)
        {
            StoreLocationService.ValidateCoordinates(req.lat, req.lng, "lat、lng");
            req.operating_hours = OperatingHoursRules.Normalize(req.operating_hours);

            if (string.IsNullOrWhiteSpace(req.category))
            {
                req.category = DefaultNewPlaceCategory;
                return;
            }

            string category = NewPlaceCategories.FirstOrDefault(c => string.Equals(c, req.category.Trim(), System.StringComparison.OrdinalIgnoreCase));
            req.category = category ?? throw new BadRequestException("category 只能是 Attraction、Restaurant、Hotel 或 Event");
        }

        private static string FirstNonBlank(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
        #endregion

        #region 商家資料
        /// <summary>店家資料（MySQL）加上 Neo4j 景點目前的營業時間，給編輯畫面帶入。</summary>
        public async Task<MerchantProfileResponse> GetProfileAsync(int sId)
        {
            MerchantDetailResponse merchant = _dao.GetById(sId)
                ?? throw new NotFoundException($"找不到 s_id={sId} 的商家資料");

            PlaceCurrentInfo place = string.IsNullOrWhiteSpace(merchant.store_uid)
                ? null
                : await _placeService.GetCurrentVersionAsync(merchant.store_uid);

            return new MerchantProfileResponse
            {
                s_id = merchant.s_id,
                au_id = merchant.au_id,
                store_name = merchant.store_name,
                store_dec = merchant.store_dec,
                store_address = merchant.store_address,
                store_city = merchant.store_city,
                store_town = merchant.store_town,
                store_uid = merchant.store_uid,
                auth_name = merchant.auth_name,
                auth_email = merchant.auth_email,
                is_active = merchant.is_active,
                is_self_built_place = IsSelfBuiltPlace(place),
                operating_hours = place?.operating_hours ?? new List<PlaceOperatingHourItem>()
            };
        }

        /// <summary>
        /// 更新店家資料（只更新有帶值的欄位），並在 Neo4j 建立新的版本節點，讓 QR Code 掃描回傳的商家資訊跟著更新。
        /// 版本節點會寫入完整欄位，所以用更新後的店家資料組內容；電話、網站沿用目前版本的值。
        /// 商家自建景點另外同步 MySQL 的 place_type 名稱（題型抽選與任務生成用）；行程規劃、地圖的名稱與座標直接查 Neo4j。
        /// 商家自建景點改了地址或重新選點時，景點重新掛鄉鎮市區並更新位置：
        /// 地址、座標都有改就照填的；只改地址會重轉座標；只帶座標會重轉地址（連同縣市、鄉鎮市區寫回店家資料）。
        /// 商家自建景點帶了營業時間就整份取代（不在版本鏈，直接換掉身分節點底下的 (:OperatingHours)）。
        /// 新位置與營業時間在寫入任何資料前就先檢查好，不對時回 400，不會只改到一半。
        /// </summary>
        public async Task UpdateProfileAsync(int sId, MerchantUpdateRequest req)
        {
            if (req.store_name != null && string.IsNullOrWhiteSpace(req.store_name))
            {
                throw new BadRequestException("店家名稱不可為空白");
            }

            StoreLocationService.ValidateCoordinates(req.lat, req.lng, "lat、lng");

            // null 代表不改；空陣列代表清空
            List<PlaceOperatingHourItem> operatingHours = req.operating_hours == null
                ? null
                : OperatingHoursRules.Normalize(req.operating_hours);

            MerchantDetailResponse before = _dao.GetById(sId)
                ?? throw new NotFoundException($"找不到 s_id={sId} 的商家資料");

            PlaceCurrentInfo current = string.IsNullOrWhiteSpace(before.store_uid)
                ? null
                : await _placeService.GetCurrentVersionAsync(before.store_uid);

            MerchantPlaceLocation newLocation = null;
            if (IsSelfBuiltPlace(current))
            {
                if (AddressChanged(before, req))
                {
                    newLocation = await _storeLocation.ResolveAsync(
                        req.store_city ?? before.store_city,
                        req.store_town ?? before.store_town,
                        req.store_address ?? before.store_address,
                        req.lat,
                        req.lng);
                }
                else if (req.lat.HasValue)
                {
                    // 只重新選點：地址跟著新座標重轉
                    newLocation = await _storeLocation.ResolveAsync(null, null, null, req.lat, req.lng);
                    req.store_city = newLocation.store_city;
                    req.store_town = newLocation.store_town;
                    req.store_address = newLocation.store_address;
                }
            }

            if (!_dao.Update(sId, req))
            {
                throw new NotFoundException($"找不到 s_id={sId} 的商家資料");
            }

            MerchantDetailResponse store = _dao.GetById(sId);
            if (string.IsNullOrWhiteSpace(store?.store_uid))
            {
                return;
            }

            Dictionary<string, object> previous = current?.merchant_override;

            if (IsSelfBuiltPlace(current))
            {
                _dao.SyncSelfBuiltPlace(store.store_uid, store.store_name);

                if (newLocation != null)
                {
                    await _placeService.UpdateMerchantPlaceLocationAsync(store.store_uid, newLocation);
                }

                if (operatingHours != null)
                {
                    await _placeService.ReplaceMerchantOperatingHoursAsync(store.store_uid, operatingHours);
                }
            }

            var fields = new MerchantPlaceFields
            {
                name = store.store_name,
                address = store.store_address,
                description = store.store_dec,
                phone = previous?.GetValueOrDefault("phone")?.ToString(),
                website = previous?.GetValueOrDefault("website")?.ToString()
            };
            await _placeService.CreateNewVersionAsync(store.store_uid, fields, "merchant", sId.ToString());
        }

        /// <summary>縣市／鄉鎮市區／地址是否跟原本不同（前端每次都帶全部欄位時，沒改的不算）</summary>
        private static bool AddressChanged(MerchantDetailResponse before, MerchantUpdateRequest req)
        {
            return (req.store_city != null && req.store_city != before.store_city)
                || (req.store_town != null && req.store_town != before.store_town)
                || (req.store_address != null && req.store_address != before.store_address);
        }

        /// <summary>商家列表（管理員用）。</summary>
        public MerchantListResponse List(MerchantListQuery query)
        {
            (var items, int total) = _dao.List(query);
            return new MerchantListResponse
            {
                items = items,
                total = total,
                page = query.page,
                page_size = query.page_size
            };
        }
        #endregion

        /// <summary>商家自建的景點在 Neo4j 身分節點帶 :MerchantPlace 標籤（政府開放資料的景點沒有）</summary>
        private static bool IsSelfBuiltPlace(PlaceCurrentInfo place)
        {
            return place?.identity_labels?.Contains("MerchantPlace") == true;
        }

        #region 已生成的影音檔案
        public List<MerchantFileItem> GetMerchantFiles(int auId)
        {
            return _dao.GetMerchantFiles(auId);
        }
        #endregion
    }
}
