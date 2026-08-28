using System;
using System.Collections.Generic;

namespace backend.Models
{
    /// <summary>依系列分組後的徽章清單，對應「徽章圖鑑」頁面。</summary>
    public class BadgeSeriesGroup
    {
        public string series_id { get; set; }                // 徽章系列代號
        public string series_name { get; set; }                // 徽章系列名稱，例如「台灣古籍系列」
        public List<BadgeItem> badges { get; set; }              // 此系列底下所有徽章清單
    }

    /// <summary>單一徽章的資訊，包含探員是否已解鎖。</summary>
    public class BadgeItem
    {
        public string badge_id { get; set; }                 // 徽章代號
        public string badge_name { get; set; }                 // 徽章名稱
        public string description { get; set; }                  // 徽章的介紹說明文字
        public string image_url { get; set; }                      // 徽章圖片的相對路徑，例如 /images/badges/b001.png
        public bool is_owned { get; set; }                           // 是否已解鎖：true=探員已擁有此徽章，false=尚未解鎖(顯示灰階)
        public DateTime? obtained_at { get; set; }                    // 解鎖時間，若尚未解鎖則為 null
    }
}