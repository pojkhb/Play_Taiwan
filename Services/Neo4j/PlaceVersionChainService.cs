using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend.ViewModels;

namespace backend.Services.Neo4j
{
    /// <summary>
    /// 商家 ↔ 景點的「版本鏈（Version-Chain）」業務邏輯。只依賴 <see cref="INeo4jGatewayService"/>，
    /// 不管目前是本地測試 Driver 還是正式對外 API。
    ///
    /// 設計：政府開放資料的原始節點（identity node，帶 uid）永遠不被修改；商家的補充/覆蓋資訊
    /// 寫在新增的 :Current 版本節點上，透過 [:HAS_VERSION] 關聯回原始節點。商家全新建立的景點
    /// 也走同一套結構，身分節點另外加 :MerchantPlace 標籤以便和政府資料區分。
    /// </summary>
    public class PlaceVersionChainService
    {
        private readonly INeo4jGatewayService _gateway;

        public PlaceVersionChainService(INeo4jGatewayService gateway)
        {
            _gateway = gateway;
        }

        /// <summary>依店名/地址模糊比對既有景點，供商家註冊時挑選要綁定的既有景點。</summary>
        public async Task<List<PlaceSearchResultItem>> SearchPlacesAsync(string keyword, int limit = 20)
        {
            const string query = @"
                MATCH (p:Place)
                WHERE coalesce(p.name, p.EventName, '') CONTAINS $keyword
                   OR coalesce(p.address, p.Address, '') CONTAINS $keyword
                RETURN p.uid AS uid,
                       coalesce(p.name, p.EventName) AS name,
                       coalesce(p.address, p.Address) AS address,
                       labels(p) AS labels
                LIMIT $limit
            ";

            var rows = await _gateway.ExecuteCypherAsync(query, new { keyword, limit });

            return rows.Select(row => new PlaceSearchResultItem
            {
                uid = Neo4jValueConverter.AsString(row.GetValueOrDefault("uid")),
                name = Neo4jValueConverter.AsString(row.GetValueOrDefault("name")),
                address = Neo4jValueConverter.AsString(row.GetValueOrDefault("address")),
                type = Neo4jValueConverter.AsStringList(row.GetValueOrDefault("labels"))
                    .FirstOrDefault(label => label != "Place") ?? "Place"
            }).ToList();
        }

