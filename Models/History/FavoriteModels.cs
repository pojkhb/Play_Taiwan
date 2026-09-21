namespace backend.Models
{
    // ===================== 收藏 =====================
    public class FavoriteItemResponse
    {
        public string favorite_id { get; set; }     // 收藏紀錄的代號
        public string item_type { get; set; }         // 收藏項目類型：postcard/badge/vlog
        public string ref_id { get; set; }              // 關聯的項目實際代號
        public string image_url { get; set; }            // 縮圖網址
        public string title { get; set; }                  // 顯示標題
    }
}
