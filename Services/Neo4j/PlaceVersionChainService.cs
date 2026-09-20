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

        private static List<PlaceOperatingHourItem> ToOperatingHours(object raw)
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
            return result;
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
                    opening_hours: $openingHours,
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
                openingHours = fields.opening_hours,
                source,
                submittedBy
            });
        }

        /// <summary>
        /// 商家註冊時選「都沒有，我要建立新的」景點：建立一顆全新身分節點（:Place:MerchantPlace）
        /// 加一顆 source='merchant' 的 :Current 版本節點，回傳新產生的 uid。
        /// </summary>
        public async Task<string> CreateMerchantPlaceAsync(MerchantPlaceFields fields, string submittedBy)
        {
            const string query = @"
                CREATE (a:Place:MerchantPlace {uid: randomUUID()})
                CREATE (v:Place:Version:Current {
                    version_id: randomUUID(),
                    version_no: 1,
                    name: $name,
                    address: $address,
                    description: $description,
                    phone: $phone,
                    website: $website,
                    opening_hours: $openingHours,
                    source: 'merchant',
                    submitted_by: $submittedBy,
                    valid_from: datetime(),
                    valid_to: null
                })
                CREATE (a)-[:HAS_VERSION]->(v)
                RETURN a.uid AS uid
            ";

            var rows = await _gateway.ExecuteCypherAsync(query, new
            {
                name = fields.name,
                address = fields.address,
                description = fields.description,
                phone = fields.phone,
                website = fields.website,
                openingHours = fields.opening_hours,
                submittedBy
            });

            return Neo4jValueConverter.AsString(rows.FirstOrDefault()?.GetValueOrDefault("uid"));
        }

        /// <summary>
        /// 商家刪除帳號時清理 Neo4j 側資料：
        /// - 若身分節點是商家自建的（:MerchantPlace），整條身分節點連同所有版本節點一併刪除。
        /// - 若是政府開放資料的景點，只刪除商家自己的 :Current 版本節點，絕對不動原始身分節點。
        /// </summary>
        public async Task DeleteMerchantVersionAsync(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return;

            const string checkQuery = @"
                MATCH (a {uid: $uid})
                RETURN labels(a) AS labels
            ";
            var checkRows = await _gateway.ExecuteCypherAsync(checkQuery, new { uid });
            var labels = checkRows.Count > 0
                ? Neo4jValueConverter.AsStringList(checkRows[0].GetValueOrDefault("labels"))
                : new List<string>();

            if (labels.Contains("MerchantPlace"))
            {
                const string deleteAllQuery = @"
                    MATCH (a {uid: $uid})
                    OPTIONAL MATCH (a)-[:HAS_VERSION]->(v)
                    DETACH DELETE v, a
                ";
                await _gateway.ExecuteCypherAsync(deleteAllQuery, new { uid });
            }
            else
            {
                const string deleteCurrentOnlyQuery = @"
                    MATCH (a {uid: $uid})-[:HAS_VERSION]->(v:Current)
                    WHERE v.source = 'merchant'
                    DETACH DELETE v
                ";
                await _gateway.ExecuteCypherAsync(deleteCurrentOnlyQuery, new { uid });
            }
        }
    }
}
