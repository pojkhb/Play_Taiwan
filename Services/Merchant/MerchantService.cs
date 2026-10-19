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
        private readonly PlaceLookupService _placeLookup;
        private readonly StoreLocationService _storeLocation;
        private readonly EmbeddingService _embedding;
        private readonly AppSettings _appSettings;
        private readonly ILogger<MerchantService> _logger;
        private readonly AuthDao _authDao;

        public MerchantService(
            MerchantDao dao,
            AuthDao authDao,
            PlaceVersionChainService placeService,
            PlaceLookupService placeLookup,
            StoreLocationService storeLocation,
            EmbeddingService embedding,
            IOptions<AppSettings> appSettings,
            ILogger<MerchantService> logger)
        {
            _dao = dao;
            _authDao = authDao;
            _placeService = placeService;
            _placeLookup = placeLookup;
            _storeLocation = storeLocation;
            _embedding = embedding;
            _appSettings = appSettings.Value;
            _logger = logger;
        }

        #region 商家註冊
        public async Task<MerchantRegisterResponse> RegisterAsync(MerchantRegisterRequest req)
        {
            if (req == null) throw new BadRequestException("請填寫註冊資料");

            // 帳號欄位跟遊客註冊同一套規則（AccountRules）；名稱、Email 重複在建立 Neo4j 景點之前就擋下來（409）
            req.auth_name = AccountRules.Name(req.auth_name);
            req.auth_email = AccountRules.Email(req.auth_email);
            AccountRules.Password(req.auth_pswd);
            if (_authDao.EmailTaken(req.auth_email)) throw new ConflictException("此 Email 已被註冊過");
            if (_authDao.NameTaken(req.auth_name)) throw new ConflictException("此帳號名稱已被使用，請換一個");

            // 註冊時還沒有照片可以清，空字串跟沒帶一樣
            string imageUrl = NormalizeImageUrl(req.image_url);
            if (imageUrl == "") imageUrl = null;

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
                req.sub_category = await ResolveSubCategoryAsync(req.category, req.sub_category);

                newPlaceLocation = await _storeLocation.ResolveAsync(req.store_city, req.store_town, req.store_address, req.lat, req.lng);

                // 商家沒填的由轉出來的位置補上，店家資料與景點版本節點的地址才會跟座標一致
                req.store_city = FirstNonBlank(req.store_city, newPlaceLocation.store_city);
                req.store_town = FirstNonBlank(req.store_town, newPlaceLocation.store_town);
                req.store_address = FirstNonBlank(req.store_address, newPlaceLocation.store_address);
            }

            string storeUid;
            PlaceCurrentInfo existing = null;
            if (hasNewPlace)
            {
                // 景點的名稱、地址、介紹跟店家資料同一份（之後更新商家資料也是用 store_* 寫入版本鏈）
                var fields = new MerchantPlaceFields
                {
                    name = req.store_name,
                    address = req.store_address,
                    description = req.store_dec,
                    phone = req.phone,
                    website = req.website,
                    image_url = imageUrl
                };
                // 介紹轉成語意向量，跟政府資料的景點一樣寫在身分節點；embedding 服務沒開時先不寫，之後商家修改資料時補上
                List<double> embedding = await _embedding.EmbedAsync(EmbeddingText(req.store_name, req.store_dec));
                storeUid = await _placeService.CreateMerchantPlaceAsync(fields, req.category, req.sub_category, embedding,
                    req.operating_hours, newPlaceLocation, req.store_name);
            }
            else
            {
                existing = await _placeService.GetCurrentVersionAsync(req.place_uid);
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

            // 選既有景點又帶了照片網址：補一顆版本節點存照片（新景點的照片已經寫在第一顆版本節點）
            if (!hasNewPlace && imageUrl != null)
            {
                Dictionary<string, object> previous = existing.merchant_override;
                var fields = new MerchantPlaceFields
                {
                    name = req.store_name,
                    address = req.store_address,
                    description = req.store_dec,
                    phone = previous?.GetValueOrDefault("phone")?.ToString(),
                    website = previous?.GetValueOrDefault("website")?.ToString(),
                    image_url = imageUrl
                };
                await _placeService.CreateNewVersionAsync(storeUid, fields, "merchant", sId.ToString());
            }

            return new MerchantRegisterResponse { au_id = auId, s_id = sId, store_uid = storeUid };
        }

        // 商家自建景點沒帶分類時預設 Restaurant（可選的分類見 PlaceVersionChainService.MerchantPlaceCategories）
        private const string DefaultNewPlaceCategory = "Restaurant";

        /// <summary>
        /// 自建景點的座標選填（地址、座標至少填一個，見 StoreLocationService），有帶時要落在臺灣（含離島）範圍內，
        /// 避免經緯度填反；營業時間整理成 Neo4j 的格式；分類統一成 place_type 使用的寫法。
        /// </summary>
        private static void ValidateNewPlace(MerchantRegisterRequest req)
        {
            StoreLocationService.ValidateCoordinates(req.lat, req.lng, "lat、lng");
            req.operating_hours = OperatingHoursRules.Normalize(req.operating_hours);
            req.category = string.IsNullOrWhiteSpace(req.category) ? DefaultNewPlaceCategory : NormalizeCategory(req.category);
        }

        /// <summary>分類統一成類型標籤的寫法（不分大小寫），不是 Attraction、Restaurant、Hotel、Event 時回 400</summary>
        private static string NormalizeCategory(string category)
        {
            return PlaceVersionChainService.MerchantPlaceCategories.Keys
                .FirstOrDefault(c => string.Equals(c, category.Trim(), System.StringComparison.OrdinalIgnoreCase))
                ?? throw new BadRequestException("category 只能是 Attraction、Restaurant、Hotel 或 Event");
        }

        /// <summary>
        /// 細分類（Neo4j 的 Category）：沒帶時用 category 的預設細分類（Restaurant、Attraction 為「其他」，Event 為「一般活動」，Hotel 不掛）；
        /// 有帶時要是 category 底下的細分類，不是的話回 400。category 要先經過 NormalizeCategory 整理。
        /// </summary>
        private async Task<string> ResolveSubCategoryAsync(string category, string subCategory)
        {
            if (string.IsNullOrWhiteSpace(subCategory))
            {
                return PlaceVersionChainService.MerchantPlaceCategories[category].defaultSubCategory;
            }

            string trimmed = subCategory.Trim();
            MerchantPlaceCategoryItem item = (await _placeService.GetMerchantPlaceCategoriesAsync())
                .First(c => c.category == category);
            if (!item.sub_categories.Contains(trimmed))
            {
                throw new BadRequestException(
                    $"sub_category「{trimmed}」不是{item.name}（{item.category}）的細分類，可選的值請查 GET api/merchant/register/categories");
            }
            return trimmed;
        }

        /// <summary>
        /// 商家自建景點修改資料時要換成的分類與細分類；都沒帶、或分類沒變也沒指定細分類時回 null（不用改）。
        /// 只帶細分類時沿用目前的分類；換了分類沒帶細分類時用新分類的預設細分類。
        /// </summary>
        private async Task<(string category, string subCategory)?> ResolveCategoryChangeAsync(PlaceCurrentInfo current, MerchantUpdateRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.category) && string.IsNullOrWhiteSpace(req.sub_category)) return null;

            string currentCategory = PlaceCategory(current);
            string category = string.IsNullOrWhiteSpace(req.category)
                ? currentCategory ?? _dao.GetPlaceCategory(current.uid) ?? DefaultNewPlaceCategory
                : NormalizeCategory(req.category);
            if (category == currentCategory && string.IsNullOrWhiteSpace(req.sub_category)) return null;

            return (category, await ResolveSubCategoryAsync(category, req.sub_category));
        }

        /// <summary>景點身分節點的類型標籤（Attraction / Restaurant / Hotel / Event）；沒有時（舊資料或已停用）為 null</summary>
        private static string PlaceCategory(PlaceCurrentInfo place) =>
            place?.identity_labels?.FirstOrDefault(PlaceVersionChainService.MerchantPlaceCategories.ContainsKey);

        /// <summary>
        /// 算語意向量的文字：跟資料清洗一樣用景點介紹原文；沒填介紹時用店名，景點才不會在語意搜尋中完全找不到。
        /// </summary>
        private static string EmbeddingText(string name, string description) => string.IsNullOrWhiteSpace(description) ? name : description;

        private static string FirstNonBlank(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

        /// <summary>
        /// 店家照片網址（image_url）去掉前後空白，要是 http／https 的完整網址，不是的話回 400。
        /// null 原樣回傳（代表不改），空白回傳空字串（代表清掉）。
        /// </summary>
        private static string NormalizeImageUrl(string url)
        {
            if (url == null) return null;

            string trimmed = url.Trim();
            if (trimmed.Length == 0) return "";

            if (!System.Uri.TryCreate(trimmed, System.UriKind.Absolute, out System.Uri uri)
                || (uri.Scheme != System.Uri.UriSchemeHttp && uri.Scheme != System.Uri.UriSchemeHttps))
            {
                throw new BadRequestException("image_url 請填 http 或 https 開頭的完整照片網址");
            }
            return trimmed;
        }
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
                category = IsSelfBuiltPlace(place) ? PlaceCategory(place) : null,
                sub_category = IsSelfBuiltPlace(place) ? place.categories?.FirstOrDefault() : null,
                operating_hours = place?.operating_hours ?? new List<PlaceOperatingHourItem>(),
                image_url = CurrentImageUrl(place?.merchant_override)
            };
        }

        /// <summary>目前版本節點上商家填的店家照片網址，沒填時為 null</summary>
        private static string CurrentImageUrl(Dictionary<string, object> merchantOverride)
        {
            string url = Neo4jValueConverter.AsString(merchantOverride?.GetValueOrDefault("image_url"));
            return string.IsNullOrWhiteSpace(url) ? null : url;
        }

        /// <summary>
        /// 更新店家資料（只更新有帶值的欄位），並在 Neo4j 建立新的版本節點，讓 QR Code 掃描回傳的商家資訊跟著更新。
        /// 版本節點會寫入完整欄位，所以用更新後的店家資料組內容；電話、網站沿用目前版本的值，
        /// 店家照片網址（image_url）沒帶時也沿用，帶空字串代表清掉。
        /// 商家自建景點另外同步 MySQL 的 place_type 名稱（題型抽選與任務生成用）；行程規劃、地圖的名稱與座標直接查 Neo4j。
        /// 商家自建景點改了地址或重新選點時，景點重新掛鄉鎮市區並更新位置：
        /// 地址、座標都有改就照填的；只改地址會重轉座標；只帶座標會重轉地址（連同縣市、鄉鎮市區寫回店家資料）。
        /// 商家自建景點帶了營業時間就整份取代（不在版本鏈，直接換掉身分節點底下的 (:OperatingHours)）。
        /// 商家自建景點改了店名或介紹時，身分節點上的名稱、介紹與介紹的語意向量也跟著更新（見 SyncSelfBuiltPlaceTextAsync）。
        /// 商家自建景點帶了分類或細分類時，換掉 Neo4j 的類型標籤與細分類，MySQL place_type 的分類也一起改（見 ResolveCategoryChangeAsync）。
        /// 新位置、營業時間與分類在寫入任何資料前就先檢查好，不對時回 400，不會只改到一半。
        /// </summary>
        public async Task UpdateProfileAsync(int sId, MerchantUpdateRequest req)
        {
            if (req.store_name != null && string.IsNullOrWhiteSpace(req.store_name))
            {
                throw new BadRequestException("店家名稱不可為空白");
            }

            StoreLocationService.ValidateCoordinates(req.lat, req.lng, "lat、lng");

            // null 代表不改；空字串代表清掉
            string imageUrl = NormalizeImageUrl(req.image_url);

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
            (string category, string subCategory)? newCategory = null;
            if (IsSelfBuiltPlace(current))
            {
                newCategory = await ResolveCategoryChangeAsync(current, req);

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
                _dao.SyncSelfBuiltPlace(store.store_uid, store.store_name, newCategory?.category);

                if (newLocation != null)
                {
                    await _placeService.UpdateMerchantPlaceLocationAsync(store.store_uid, newLocation);
                }

                if (operatingHours != null)
                {
                    await _placeService.ReplaceMerchantOperatingHoursAsync(store.store_uid, operatingHours);
                }

                // 先換分類（活動的名稱、介紹屬性名稱不同），再同步名稱、介紹
                if (newCategory != null)
                {
                    await _placeService.UpdateMerchantPlaceCategoryAsync(store.store_uid, newCategory.Value.category, newCategory.Value.subCategory);
                }

                await SyncSelfBuiltPlaceTextAsync(current, store);
            }

            var fields = new MerchantPlaceFields
            {
                name = store.store_name,
                address = store.store_address,
                description = store.store_dec,
                phone = previous?.GetValueOrDefault("phone")?.ToString(),
                website = previous?.GetValueOrDefault("website")?.ToString(),
                image_url = imageUrl == null ? CurrentImageUrl(previous) : (imageUrl == "" ? null : imageUrl)
            };
            await _placeService.CreateNewVersionAsync(store.store_uid, fields, "merchant", sId.ToString());
        }

        /// <summary>
        /// 自建景點身分節點上的名稱、介紹（跟政府資料一樣放在節點上）同步成店家資料，並重新算介紹的語意向量。
        /// 名稱、介紹都沒改而且已經有向量時不用動；還沒有向量（例如註冊時 embedding 服務沒開）時就算沒改也補算。
        /// current 是更新店家資料前查的，身分節點這時還是舊的名稱、介紹。
        /// </summary>
        private async Task SyncSelfBuiltPlaceTextAsync(PlaceCurrentInfo current, MerchantDetailResponse store)
        {
            bool textChanged = !SameText(current.gov_name, store.store_name) || !SameText(current.gov_description, store.store_dec);
            if (!textChanged && await _placeService.HasDescriptionEmbeddingAsync(store.store_uid)) return;

            List<double> embedding = await _embedding.EmbedAsync(EmbeddingText(store.store_name, store.store_dec));
            await _placeService.UpdateMerchantPlaceTextAsync(store.store_uid, store.store_name, store.store_dec, embedding);
        }

        /// <summary>null 跟空字串視為相同</summary>
        private static bool SameText(string a, string b) => (a ?? "") == (b ?? "");

        /// <summary>縣市／鄉鎮市區／地址是否跟原本不同（前端每次都帶全部欄位時，沒改的不算）</summary>
        private static bool AddressChanged(MerchantDetailResponse before, MerchantUpdateRequest req)
        {
            return (req.store_city != null && req.store_city != before.store_city)
                || (req.store_town != null && req.store_town != before.store_town)
                || (req.store_address != null && req.store_address != before.store_address);
        }

        /// <summary>商家列表（管理員用），每間店附上 Neo4j 景點目前的營業時間（這一頁的店一次查）。</summary>
        public async Task<MerchantListResponse> ListAsync(MerchantListQuery query)
        {
            (var items, int total) = _dao.List(query);

            Dictionary<string, List<PlaceOperatingHourItem>> hours = await _placeLookup.GetOperatingHoursAsync(items.Select(i => i.store_uid));
            foreach (MerchantListItem item in items)
            {
                item.operating_hours = (item.store_uid != null ? hours.GetValueOrDefault(item.store_uid) : null)
                    ?? new List<PlaceOperatingHourItem>();
            }

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
