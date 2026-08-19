using System;

namespace backend.Models
{
    /// <summary>明信片主檔 (對應資料表 md_postcard)。</summary>
    public class PostcardCatalog
    {
        public string PostcardId { get; set; }
        public string StoryId { get; set; }
        public string PostcardName { get; set; }
        public string Summary { get; set; }
        public string ImageUrl { get; set; }
        public bool IsNightEditionDefault { get; set; }
        public string Category { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}