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

            bool hasExistingPlace = !string.IsNullOrWhiteSpace(req.place_uid);
            bool hasNewPlace = req.new_place != null;

            if (hasExistingPlace == hasNewPlace)
            {
                throw new BadRequestException("請擇一提供 place_uid（選擇既有景點）或 new_place（建立新景點）");
            }

            // 自建景點：地址、座標只填一個時自動轉另一個（轉不了回 400），在建立任何資料前先算好
            MerchantPlaceLocation newPlaceLocation = null;
            if (hasNewPlace)
            {
                ValidateNewPlace(req.new_place);

                string address = string.IsNullOrWhiteSpace(req.store_address) ? req.new_place.address : req.store_address;
                newPlaceLocation = await _storeLocation.ResolveAsync(req.store_city, req.store_town, address, req.new_place.lat, req.new_place.lng);

                // 商家沒填的由轉出來的位置補上，店家資料與景點版本節點的地址才會跟座標一致
                req.store_city = FirstNonBlank(req.store_city, newPlaceLocation.store_city);
                req.store_town = FirstNonBlank(req.store_town, newPlaceLocation.store_town);
                req.store_address = FirstNonBlank(req.store_address, newPlaceLocation.store_address);
                req.new_place.address = FirstNonBlank(req.new_place.address, req.store_address);
            }

            string storeUid;
            if (hasExistingPlace)
            {
                PlaceCurrentInfo existing = await _placeService.GetCurrentVersionAsync(req.place_uid);
                if (existing == null)
                {
                    throw new NotFoundException($"找不到 uid={req.place_uid} 的景點，請重新搜尋");
                }
                storeUid = req.place_uid;
            }
            else
            {
                storeUid = await _placeService.CreateMerchantPlaceAsync(req.new_place, newPlaceLocation, req.store_name);
            }

            var hashTool = new sha256Hash();
            string passwordHash = hashTool.getSha256(req.auth_pswd, _appSettings.hash_key);

            int auId, sId;
            try
            {
                (auId, sId) = _dao.RegisterMerchant(req, passwordHash, storeUid, hasNewPlace ? req.new_place : null);
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
        /// 避免經緯度填反；分類統一成 place_type 使用的寫法。
        /// </summary>
        private static void ValidateNewPlace(MerchantNewPlace place)
        {
            StoreLocationService.ValidateCoordinates(place.lat, place.lng, "new_place.lat、new_place.lng");

            if (string.IsNullOrWhiteSpace(place.category))
            {
                place.category = DefaultNewPlaceCategory;
                return;
            }

            string category = NewPlaceCategories.FirstOrDefault(c => string.Equals(c, place.category.Trim(), System.StringComparison.OrdinalIgnoreCase));
            place.category = category ?? throw new BadRequestException("new_place.category 只能是 Attraction、Restaurant、Hotel 或 Event");
        }

        private static string FirstNonBlank(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
        #endregion

        #region 商家資料
        public MerchantDetailResponse GetProfile(int sId)
        {
            MerchantDetailResponse merchant = _dao.GetById(sId);
            if (merchant == null) throw new NotFoundException($"找不到 s_id={sId} 的商家資料");
            return merchant;
        }

        /// <summary>
        /// 更新店家資料（只更新有帶值的欄位），並在 Neo4j 建立新的版本節點，讓 QR Code 掃描回傳的商家資訊跟著更新。
        /// 版本節點會寫入完整欄位，所以用更新後的店家資料組內容；電話、網站、營業時間沿用目前版本的值。
        /// 商家自建景點另外同步 MySQL 的 place_type 名稱（題型抽選與任務生成用）；行程規劃、地圖的名稱與座標直接查 Neo4j。
        /// 商家自建景點改了地址或重新選點時，景點重新掛鄉鎮市區並更新位置：
        /// 地址、座標都有改就照填的；只改地址會重轉座標；只帶座標會重轉地址（連同縣市、鄉鎮市區寫回店家資料）。
        /// 新位置在寫入任何資料前就先算好，轉不了時回 400，不會只改到一半。
        /// </summary>
        public async Task UpdateProfileAsync(int sId, MerchantUpdateRequest req)
        {
            if (req.store_name != null && string.IsNullOrWhiteSpace(req.store_name))
            {
                throw new BadRequestException("店家名稱不可為空白");
            }

            StoreLocationService.ValidateCoordinates(req.lat, req.lng, "lat、lng");

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
            }

            var fields = new MerchantPlaceFields
            {
                name = store.store_name,
                address = store.store_address,
                description = store.store_dec,
                phone = previous?.GetValueOrDefault("phone")?.ToString(),
                website = previous?.GetValueOrDefault("website")?.ToString(),
                opening_hours = previous?.GetValueOrDefault("opening_hours")?.ToString()
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

        #region 刪除商家帳號
        /// <summary>
        /// 商家整體刪除：MySQL 端在單一 Transaction 內完成（見 MerchantDao.DeleteCascade）；
        /// Neo4j 沒有跨資料庫交易保護，因此放在 MySQL commit 成功之後才執行，
        /// 失敗只記 log，不影響已經完成的 MySQL 刪除結果（此階段尚未做兩階段提交）。
        /// 刪除前先從 Neo4j 判斷是不是商家自建景點：是的話該景點不再排入行程（Neo4j 節點標成已刪除，座標保留給舊劇本）。
        /// </summary>
        public async Task DeleteAccountAsync(int sId)
        {
            MerchantDetailResponse store = _dao.GetById(sId);
            if (store == null) throw new NotFoundException($"找不到 s_id={sId} 的商家資料");

            bool selfBuiltPlace = false;
            if (!string.IsNullOrWhiteSpace(store.store_uid))
            {
                try
                {
                    selfBuiltPlace = IsSelfBuiltPlace(await _placeService.GetCurrentVersionAsync(store.store_uid));
                }
                catch (System.Exception ex)
                {
                    _logger.LogWarning(ex, "無法從 Neo4j 判斷商家 s_id={SId} 的景點是否為自建景點，視為既有景點處理", sId);
                }
            }

            string storeUid = _dao.DeleteCascade(sId, selfBuiltPlace);

            if (!string.IsNullOrWhiteSpace(storeUid))
            {
                try
                {
                    await _placeService.DeleteMerchantVersionAsync(storeUid);
                }
                catch (System.Exception ex)
                {
                    _logger.LogError(ex, "商家 s_id={SId} 的 MySQL 資料已刪除，但清理 Neo4j uid={Uid} 失敗", sId, storeUid);
                }
            }
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
