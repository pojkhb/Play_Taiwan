using System.Collections.Generic;

namespace backend.Models
{
    // ===================== 地圖導覽 (對應資料表 place) =====================
    public class NavigationRequest
    {
        public int node_id { get; set; }   // 欲導航前往的節點代號，對應 story_node.sn_id
    }

    public class NavigationResponse
    {
        public string maps_deeplink_url { get; set; }   // Google Maps 導航連結，對應 place.p_navigation_url
    }

    // ===================== 附近景點 (對應資料表 place) =====================
    public class NearbyPlaceResponse
    {
        public int place_id { get; set; }                   // 景點/店家代號，對應 place.p_id
        public string category { get; set; }                  // 分類：美食及其他
        public string name { get; set; }                        // 名稱
        public string address { get; set; }                      // 地址
        public string open_time { get; set; }                      // 開放/營業時間
        public List<string> photo_urls { get; set; }              // 圖片網址陣列
        public string maps_deeplink_url { get; set; }                // Google Maps 導航連結
    }
}
