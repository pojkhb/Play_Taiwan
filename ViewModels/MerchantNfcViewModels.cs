namespace backend.ViewModels
{
    /// <summary>商家掃描到 NFC 貼紙後，把貼紙 UID 跟指定優惠券綁定。</summary>
    public class NfcBindRequest
    {
        public string nfc_uid { get; set; }
        public int coupon_id { get; set; }
    }

    /// <summary>使用者掃描 NFC 貼紙的回應：商家資訊來自 Neo4j，優惠券資訊來自 MySQL。</summary>
    public class NfcScanResponse
    {
        public CouponResponse coupon { get; set; }
        public PlaceCurrentInfo merchant_place { get; set; }
        /// <summary>是否為此次掃描新寫入的領取紀錄；若使用者先前已領取過同一張優惠券則為 false。</summary>
        public bool is_newly_claimed { get; set; }
    }

    /// <summary>核銷優惠券請求。</summary>
    public class CouponRedeemRequest
    {
        public int au_id { get; set; }
    }
}
