using System;

namespace backend.ViewModels
{
    public class CouponCreateRequest
    {
        public int s_id { get; set; }
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
}
