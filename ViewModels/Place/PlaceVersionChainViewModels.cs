using System.Collections.Generic;

namespace backend.ViewModels
{
    /// <summary>商家註冊時，搜尋既有景點的候選項目。</summary>
    public class PlaceSearchResultItem
    {
        public string uid { get; set; }
        public string name { get; set; }
        public string address { get; set; }
        /// <summary>景點類型，取自 Neo4j labels（扣除共用的 Place 標籤）。</summary>
        public string type { get; set; }
    }

    /// <summary>
    /// 商家可自行編輯、寫入版本鏈的欄位。PUT 更新商家資料、註冊時建立新景點都共用這組欄位。
    /// </summary>
    public class MerchantPlaceFields
    {
        public string name { get; set; }
        public string address { get; set; }
        public string description { get; set; }
        public string phone { get; set; }
        public string website { get; set; }
        public string opening_hours { get; set; }
    }

    /// <summary>
    /// 商家註冊時選「都沒有，我要建立新的」景點要帶的資料：版本鏈欄位 + 座標與分類。
    /// 地址（註冊請求的 store_city、store_town、store_address）與座標至少填一個，只填一個時後端自動轉另一個，
    /// 轉不了才要兩個都填（見 StoreLocationService）；景點會掛到 Neo4j 的鄉鎮市區。
    /// 分類影響任務類型判斷（Restaurant 會加地方美食型）。
    /// </summary>
    public class MerchantNewPlace : MerchantPlaceFields
    {
        /// <summary>緯度（選填，前端地圖選點取得；和 lng 要一起帶）</summary>
        public double? lat { get; set; }

        /// <summary>經度（選填）</summary>
        public double? lng { get; set; }

        /// <summary>景點分類：Attraction / Restaurant / Hotel / Event，不帶時預設 Restaurant</summary>
        public string category { get; set; }
    }

    /// <summary>Neo4j 的鄉鎮市區節點（:Town），town_id 為「縣市_鄉鎮市區」。</summary>
    public class TownRef
    {
        public string town_id { get; set; }
        public string town_name { get; set; }
        public string city_name { get; set; }
    }

    /// <summary>
    /// 商家自建景點的位置：縣市、鄉鎮市區、地址與座標（地址、座標最後都要有，只填一個時由後端自動轉另一個）。
    /// 地址轉換 API 也回傳這個，欄位名稱與註冊請求相同，前端可以直接填回表單。
    /// </summary>
    public class MerchantPlaceLocation
    {
        /// <summary>縣市（Neo4j 的寫法，例如「台中市」）</summary>
        public string store_city { get; set; }

        /// <summary>鄉鎮市區，例如「西區」</summary>
        public string store_town { get; set; }

        /// <summary>不含縣市、鄉鎮市區的地址，例如「英才路600號」</summary>
        public string store_address { get; set; }

        /// <summary>完整地址（縣市＋鄉鎮市區＋地址）</summary>
        public string full_address { get; set; }

        public double lat { get; set; }
        public double lng { get; set; }

        /// <summary>Neo4j Town.id（「縣市_鄉鎮市區」），景點掛鄉鎮用，不回傳給前端</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string town_id { get; set; }
    }

    /// <summary>單一圖片資訊，對應 Neo4j (:Image) 節點。</summary>
    public class PlaceImageItem
    {
        public string url { get; set; }
        public string description { get; set; }
    }

    /// <summary>單一營業時段，對應 Neo4j (:OperatingHours) 節點。</summary>
    public class PlaceOperatingHourItem
    {
        public string day_of_week { get; set; }
        public string open_time { get; set; }
        public string close_time { get; set; }
    }

    /// <summary>
    /// 查詢景點目前生效資料的回應：政府原始資料（完整景點詳情）+ 版本鏈覆蓋後的最新內容
    /// （若商家補充過）。gov_ 開頭的欄位一律來自政府開放資料節點本身，不會因商家編輯而改變；
    /// merchant_override 才是商家可以覆蓋的內容。
    /// </summary>
    public class PlaceCurrentInfo
    {
        public string uid { get; set; }
        /// <summary>身分節點的原始 labels，例如 ["Attraction","Place"] 或 ["Place","MerchantPlace"]。</summary>
        public List<string> identity_labels { get; set; }

        public string gov_name { get; set; }
        public string gov_description { get; set; }
        public string gov_address { get; set; }
        public double? gov_lat { get; set; }
        public double? gov_lon { get; set; }
        /// <summary>營業狀態／活動狀態，不同類型節點的意義略有差異（見 identity_labels 判斷類型）。</summary>
        public string gov_status { get; set; }
        public string gov_phone { get; set; }
        public string gov_website { get; set; }
        public string gov_ticket_info { get; set; }
        public string gov_travel_info { get; set; }

        /// <summary>景點分類（HAS_CATEGORY），例如 ["遊憩類"]。</summary>
        public List<string> categories { get; set; }
        /// <summary>所屬縣市（LOCATED_IN_CITY）。</summary>
        public string city { get; set; }
        /// <summary>所屬鄉鎮市區（LOCATED_IN_TOWN）。</summary>
        public string town { get; set; }
        /// <summary>景點圖片清單（HAS_IMAGE）。</summary>
        public List<PlaceImageItem> images { get; set; }
        /// <summary>每日營業時段（HAS_OPERATING_HOURS），Event 類型通常沒有這個關聯，會是空陣列。</summary>
        public List<PlaceOperatingHourItem> operating_hours { get; set; }
        /// <summary>旅宿類型（HAS_CLASS），只有 Hotel 節點會有值，其餘型別是空陣列。</summary>
        public List<string> hotel_classes { get; set; }

        /// <summary>商家版本鏈目前生效（:Current）的覆蓋內容；沒有商家補充過則為 null。</summary>
        public Dictionary<string, object> merchant_override { get; set; }
    }
}
