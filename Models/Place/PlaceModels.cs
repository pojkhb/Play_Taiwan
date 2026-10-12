using System.Collections.Generic;

namespace backend.Models
{
    // ===================== 地圖導覽 (對應資料表 place) =====================
    public class NavigationRequest
    {
        public int node_id { get; set; }   // 欲導航前往的節點代號，對應 story_node.sn_id
    }

    /// <summary>導航資訊</summary>
    public class NavigationResponse
    {
        /// <summary>Google Maps 導航連結，前端直接開啟即可導航</summary>
        public string maps_deeplink_url { get; set; }
    }

    // ===================== 附近景點 (對應資料表 place) =====================
    /// <summary>周邊好去的一個地點</summary>
    public class NearbyPlaceResponse
    {
        /// <summary>景點/店家代號（MySQL place.p_id）；只有 Neo4j 資料的地點為 0，列表的 key 請改用 place_uid</summary>
        public int place_id { get; set; }

        /// <summary>景點的 Neo4j uid；只有 MySQL 資料時為 place-{p_id}</summary>
        public string place_uid { get; set; }

        /// <summary>分類：飲食、其他</summary>
        public string category { get; set; }

        /// <summary>類型，例如「小吃」「古蹟」（place.p_type）</summary>
        public string type { get; set; }

        /// <summary>名稱</summary>
        public string name { get; set; }

        /// <summary>地址</summary>
        public string address { get; set; }

        /// <summary>開放/營業時間</summary>
        public string open_time { get; set; }

        /// <summary>照片網址清單</summary>
        public List<string> photo_urls { get; set; }

        /// <summary>Google Maps 導航連結</summary>
        public string maps_deeplink_url { get; set; }

        /// <summary>跟使用者目前位置的直線距離（公尺），清單已經由近到遠排好</summary>
        public int distance_m { get; set; }

        /// <summary>現在有沒有營業／開放；沒有資料或看不懂時為 null（不確定）。美食類已知沒營業的不會列出</summary>
        public bool? is_open_now { get; set; }

        /// <summary>緯度</summary>
        public double? lat { get; set; }

        /// <summary>經度</summary>
        public double? lng { get; set; }
    }
}
