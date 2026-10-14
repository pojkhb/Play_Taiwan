using System;

namespace backend.ViewModels
{
    /// <summary>新增優惠券請求；所屬商家由 JWT 的 s_id 決定，不需帶 s_id。</summary>
    public class CouponCreateRequest
    {
        public string coupon_code { get; set; }
        public string coupon_name { get; set; }
        public string discount_commodity { get; set; }
        /// <summary>percent（百分比折扣）/ amount（固定金額折抵）</summary>
        public string discount_type { get; set; }
        public decimal discount_value { get; set; }
        public DateTime? valid_from { get; set; }
        public DateTime? valid_to { get; set; }
    }

    public class CouponUpdateRequest
    {
        public string coupon_code { get; set; }
        public string coupon_name { get; set; }
        public string discount_commodity { get; set; }
        public string discount_type { get; set; }
        public decimal discount_value { get; set; }
        public DateTime? valid_from { get; set; }
        public DateTime? valid_to { get; set; }
    }

    public class CouponStatusUpdateRequest
    {
        /// <summary>active（上架）/ inactive（下架）</summary>
        public string status { get; set; }
    }

    public class CouponResponse
    {
        public int coupon_id { get; set; }
        public int s_id { get; set; }
        public string coupon_code { get; set; }
        public string coupon_name { get; set; }
        public string discount_commodity { get; set; }
        public string discount_type { get; set; }
        public decimal discount_value { get; set; }
        public DateTime? valid_from { get; set; }
        public DateTime? valid_to { get; set; }
        public string status { get; set; }
    }

    /// <summary>商家查詢自己的優惠券（GET api/merchant/coupons、api/merchant/coupons/{couponId}）：優惠券加上綁定的 QR Code 與店家目前的營業時間。</summary>
    public class MerchantCouponResponse : CouponResponse
    {
        /// <summary>這張優惠券綁定的 QR Code（qrcode_coupon.qr_uid）；還沒綁定時為 null。</summary>
        public string qrcode_uid { get; set; }

        /// <summary>店家營業時間（格式同 GET api/Merchant/Profile 的 operating_hours）；沒有綁定景點、沒填或 Neo4j 連不上時為空陣列。</summary>
        public System.Collections.Generic.List<PlaceOperatingHourItem> operating_hours { get; set; }
    }
}