        /// <summary>
        /// 查詢景點目前生效資料：完整的政府原始資料（含分類/縣市鄉鎮/圖片/營業時間/旅宿類型）
        /// + 版本鏈覆蓋後的最新內容（若有商家補充過）。政府資料的欄位在 Attraction/Event/Hotel/
        /// Restaurant 之間命名不一致（例如 name vs EventName），用 coalesce 統一成同一組回應欄位。
        /// </summary>
        public async Task<PlaceCurrentInfo> GetCurrentVersionAsync(string uid)
        {
            const string query = @"
                MATCH (a {uid: $uid})
                OPTIONAL MATCH (a)-[:HAS_VERSION]->(v:Current)

                CALL {
                    WITH a
                    OPTIONAL MATCH (a)-[:HAS_CATEGORY]->(cat:Category)
                    RETURN collect(DISTINCT cat.name) AS categories
                }
                CALL {
                    WITH a
                    OPTIONAL MATCH (a)-[:LOCATED_IN_CITY]->(city:City)
                    RETURN city.name AS cityName
                }
                CALL {
                    WITH a
                    OPTIONAL MATCH (a)-[:LOCATED_IN_TOWN]->(town:Town)
                    RETURN town.name AS townName
                }
                CALL {
                    WITH a
                    OPTIONAL MATCH (a)-[:HAS_IMAGE]->(img:Image)
                    RETURN collect(DISTINCT {url: img.url, description: img.description}) AS images
                }
                CALL {
                    WITH a
                    OPTIONAL MATCH (a)-[:HAS_OPERATING_HOURS]->(oh:OperatingHours)
                    RETURN collect(DISTINCT {day_of_week: oh.dayOfWeek, open_time: oh.openTime, close_time: oh.closeTime}) AS operatingHours
                }
                CALL {
                    WITH a
                    OPTIONAL MATCH (a)-[:HAS_CLASS]->(hc:HotelClass)
                    RETURN collect(DISTINCT hc.name) AS hotelClasses
                }

                RETURN a.uid AS uid,
                       labels(a) AS identityLabels,
                       coalesce(a.name, a.EventName) AS govName,
                       coalesce(a.description, a.Description) AS govDescription,
                       coalesce(a.address, a.Address) AS govAddress,
                       coalesce(a.lat, a.PositionLat) AS govLat,
                       coalesce(a.lon, a.PositionLon) AS govLon,
                       coalesce(a.business_status, a.EventStatus) AS govStatus,
                       a.phone AS govPhone,
                       coalesce(a.website, a.WebsiteURL) AS govWebsite,
                       a.ticket_info AS govTicketInfo,
                       coalesce(a.travel_info, a.TrafficInfo) AS govTravelInfo,
                       categories,
                       cityName,
                       townName,
                       images,
                       operatingHours,
                       hotelClasses,
                       v { .* } AS overrideData
            ";

            var rows = await _gateway.ExecuteCypherAsync(query, new { uid });
            var row = rows.FirstOrDefault();
            if (row == null) return null;

            return new PlaceCurrentInfo
            {
                uid = Neo4jValueConverter.AsString(row.GetValueOrDefault("uid")),
                identity_labels = Neo4jValueConverter.AsStringList(row.GetValueOrDefault("identityLabels")),
                gov_name = Neo4jValueConverter.AsString(row.GetValueOrDefault("govName")),
                gov_description = Neo4jValueConverter.AsString(row.GetValueOrDefault("govDescription")),
                gov_address = Neo4jValueConverter.AsString(row.GetValueOrDefault("govAddress")),
                gov_lat = Neo4jValueConverter.AsDouble(row.GetValueOrDefault("govLat")),
                gov_lon = Neo4jValueConverter.AsDouble(row.GetValueOrDefault("govLon")),
                gov_status = Neo4jValueConverter.AsString(row.GetValueOrDefault("govStatus")),
                gov_phone = Neo4jValueConverter.AsString(row.GetValueOrDefault("govPhone")),
                gov_website = Neo4jValueConverter.AsString(row.GetValueOrDefault("govWebsite")),
                gov_ticket_info = Neo4jValueConverter.AsString(row.GetValueOrDefault("govTicketInfo")),
                gov_travel_info = Neo4jValueConverter.AsString(row.GetValueOrDefault("govTravelInfo")),
                categories = Neo4jValueConverter.AsStringList(row.GetValueOrDefault("categories")),
                city = Neo4jValueConverter.AsString(row.GetValueOrDefault("cityName")),
                town = Neo4jValueConverter.AsString(row.GetValueOrDefault("townName")),
                images = ToImages(row.GetValueOrDefault("images")),
                operating_hours = ToOperatingHours(row.GetValueOrDefault("operatingHours")),
                hotel_classes = Neo4jValueConverter.AsStringList(row.GetValueOrDefault("hotelClasses")),
                merchant_override = Neo4jValueConverter.AsDictionary(row.GetValueOrDefault("overrideData"))
            };
        }

        private static List<PlaceImageItem> ToImages(object raw)
        {
            var result = new List<PlaceImageItem>();
            foreach (object item in Neo4jValueConverter.AsList(raw))
            {
                Dictionary<string, object> map = Neo4jValueConverter.AsDictionary(item);
                if (map == null) continue;
                string url = Neo4jValueConverter.AsString(map.GetValueOrDefault("url"));
                if (string.IsNullOrWhiteSpace(url)) continue;

                result.Add(new PlaceImageItem
                {
                    url = url,
                    description = Neo4jValueConverter.AsString(map.GetValueOrDefault("description"))
                });
            }
            return result;
        }

