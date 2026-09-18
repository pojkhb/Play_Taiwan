using System.Collections.Generic;

namespace backend.Models
{
    // ===================== 首頁總覽 =====================

    /// <summary>首頁總覽回應。統計數字皆依目前登入的 au_id 計算。
    /// 欄位加上 ho_ 前綴，代表這是首頁專屬的彙整 DTO，不直接對應單一資料表欄位。</summary>
    public class HomeOverviewResponse
    {
        public int ho_completed_story { get; set; }    // 已完成故事數量，來自 story_session，條件 au_id=當前使用者 且 ss_status=已完成
        public int ho_postcard_count { get; set; }       // 已收集明信片數量，來自 postcard，條件 au_id=當前使用者
        public int ho_badge_count { get; set; }            // 已解鎖徽章數量，來自 au_badge，條件 au_id=當前使用者
        public int ho_vlog_count { get; set; }               // 已產生 VLOG 數量，來自 au_vlog，條件 au_id=當前使用者 且 av_vlog_status=3(已完成)
        public List<HomeCardItem> ho_recent_cards { get; set; } // 最近收藏/探險的卡片列表
    }

    /// <summary>首頁卡片項目。欄位加上 hc_ 前綴，hc_id 依 hc_type 指向不同資料表的整數主鍵。</summary>
    public class HomeCardItem
    {
        public int hc_id { get; set; }         // 卡片對應資料的主鍵：story.s_id / postcard.p_id / au_vlog.av_id
        public string hc_type { get; set; }      // 卡片類型：story、postcard、vlog
        public string hc_title { get; set; }       // 卡片標題
        public string hc_image { get; set; }        // 卡片圖片網址
    }
}