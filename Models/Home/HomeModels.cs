using System.Collections.Generic;

namespace backend.Models
{
    // ===================== 首頁總覽 =====================

    /// <summary>首頁總覽回應。統計數字皆依目前登入的 au_id 計算。
    /// 欄位加上 ho_ 前綴，代表這是首頁專屬的彙整 DTO，不直接對應單一資料表欄位。</summary>
    public class HomeOverviewResponse
    {
        /// <summary>已完成的劇本（探索）數量</summary>
        public int ho_completed_story { get; set; }

        /// <summary>已收集的明信片數量</summary>
        public int ho_postcard_count { get; set; }

        /// <summary>已解鎖的徽章數量</summary>
        public int ho_badge_count { get; set; }

        /// <summary>已完成的 Vlog 影片數量</summary>
        public int ho_vlog_count { get; set; }

        /// <summary>最近收藏/探險的卡片列表</summary>
        public List<HomeCardItem> ho_recent_cards { get; set; }
    }

    /// <summary>首頁卡片項目。欄位加上 hc_ 前綴，hc_id 依 hc_type 指向不同資料表的整數主鍵。</summary>
    public class HomeCardItem
    {
        /// <summary>卡片對應資料的代號：hc_type = story 時是劇本代號、postcard 時是明信片代號、vlog 時是 Vlog 代號</summary>
        public int hc_id { get; set; }

        /// <summary>卡片類型：story（劇本）、postcard（明信片）、vlog（影片）</summary>
        public string hc_type { get; set; }

        /// <summary>卡片標題</summary>
        public string hc_title { get; set; }

        /// <summary>卡片圖片網址</summary>
        public string hc_image { get; set; }
    }
}