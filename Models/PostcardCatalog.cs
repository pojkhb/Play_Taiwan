using System;

namespace backend.Models
{
    /// <summary>明信片主檔 (對應資料表 md_postcard)。</summary>
    public class PostcardCatalog
    {
        public string PostcardId { get; set; }               // 明信片主檔代號
        public string StoryId { get; set; }                    // 所屬劇本代號，對應 md_story.story_id
        public string PostcardName { get; set; }                 // 明信片名稱
        public string Summary { get; set; }                        // 明信片簡述文字
        public string ImageUrl { get; set; }                         // 明信片圖片的相對路徑
        public bool IsNightEditionDefault { get; set; }                // 是否預設為夜間限定版本
        public string Category { get; set; }                             // 分類，例如「文史建築」、「美食」
        public int SortOrder { get; set; }                                  // 顯示排序值，數字越小越前面
        public bool IsActive { get; set; }                                    // 是否啟用此明信片主檔
        public DateTime CreatedAt { get; set; }                                 // 建立時間
        public DateTime UpdatedAt { get; set; }                                  // 最後更新時間
    }
}