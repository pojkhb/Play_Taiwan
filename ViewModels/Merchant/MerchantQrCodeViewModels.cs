namespace backend.ViewModels
{
    /// <summary>商家掃描到 QR Code 後，把 qr_uid 跟自己店家的優惠券綁定。</summary>
    public class QrCodeBindRequest
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
        /// 店家照片（完整網址）：用商家的景點 ID（store.store_uid）向 Neo4j 拿，一個景點有多張時固定是同一張，
        /// 跟地圖、劇本用的是同一張；沒有照片時為 null（全部的照片在 merchant_place.images）。
        /// </summary>
        public string image_url { get; set; }
        /// <summary>是否為此次掃描新寫入的領取紀錄；未登入、非遊客，或先前已領取過同一張優惠券時為 false。</summary>
        public bool is_newly_claimed { get; set; }
    }
}
