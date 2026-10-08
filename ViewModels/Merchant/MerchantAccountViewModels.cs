namespace backend.ViewModels
{
    /// <summary>
    /// 商家註冊請求。place_uid 有值代表商家在「搜尋既有景點」步驟選了現成的景點；
    /// 若商家選「都沒有，我要建立新的」，place_uid 留空，後端用 store_name、store_address、store_dec
    /// 與 lat、lng、category 等欄位建立新景點。
    /// </summary>
    public class MerchantRegisterRequest
    {
        public string auth_name { get; set; }
        public string auth_email { get; set; }
        public string auth_pswd { get; set; }

        public string store_name { get; set; }
        public string store_dec { get; set; }
        public string store_address { get; set; }
        public string store_city { get; set; }
        public string store_town { get; set; }

        /// <summary>選擇既有景點時的 Neo4j uid（GET api/merchant/register/search-place 回傳的 uid）；留空代表建立新景點。</summary>
        public string place_uid { get; set; }

        // 以下欄位只在建立新景點（沒帶 place_uid）時使用，選既有景點時忽略

        /// <summary>
        /// 緯度（選填，前端地圖選點取得；和 lng 要一起帶）。
        /// 地址（store_city＋store_town＋store_address）與座標（lat/lng）
        /// 至少填一個：只填地址會轉座標（要對到門牌）、只填座標會轉地址（300 公尺內要有門牌），轉不了回 400 請兩個都填。
        /// 前端可以先用 GET api/merchant/register/address-to-location、location-to-address 自動帶出另一個。
        /// </summary>
        public double? lat { get; set; }

        /// <summary>經度（選填）</summary>
        public double? lng { get; set; }

        /// <summary>景點分類：Attraction / Restaurant / Hotel / Event，不帶時預設 Restaurant（Restaurant 會多出地方美食型任務）</summary>
        public string category { get; set; }

        public string phone { get; set; }
        public string website { get; set; }

        /// <summary>
        /// 營業時間（選填），格式跟 Neo4j 政府資料的營業時間相同：一個時段一筆，
        /// day_of_week 為 Monday～Sunday，open_time / close_time 為 24 小時制 "HH:mm"。
        /// 跨夜營業結束時間填隔天的時間（例如 18:00～02:00，營業到半夜 12 點填 00:00），全天營業填 00:00～23:59；
        /// 公休日不用填，中午休息就同一天填兩筆。格式不對或同一天時段重疊回 400。
        /// </summary>
        public System.Collections.Generic.List<PlaceOperatingHourItem> operating_hours { get; set; }
    }

    public class MerchantRegisterResponse
    {
        public int au_id { get; set; }
        public int s_id { get; set; }
        public string store_uid { get; set; }
    }

    /// <summary>
    /// 更新商家資料請求（PUT api/Merchant/Profile）。只更新有帶值的欄位，沒帶（null）的欄位維持原值。
    /// 這些欄位同時會同步寫入 Neo4j 版本鏈（見 PlaceVersionChainService），
    /// 讓 QR Code 掃描回傳的商家資訊跟著更新。
    /// 商家自建景點改了 store_city／store_town／store_address，或帶了 lat/lng 時，景點會重新掛鄉鎮市區並更新位置：
    /// 地址、座標都有改就照填的；只改地址會重轉座標；只帶 lat/lng（重新選點）會重轉地址；轉不了回 400 請兩個都填。
    /// </summary>
    public class MerchantUpdateRequest
    {
        public string store_name { get; set; }
        public string store_dec { get; set; }
        public string store_address { get; set; }
        public string store_city { get; set; }
        public string store_town { get; set; }

        /// <summary>緯度（選填，只對商家自建景點有效；和 lng 要一起帶）</summary>
        public double? lat { get; set; }

        /// <summary>經度（選填）</summary>
        public double? lng { get; set; }

        /// <summary>
        /// 營業時間（選填，只對商家自建景點有效，綁定既有景點的營業時間來自政府資料、不會被修改）。
        /// 格式跟註冊相同；有帶就整份取代原本的營業時間，帶空陣列 [] 代表清空，不帶（null）維持原值。
        /// </summary>
        public System.Collections.Generic.List<PlaceOperatingHourItem> operating_hours { get; set; }
    }

    /// <summary>登入商家查詢自己的店家資料（GET api/Merchant/Profile），多了營業時間給編輯畫面帶入。</summary>
    public class MerchantProfileResponse : MerchantDetailResponse
    {
        /// <summary>是否為商家自建景點：true 才能修改座標（lat/lng）與營業時間</summary>
        public bool is_self_built_place { get; set; }

        /// <summary>目前的營業時間（Neo4j 景點的 operating_hours），沒有綁定景點或沒填時為空陣列</summary>
        public System.Collections.Generic.List<PlaceOperatingHourItem> operating_hours { get; set; }
    }

    public class MerchantDetailResponse
    {
        public int s_id { get; set; }
        public int au_id { get; set; }
        public string store_name { get; set; }
        public string store_dec { get; set; }
        public string store_address { get; set; }
        public string store_city { get; set; }
        public string store_town { get; set; }
        public string store_uid { get; set; }
        public string auth_name { get; set; }
        public string auth_email { get; set; }
        public bool is_active { get; set; }
    }

    public class MerchantListQuery
    {
        public string keyword { get; set; }
        public int page { get; set; } = 1;
        public int page_size { get; set; } = 20;
    }

    public class MerchantListResponse
    {
        public System.Collections.Generic.List<MerchantDetailResponse> items { get; set; }
        public int total { get; set; }
        public int page { get; set; }
        public int page_size { get; set; }
    }
}
