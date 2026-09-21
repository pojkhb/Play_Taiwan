// 檔案路徑：System\ViewModels\Postcard\PostcardCatalogViewModels.cs
// 對應新資料表 `postcard`（md_postcard + ep_postcard 已合併）。
using System;

namespace backend.ViewModels
{
    /// <summary>明信片回應內容。</summary>
    public class PostcardCatalogResponse
    {
        public int PostcardId { get; set; }            // postcard.p_id
        public int? StoryId { get; set; }                // postcard.s_id
        public int? NodeId { get; set; }                   // postcard.sn_id
        public string PostcardName { get; set; }             // postcard.p_name
        public string Summary { get; set; }                    // postcard.p_summary
        public string ImageUrl { get; set; }                     // postcard.p_imag_url
        public bool IsNightEdition { get; set; }                   // postcard.is_night：1=是、2=否
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