        /// <summary>
        /// Neo4j 收集回來的營業時段（{day_of_week, open_time, close_time} 清單）→ 回應格式（見 OperatingHoursRules.ForDisplay：
        /// 時間統一成 HH:mm、去掉重複、依星期與開始時間排好）。
        /// 店家資料、掃 QR Code、優惠券列表的 operating_hours 都用這裡轉，格式一致。
        /// </summary>
        internal static List<PlaceOperatingHourItem> ToOperatingHours(object raw)
        {
            var result = new List<PlaceOperatingHourItem>();
            foreach (object item in Neo4jValueConverter.AsList(raw))
            {
                Dictionary<string, object> map = Neo4jValueConverter.AsDictionary(item);
                if (map == null) continue;
                string dayOfWeek = Neo4jValueConverter.AsString(map.GetValueOrDefault("day_of_week"));
                if (string.IsNullOrWhiteSpace(dayOfWeek)) continue;

                result.Add(new PlaceOperatingHourItem
                {
                    day_of_week = dayOfWeek,
                    open_time = Neo4jValueConverter.AsString(map.GetValueOrDefault("open_time")),
                    close_time = Neo4jValueConverter.AsString(map.GetValueOrDefault("close_time"))
                });
            }
            return OperatingHoursRules.ForDisplay(result);
        }

        /// <summary>
        /// 幫指定 uid 的景點建立一顆新的 :Current 版本節點（把舊的 Current 轉成 Historical）。
        /// 商家綁定既有景點後、或之後修改商家資料時呼叫。
        /// </summary>
        public async Task CreateNewVersionAsync(string uid, MerchantPlaceFields fields, string source, string submittedBy)
        {
            const string query = @"
                MATCH (a {uid: $uid})
                OPTIONAL MATCH (a)-[:HAS_VERSION]->(old:Current)
                SET old:Historical, old.valid_to = datetime()
                REMOVE old:Current
                WITH a, old
                CREATE (new:Place:Version:Current {
                    version_id: randomUUID(),
                    version_no: coalesce(old.version_no, 0) + 1,
                    name: $name,
                    address: $address,
                    description: $description,
                    phone: $phone,
                    website: $website,
                    image_url: $imageUrl,
                    source: $source,
                    submitted_by: $submittedBy,
                    valid_from: datetime(),
                    valid_to: null
                })
                CREATE (a)-[:HAS_VERSION]->(new)
            ";

            await _gateway.ExecuteCypherAsync(query, new
            {
                uid,
                name = fields.name,
                address = fields.address,
                description = fields.description,
                phone = fields.phone,
                website = fields.website,
                imageUrl = fields.image_url,
                source,
                submittedBy
            });
        }

