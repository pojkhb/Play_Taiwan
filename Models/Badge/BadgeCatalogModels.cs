// 檔案路徑：System\Models\Badge\BadgeCatalogModels.cs
// 對應新資料表 `badge`（勳章主表）與 `au_badge`（使用者勳章對照表）。
using System;
using System.Collections.Generic;

namespace backend.Models
{
    /// <summary>
    /// 勳章圖鑑的單筆查詢結果（badge LEFT JOIN au_badge 的扁平列），
    /// 由 BadgeDao 取出後，在 BadgeService 依 b_fication 分組成 BadgeSeriesGroup。
    /// </summary>
    public class BadgeResponse
    {
        public int b_id { get; set; }                  // 對應 badge.b_id
        public string b_name { get; set; }               // 對應 badge.b_name，勳章名稱
        public string b_fication { get; set; }             // 對應 badge.b_fication，勳章分類
        public string b_thing { get; set; }                  // 對應 badge.b_thing，勳章對應物件
        public string b_image { get; set; }                    // 對應 badge.b_image
        public bool is_owned { get; set; }                       // 由 au_badge LEFT JOIN 判斷是否已解鎖
        public DateTime? obtained_at { get; set; }                 // 解鎖時間，取自 au_badge.created_at，未解鎖為 null
    }

    /// <summary>依分類分組後的勳章清單，對應「勳章圖鑑」頁面。
    /// 分類直接用 badge.b_fication 的文字值分組，資料庫沒有獨立的系列主表。</summary>
    public class BadgeSeriesGroup
    {
        /// <summary>勳章分類（系列）名稱</summary>
        public string series_name { get; set; }

        /// <summary>此分類底下的所有勳章</summary>
        public List<BadgeItem> badges { get; set; }
    }

    /// <summary>單一勳章的資訊，包含使用者是否已解鎖。</summary>
    public class BadgeItem
    {
        /// <summary>勳章代號</summary>
        public int b_id { get; set; }

        /// <summary>勳章名稱</summary>
        public string b_name { get; set; }

        /// <summary>勳章對應的物件/達成條件說明</summary>
        public string b_thing { get; set; }

        /// <summary>勳章圖片網址</summary>
        public string b_image { get; set; }

        /// <summary>目前登入者是否已擁有（已解鎖）</summary>
        public bool is_owned { get; set; }

        /// <summary>解鎖時間，未解鎖為 null</summary>
        public DateTime? obtained_at { get; set; }
    }
}
