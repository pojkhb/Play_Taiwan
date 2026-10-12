// 檔案路徑：System\Models\Badge\BadgeDrawModels.cs
// 完成劇本後抽勳章（POST /api/Badge/Draw），一個劇本只能抽一枚，記錄在 au_badge.s_id。
using System.Collections.Generic;

namespace backend.Models
{
    /// <summary>勳章基本資料（badge 主表）</summary>
    public class BadgeInfo
    {
        public int b_id { get; set; }

        /// <summary>勳章名稱，例如「霓虹之眼」</summary>
        public string b_name { get; set; }

        /// <summary>勳章類別，例如「島嶼城市」；抽勳章 API 回傳時前面帶圖示，例如「🏝️ 島嶼城市」</summary>
        public string b_fication { get; set; }

        /// <summary>勳章對應物件，例如「台北」</summary>
        public string b_thing { get; set; }

        public string b_image { get; set; }
    }

    public class BadgeDrawRequest
    {
        /// <summary>已完成的劇本 ID（story.s_id）</summary>
        public int story_id { get; set; }
    }

    public class BadgeDrawResponse
    {
        public int story_id { get; set; }

        /// <summary>這個劇本可抽的勳章類別，前面帶圖示，例如「🏝️ 島嶼城市」</summary>
        public List<string> categories { get; set; }

        /// <summary>true = 這次新抽到；false = 之前已經抽過（badge 是當時抽到的那枚）或沒有可抽的勳章（badge 為 null）</summary>
        public bool is_new { get; set; }

        /// <summary>抽到的勳章；可抽的勳章都已收集時為 null</summary>
        public BadgeInfo badge { get; set; }
    }
}
