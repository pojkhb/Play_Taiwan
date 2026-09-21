namespace backend.Models
{
    /// <summary>獎章池項目 (對應 md_badge_pool)。</summary>
    public class BadgePoolItem
    {
        public int BadgePoolId { get; set; }                 // 獎章池紀錄唯一編號
        public string StoryId { get; set; }                    // 所屬劇本代號
        public string BadgeId { get; set; }                     // 徽章代號
        public string BadgeName { get; set; }                    // 徽章名稱
        public int Weight { get; set; }                            // 抽取權重，數字越大越容易被抽到
        public bool IsActive { get; set; }                          // 是否啟用此獎章池項目
    }
}