        /// <summary>
        /// 依縣市＋鄉鎮市區找 Neo4j 的 Town 節點（Town.id 為「縣市_鄉鎮市區」，Neo4j 用「台」，比對時統一台／臺），
        /// 找不到時回傳 null。
        /// </summary>
        public async Task<TownRef> FindTownAsync(string city, string town)
        {
            string key = TownKey(city, town);
            if (key == null) return null;

            var rows = await _gateway.ExecuteCypherAsync(@"
                MATCH (t:Town) WHERE replace(t.id, '臺', '台') = $key
                OPTIONAL MATCH (t)-[:PART_OF]->(c:City)
                RETURN t.id AS town_id, t.name AS town, c.name AS city
                LIMIT 1", new { key });

            return ToTownRef(rows.FirstOrDefault());
        }

        /// <summary>
        /// 依地址開頭的「縣市＋鄉鎮市區」找 Neo4j 的 Town 節點（例如「台中市西區安龍里英才路600號」→ 台中市_西區），
        /// 找不到時回傳 null。鄉鎮名稱有包含關係時（例如新市區）取最長的。
        /// </summary>
        public async Task<TownRef> FindTownByAddressAsync(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;

            var rows = await _gateway.ExecuteCypherAsync(@"
                MATCH (t:Town)
                WITH t, split(replace(t.id, '臺', '台'), '_') AS parts
                WHERE size(parts) = 2 AND $address STARTS WITH (parts[0] + parts[1])
                OPTIONAL MATCH (t)-[:PART_OF]->(c:City)
                RETURN t.id AS town_id, t.name AS town, c.name AS city, size(parts[1]) AS town_length
                ORDER BY town_length DESC
                LIMIT 1", new { address = Clean(address) });

            return ToTownRef(rows.FirstOrDefault());
        }

        private static TownRef ToTownRef(Dictionary<string, object> row)
        {
            if (row == null) return null;

            return new TownRef
            {
                town_id = Neo4jValueConverter.AsString(row.GetValueOrDefault("town_id")),
                town_name = Neo4jValueConverter.AsString(row.GetValueOrDefault("town")),
                city_name = Neo4jValueConverter.AsString(row.GetValueOrDefault("city"))
            };
        }

        private static string TownKey(string city, string town)
        {
            if (string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(town)) return null;
            return $"{Clean(city)}_{Clean(town)}";
        }

        /// <summary>比對地名用：去掉空白、統一成 Neo4j 的「台」</summary>
        private static string Clean(string s) => new string(s.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).Replace('臺', '台');

        /// <summary>
        /// 商家自建景點的分類（註冊的 category，也是 place_type.place_category）→ Neo4j 主分類名稱與預設細分類。
        /// 分類同時當作身分節點的類型標籤（:Attraction、:Restaurant…），跟政府資料一樣。
        /// 預設細分類用政府資料本來就有的「其他」「一般活動」；旅宿沒有通用的細分類，不掛。
        /// </summary>
        public static readonly IReadOnlyDictionary<string, (string mainCategory, string defaultSubCategory)> MerchantPlaceCategories =
            new Dictionary<string, (string, string)>
            {
                ["Attraction"] = ("景點", "其他"),
                ["Restaurant"] = ("餐廳", "其他"),
                ["Hotel"] = ("住宿", null),
                ["Event"] = ("活動", "一般活動")
            };

        /// <summary>
        /// 商家自建景點可選的分類與細分類（Neo4j 的 MainCategory → Category）。
        /// 細分類依掛的景點數排序（常用的在前），「其他」排最後。
        /// </summary>
        public async Task<List<MerchantPlaceCategoryItem>> GetMerchantPlaceCategoriesAsync()
        {
            var rows = await _gateway.ExecuteCypherAsync(@"
                MATCH (m:MainCategory)-[:HAS_SUBCATEGORY]->(c:Category)
                WHERE m.name IN $mainCategories
                WITH m, c ORDER BY c.name = '其他', COUNT { (c)<-[:HAS_CATEGORY]-() } DESC, c.name
                RETURN m.name AS main_category, collect(c.name) AS sub_categories",
                new { mainCategories = MerchantPlaceCategories.Values.Select(v => v.mainCategory).ToList() });

            Dictionary<string, List<string>> subCategories = rows.ToDictionary(
                row => Neo4jValueConverter.AsString(row.GetValueOrDefault("main_category")),
                row => Neo4jValueConverter.AsStringList(row.GetValueOrDefault("sub_categories")));

            return MerchantPlaceCategories.Select(kv => new MerchantPlaceCategoryItem
            {
                category = kv.Key,
                name = kv.Value.mainCategory,
                default_sub_category = kv.Value.defaultSubCategory,
                sub_categories = subCategories.GetValueOrDefault(kv.Value.mainCategory) ?? new List<string>()
            }).ToList();
        }

        // 身分節點上的名稱、介紹（變數 name、description），屬性名稱跟同類型的政府資料一樣（活動是 EventName / Description，其他是 name / description），
        // 接在已綁定 a 的查詢後面；值是 null 時拿掉該屬性
        private const string SetPlaceTextCypher = @"
                SET a += CASE WHEN a:Event THEN {EventName: name, Description: description} ELSE {name: name, description: description} END";

        // 套用分類（變數 category、subCategory）：換成 category 的類型標籤（null 時不加），名稱、介紹改用該類型的屬性名稱，
        // 細分類換成 subCategory（旅宿另外掛同名的旅宿等級；Neo4j 沒有這個細分類時不掛），接在已綁定 a 的查詢後面。
        // 四個類型標籤跟 MerchantPlaceCategories 一致
        private const string ApplyCategoryCypher = @"
                WITH a, category, subCategory, coalesce(a.name, a.EventName) AS name, coalesce(a.description, a.Description) AS description
                REMOVE a:Attraction:Restaurant:Hotel:Event, a.name, a.EventName, a.description, a.Description
                SET a:$(CASE WHEN category IS NULL THEN [] ELSE [category] END)" + SetPlaceTextCypher + @"
                WITH a, subCategory
                OPTIONAL MATCH (a)-[old:HAS_CATEGORY|HAS_CLASS]->()
                DELETE old
                WITH DISTINCT a, subCategory
                OPTIONAL MATCH (c:Category {name: subCategory})
                OPTIONAL MATCH (hc:HotelClass {name: subCategory}) WHERE a:Hotel
                FOREACH (_ IN CASE WHEN c IS NULL THEN [] ELSE [1] END | CREATE (a)-[:HAS_CATEGORY]->(c))
                FOREACH (_ IN CASE WHEN hc IS NULL THEN [] ELSE [1] END | CREATE (a)-[:HAS_CLASS]->(hc))";

        // 把自建景點掛到指定的鄉鎮市區與所屬縣市（先拆掉舊的行政區關聯），接在已綁定 a 的查詢後面
        private const string LinkTownCypher = @"
                WITH a
                OPTIONAL MATCH (a)-[old:LOCATED_IN_TOWN|LOCATED_IN_CITY]->()
                DELETE old
                WITH DISTINCT a
                OPTIONAL MATCH (t:Town {id: $townId})
                OPTIONAL MATCH (t)-[:PART_OF]->(c:City)
                FOREACH (_ IN CASE WHEN t IS NULL THEN [] ELSE [1] END | CREATE (a)-[:LOCATED_IN_TOWN]->(t))
                FOREACH (_ IN CASE WHEN c IS NULL THEN [] ELSE [1] END | CREATE (a)-[:LOCATED_IN_CITY]->(c))";

        /// <summary>
        /// 商家註冊時選「都沒有，我要建立新的」景點：建立一顆全新身分節點（:Place:MerchantPlace，帶座標、完整地址，
        /// 並掛上鄉鎮市區與縣市，和政府資料的景點一樣可以依行政區查詢）
        /// 加一顆 source='merchant' 的 :Current 版本節點，回傳新產生的 uid。
        /// 身分節點跟政府資料的景點一樣：加上分類的類型標籤（category，已經過 MerchantPlaceCategories 檢查），
        /// 帶名稱、介紹與介紹的語意向量（description_embedding，embedding 為 null 時不寫），
        /// 掛細分類（HAS_CATEGORY，旅宿另外掛同名的旅宿等級 HAS_CLASS；Neo4j 沒有這個細分類時不掛），
        /// 分類瀏覽、語意搜尋、附近景點／美食查詢才找得到。
        /// 營業時間（已經過 OperatingHoursRules 整理）跟政府資料一樣，一個時段建一顆 (:OperatingHours) 掛在身分節點。
        /// </summary>
        public async Task<string> CreateMerchantPlaceAsync(MerchantPlaceFields fields, string category, string subCategory, List<double> embedding,
            List<PlaceOperatingHourItem> operatingHours, MerchantPlaceLocation location, string submittedBy)
        {
            string query = @"
                CREATE (a:Place:MerchantPlace {uid: randomUUID(), lat: $lat, lon: $lon, address: $fullAddress,
                    name: $name, description: $description, description_embedding: $embedding})
                CREATE (v:Place:Version:Current {
                    version_id: randomUUID(),
                    version_no: 1,
                    name: $name,
                    address: $address,
                    description: $description,
                    phone: $phone,
                    website: $website,
                    image_url: $imageUrl,
                    source: 'merchant',
                    submitted_by: $submittedBy,
                    valid_from: datetime(),
                    valid_to: null
                })
                CREATE (a)-[:HAS_VERSION]->(v)
                FOREACH (h IN $operatingHours |
                    CREATE (a)-[:HAS_OPERATING_HOURS]->(:OperatingHours {dayOfWeek: h.dayOfWeek, openTime: h.openTime, closeTime: h.closeTime}))
                WITH a, $category AS category, $subCategory AS subCategory" + ApplyCategoryCypher + LinkTownCypher + @"
                RETURN a.uid AS uid
            ";

            var rows = await _gateway.ExecuteCypherAsync(query, new
            {
                category,
                subCategory,
                embedding,
                lat = location.lat,
                lon = location.lng,
                fullAddress = location.full_address,
                townId = location.town_id,
                name = fields.name,
                address = fields.address,
                description = fields.description,
                phone = fields.phone,
                website = fields.website,
                imageUrl = fields.image_url,
                operatingHours = ToOperatingHoursParam(operatingHours),
                submittedBy
            });

            return Neo4jValueConverter.AsString(rows.FirstOrDefault()?.GetValueOrDefault("uid"));
        }

        /// <summary>自建景點的身分節點上是否已經有語意向量（description_embedding）</summary>
        public async Task<bool> HasDescriptionEmbeddingAsync(string uid)
        {
            var rows = await _gateway.ExecuteCypherAsync(@"
                MATCH (a:MerchantPlace {uid: $uid})
                RETURN a.description_embedding IS NOT NULL AS has_embedding", new { uid });

            return rows.FirstOrDefault()?.GetValueOrDefault("has_embedding") is true;
        }

        /// <summary>
        /// 商家改了店名或介紹：更新自建景點身分節點上的名稱、介紹與語意向量。
        /// embedding 為 null（embedding 服務沒開）時拿掉舊的向量，不留跟新介紹對不上的向量。
        /// 只動 :MerchantPlace，政府開放資料的景點不會被修改。
        /// </summary>
        public async Task UpdateMerchantPlaceTextAsync(string uid, string name, string description, List<double> embedding)
        {
            string query = @"
                MATCH (a:MerchantPlace {uid: $uid})
                WITH a, $name AS name, $description AS description" + SetPlaceTextCypher + @"
                SET a.description_embedding = $embedding";

            await _gateway.ExecuteCypherAsync(query, new { uid, name, description, embedding });
        }

        /// <summary>
        /// 商家改了分類或細分類：換掉自建景點的類型標籤與細分類（category、subCategory 已經檢查過）。
        /// 景點已經停用（軟刪除）時只記下新的分類，重新啟用時才套用。
        /// 只動 :MerchantPlace，政府開放資料的景點不會被修改。
        /// </summary>
        public async Task UpdateMerchantPlaceCategoryAsync(string uid, string category, string subCategory)
        {
            string query = @"
                MATCH (a:MerchantPlace {uid: $uid})
                FOREACH (_ IN CASE WHEN a.deactivated_at IS NULL THEN [] ELSE [1] END |
                    SET a.inactive_category = $category, a.inactive_sub_category = $subCategory)
                WITH a, $category AS category, $subCategory AS subCategory
                WHERE a.deactivated_at IS NULL" + ApplyCategoryCypher;

            await _gateway.ExecuteCypherAsync(query, new { uid, category, subCategory });
        }

        /// <summary>
        /// 商家帳號停用（軟刪除）：自建景點拿掉類型標籤與細分類、標成「永久歇業」，
        /// 附近美食、劇本排景點、分類瀏覽與語意搜尋（都會加類型條件）就查不到；
        /// 原本的分類記在 inactive_category / inactive_sub_category，身分節點、版本與營業時間都保留，舊劇本照樣查得到。
        /// 已經停用過的不再處理（不會蓋掉記下來的分類）。只動 :MerchantPlace，政府開放資料的景點不會被修改。
        /// </summary>
        public async Task DeactivateMerchantPlaceAsync(string uid)
        {
            await _gateway.ExecuteCypherAsync(@"
                MATCH (a:MerchantPlace {uid: $uid}) WHERE a.deactivated_at IS NULL
                OPTIONAL MATCH (a)-[:HAS_CATEGORY]->(c:Category)
                WITH a, head(collect(c.name)) AS subCategory
                OPTIONAL MATCH (a)-[old:HAS_CATEGORY|HAS_CLASS]->()
                DELETE old
                WITH DISTINCT a, subCategory
                SET a.inactive_category = head([l IN labels(a) WHERE l IN ['Attraction', 'Restaurant', 'Hotel', 'Event']]),
                    a.inactive_sub_category = subCategory,
                    a.business_status = '永久歇業',
                    a.deactivated_at = datetime()
                REMOVE a:Attraction:Restaurant:Hotel:Event", new { uid });
        }

        /// <summary>
        /// 商家帳號重新啟用：把停用時記下的分類、細分類套回自建景點，拿掉「永久歇業」。沒停用過的不處理。
        /// </summary>
        public async Task ReactivateMerchantPlaceAsync(string uid)
        {
            string query = @"
                MATCH (a:MerchantPlace {uid: $uid}) WHERE a.deactivated_at IS NOT NULL
                WITH a, a.inactive_category AS category, a.inactive_sub_category AS subCategory
                REMOVE a.inactive_category, a.inactive_sub_category, a.business_status, a.deactivated_at" + ApplyCategoryCypher;

            await _gateway.ExecuteCypherAsync(query, new { uid });
        }

        /// <summary>
        /// 商家改了營業時間：刪掉自建景點原本的 (:OperatingHours)，整份換成新的（已經過 OperatingHoursRules 整理）。
        /// 只動 :MerchantPlace，政府開放資料的景點不會被修改。
        /// </summary>
        public async Task ReplaceMerchantOperatingHoursAsync(string uid, List<PlaceOperatingHourItem> operatingHours)
        {
            await _gateway.ExecuteCypherAsync(@"
                MATCH (a:MerchantPlace {uid: $uid})
                OPTIONAL MATCH (a)-[:HAS_OPERATING_HOURS]->(old:OperatingHours)
                DETACH DELETE old
                WITH DISTINCT a
                FOREACH (h IN $operatingHours |
                    CREATE (a)-[:HAS_OPERATING_HOURS]->(:OperatingHours {dayOfWeek: h.dayOfWeek, openTime: h.openTime, closeTime: h.closeTime}))",
                new { uid, operatingHours = ToOperatingHoursParam(operatingHours) });
        }

        /// <summary>營業時間轉成 Cypher 參數（屬性名稱跟政府資料的 (:OperatingHours) 一樣）</summary>
        private static List<Dictionary<string, object>> ToOperatingHoursParam(List<PlaceOperatingHourItem> operatingHours)
        {
            return (operatingHours ?? new List<PlaceOperatingHourItem>())
                .Select(h => new Dictionary<string, object>
                {
                    ["dayOfWeek"] = h.day_of_week,
                    ["openTime"] = h.open_time,
                    ["closeTime"] = h.close_time
                })
                .ToList();
        }

        /// <summary>
        /// 商家改了地址：更新自建景點身分節點的座標、完整地址，並重新掛鄉鎮市區與縣市。
        /// 只動 :MerchantPlace，政府開放資料的景點不會被修改。
        /// </summary>
        public async Task UpdateMerchantPlaceLocationAsync(string uid, MerchantPlaceLocation location)
        {
            string query = @"
                MATCH (a:MerchantPlace {uid: $uid})
                SET a.lat = $lat, a.lon = $lon, a.address = $fullAddress" + LinkTownCypher;

            await _gateway.ExecuteCypherAsync(query, new
            {
                uid,
                lat = location.lat,
                lon = location.lng,
                fullAddress = location.full_address,
                townId = location.town_id
            });
        }

        /// <summary>
        /// 刪掉剛建立、還沒被任何劇本用到的商家自建景點（身分節點、版本節點與營業時間一起刪），商家註冊的 MySQL 寫入失敗時清理用。
        /// 只刪 :MerchantPlace，政府開放資料的景點不會被刪。
        /// </summary>
        public async Task DeleteUnusedMerchantPlaceAsync(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return;

            await _gateway.ExecuteCypherAsync(@"
                MATCH (a:MerchantPlace {uid: $uid})
                OPTIONAL MATCH (a)-[:HAS_VERSION|HAS_OPERATING_HOURS]->(child)
                DETACH DELETE child, a", new { uid });
        }
    }
}
