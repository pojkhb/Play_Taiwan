// 檔案路徑：System\ViewModels\Postcard\PostcardCatalogViewModels.cs
// 對應新資料表 `postcard`（md_postcard + ep_postcard 已合併）。
using System;

namespace backend.ViewModels
{
    /// <summary>明信片回應內容。</summary>
    public class PostcardCatalogResponse
    {
        /// <summary>明信片代號（查單張、刪除、列印、顯示圖片都用這個）</summary>
        public int PostcardId { get; set; }

        /// <summary>所屬劇本代號，沒有時為 null</summary>
        public int? StoryId { get; set; }

        /// <summary>取得明信片時完成的節點代號，沒有時為 null</summary>
        public int? NodeId { get; set; }

        /// <summary>明信片名稱</summary>
        public string PostcardName { get; set; }

        /// <summary>明信片簡介</summary>
        public string Summary { get; set; }

        /// <summary>明信片圖片網址</summary>
        public string ImageUrl { get; set; }

        /// <summary>是否為夜晚限定版</summary>
        public bool IsNightEdition { get; set; }

        /// <summary>建立時間</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>最後更新時間</summary>
        public DateTime UpdatedAt { get; set; }
    }
}
