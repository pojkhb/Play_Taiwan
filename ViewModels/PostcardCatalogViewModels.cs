using System;

namespace backend.ViewModels
{
    /// <summary>明信片主檔回應內容。</summary>
    public class PostcardCatalogResponse
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

    /// <summary>新增/更新明信片主檔的請求內容。</summary>
    public class PostcardCatalogRequest
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
    }
}