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
