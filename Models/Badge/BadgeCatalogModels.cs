using System;
using System.Collections.Generic;

namespace backend.Models
{
    /// <summary>依分類分組後的勳章清單，對應「勳章圖鑑」頁面。
    /// 分類目前直接用 badge.b_fication 的文字值分組，資料庫尚無獨立的系列主表。</summary>
    public class BadgeSeriesGroup
    {
        public string series_name { get; set; }        // 對應 badge.b_fication，勳章分類名稱
        public List<BadgeItem> badges { get; set; }      // 此分類底下所有勳章清單
    }

    /// <summary>單一勳章的資訊，包含使用者是否已解鎖。</summary>
    public class BadgeItem
    {
        public int b_id { get; set; }                  // 對應 badge.b_id
        public string b_name { get; set; }               // 對應 badge.b_name
        public string b_thing { get; set; }                // 對應 badge.b_thing，勳章對應物件
        public string b_image { get; set; }                 // 對應 badge.b_image
        public bool is_owned { get; set; }                    // 由 au_badge LEFT JOIN 判斷是否已解鎖
        public DateTime? obtained_at { get; set; }              // 解鎖時間，取自 au_badge.created_at，未解鎖為 null
    }

    /// <summary>使用者已獲得的單一勳章資訊，對應 badge + au_badge。</summary>
    public class BadgeResponse
    {
        public string badge_id { get; set; }             // 徽章代號
        public string badge_name { get; set; }             // 徽章名稱
        public string badge_type { get; set; }               // 徽章類型：景點/類型/等級
        public string image_url { get; set; }                 // 徽章圖片網址
        public DateTime obtained_date { get; set; }             // 獲得日期，對應 au_badge.created_at
    }
}