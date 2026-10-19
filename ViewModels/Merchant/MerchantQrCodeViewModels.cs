using System;

namespace backend.ViewModels
{
    /// <summary>商家掃描到 QR Code 後，把 qr_uid 跟自己店家的優惠券綁定。</summary>
    public class QrCodeBindRequest
    {
        public string qr_uid { get; set; }
        public int coupon_id { get; set; }
    }

    /// <summary>綁定成功後回傳綁好的 QR Code 與優惠券，前端可以直接拿 qr_uid 顯示 QR Code。</summary>
    public class QrCodeBindResponse
    {
        public string qr_uid { get; set; }
        public int coupon_id { get; set; }
    }

    /// <summary>使用者掃描 QR Code 的回應：商家資訊來自 Neo4j，優惠券資訊來自 MySQL。</summary>
    public class QrCodeScanResponse
    {
        public CouponResponse coupon { get; set; }
        public PlaceCurrentInfo merchant_place { get; set; }
        /// <summary>
        /// 店家照片（完整網址）：用商家的景點 ID（store.store_uid）向 Neo4j 拿。商家在店家資料填了照片網址就用那張
        /// （也在 merchant_place.merchant_override.image_url）；沒填時用景點照片，一個景點有多張時固定是同一張，
        /// 跟地圖、劇本用的是同一張；沒有照片時為 null（景點的全部照片在 merchant_place.images）。
        /// </summary>
        public string image_url { get; set; }
        /// <summary>
        /// 店家營業時間，跟 merchant_place.operating_hours 同一份（拉到頂層方便前端取用），依星期與開始時間排序；
        /// 沒有營業時間時為空陣列。
        /// </summary>
        public System.Collections.Generic.List<PlaceOperatingHourItem> operating_hours { get; set; }
        /// <summary>是否為此次掃描新寫入的領取紀錄；未登入、非遊客，或先前已領取過同一張優惠券時為 false。</summary>
        public bool is_newly_claimed { get; set; }
    }

    /// <summary>使用者擁有的一張優惠券：優惠券內容、店家資訊與這張券目前的狀態。</summary>
    public class MyCouponResponse
    {
        public CouponResponse coupon { get; set; }
        public string store_name { get; set; }
        /// <summary>店家完整地址（縣市＋鄉鎮市區＋地址），都沒填時為 null。</summary>
        public string full_address { get; set; }
        /// <summary>店家照片（完整網址），跟掃 QR Code 拿到的是同一張；沒有照片時為 null。</summary>
        public string image_url { get; set; }
        /// <summary>店家營業時間，格式同掃 QR Code 的 operating_hours；沒有營業時間或 Neo4j 連不上時為空陣列。</summary>
        public System.Collections.Generic.List<PlaceOperatingHourItem> operating_hours { get; set; }
        /// <summary>領取（掃描 QR Code）的時間。</summary>
        public DateTime obtained_at { get; set; }
        public bool is_used { get; set; }
        public DateTime? used_at { get; set; }
        /// <summary>現在能不能用：還沒核銷、商家沒有下架，而且在有效期間內（valid_from／valid_to 沒填代表不限）。</summary>
        public bool is_usable { get; set; }
    }
}
