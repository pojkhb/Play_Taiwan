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
        /// <summary>景點/店家代號</summary>
        public int place_id { get; set; }

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

        /// <summary>緯度（前端依劇本節點算距離、排序用）</summary>
        public double? lat { get; set; }

        /// <summary>經度</summary>
        public double? lng { get; set; }
    }
}
