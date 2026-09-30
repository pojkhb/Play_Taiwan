namespace backend.ViewModels
{
    /// <summary>
    /// 商家註冊請求。place_uid 有值代表商家在「搜尋既有景點」步驟選了現成的景點；
    /// 若商家選「都沒有，我要建立新的」，place_uid 留空、改帶 new_place。
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

        /// <summary>選擇既有景點時的 Neo4j uid；與 new_place 二擇一。</summary>
        public string place_uid { get; set; }

        /// <summary>
        /// 選擇「建立新景點」時的景點資料；與 place_uid 二擇一。
        /// 地址（store_city＋store_town＋store_address，沒填 store_address 時用 new_place.address）與座標（new_place.lat/lng）
        /// 至少填一個：只填地址會轉座標（要對到門牌）、只填座標會轉地址（300 公尺內要有門牌），轉不了回 400 請兩個都填。
        /// 前端可以先用 GET api/merchant/register/address-to-location、location-to-address 自動帶出另一個。
        /// category 可選 Attraction / Restaurant / Hotel / Event（預設 Restaurant）。
        /// </summary>
        public MerchantNewPlace new_place { get; set; }
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
