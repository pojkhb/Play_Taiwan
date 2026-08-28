using System;

namespace backend.Models
{
    /// <summary>剪影圖片資料 (對應資料表 md_silhouette)。</summary>
    public class Silhouette
    {
        public string silhouette_id { get; set; }            // 剪影圖片代號
        public string name { get; set; }                       // 剪影對應的景點/物件名稱
        public string image_url { get; set; }                    // 原始圖片的相對路徑，放在 wwwroot/images 底下
        public string city { get; set; }                            // 所屬城市名稱
        public string category { get; set; }                          // 分類，例如「建築」、「自然景觀」
        public bool is_active { get; set; }                              // 是否啟用此剪影素材
        public int sort_order { get; set; }                                 // 顯示排序值，數字越小越前面
    }
}