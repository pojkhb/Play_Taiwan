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

        /// <summary>選擇「建立新景點」時要寫入 Neo4j 的欄位；與 place_uid 二擇一。</summary>
        public MerchantPlaceFields new_place { get; set; }
    }

    public class MerchantRegisterResponse
    {
        public int au_id { get; set; }
        public int s_id { get; set; }
        public string store_uid { get; set; }
    }

    /// <summary>
    /// 更新商家資料請求。這些欄位同時會同步寫入 Neo4j 版本鏈（見 PlaceVersionChainService），
    /// 讓 NFC 掃描回傳的商家資訊跟著更新。
    /// </summary>
    public class MerchantUpdateRequest
    {
        public string store_name { get; set; }
        public string store_dec { get; set; }
        public string store_address { get; set; }
        public string store_city { get; set; }
        public string store_town { get; set; }
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
