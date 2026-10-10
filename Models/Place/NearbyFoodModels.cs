using System.Collections.Generic;

namespace backend.Models
{
    /// <summary>
    /// 用餐時間推播：使用者目前位置附近的美食（GET api/Map/NearbyFood）。
    /// 前端在用餐時間跳出手機本機通知，點開後帶目前 GPS 呼叫這支，顯示附近好吃的。
    /// </summary>
    public class NearbyFoodResponse
    {
        /// <summary>現在是哪一餐（台灣時間）：早餐、午餐、下午茶、晚餐、宵夜</summary>
        public string meal { get; set; }

        /// <summary>可以直接當通知或頁面標題的一句話，例如「午餐時間到了！附近 350 公尺有「山河魯肉飯」，還有優惠券可以領」</summary>
        public string message { get; set; }

        /// <summary>這次搜尋的半徑（公尺）</summary>
        public int radius_m { get; set; }

        /// <summary>附近的美食：有優惠券的店家排前面，其餘由近到遠；已知目前沒營業的不列出</summary>
        public List<NearbyFoodPlace> places { get; set; } = new();
    }

    /// <summary>附近的一間美食</summary>
    public class NearbyFoodPlace
    {
        /// <summary>景點代號（Neo4j uid；只有 MySQL 資料時為 place-{p_id}）</summary>
        public string place_id { get; set; }

        /// <summary>店名</summary>
        public string name { get; set; }

        /// <summary>地址</summary>
        public string address { get; set; }

        /// <summary>類型，例如「小吃」「冰品」；沒有資料時為 null</summary>
        public string type { get; set; }

        public double lat { get; set; }
        public double lng { get; set; }

        /// <summary>跟使用者的直線距離（公尺）</summary>
        public int distance_m { get; set; }

        /// <summary>店家照片；沒有時為 null</summary>
        public string image_url { get; set; }

        /// <summary>今天的營業時間，例如「11:00-14:00、17:00-21:00」；沒有資料時為 null</summary>
        public string open_time { get; set; }

        /// <summary>現在有沒有營業；營業時間沒有資料或看不懂時為 null（不確定）</summary>
        public bool? is_open_now { get; set; }

        /// <summary>這間店目前可以領的優惠券（商家用 QR Code 綁定的那些），沒有時為空陣列</summary>
        public List<NearbyFoodCoupon> coupons { get; set; } = new();

        /// <summary>Google 地圖導航連結</summary>
        public string maps_deeplink_url { get; set; }
    }

    /// <summary>店家目前上架、在有效期內的優惠券</summary>
    public class NearbyFoodCoupon
    {
        public int coupon_id { get; set; }
        public string coupon_name { get; set; }

        /// <summary>折扣品項</summary>
        public string discount_commodity { get; set; }

        /// <summary>percent（百分比折扣）／amount（固定金額折抵）</summary>
        public string discount_type { get; set; }

        public decimal discount_value { get; set; }

        /// <summary>截止時間；沒有期限時為 null</summary>
        public System.DateTime? valid_to { get; set; }
    }
}
