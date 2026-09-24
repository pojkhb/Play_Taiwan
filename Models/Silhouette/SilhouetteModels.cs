// 檔案路徑：System\Models\Silhouette\SilhouetteModels.cs
// 對應新資料表 `silhouette`（剪影主表，素材可跨劇本節點重複使用）。
namespace backend.Models
{
    /// <summary>剪影素材</summary>
    public class Silhouette
    {
        /// <summary>剪影代號</summary>
        public int si_id { get; set; }

        /// <summary>剪影名稱，例如「台北101剪影」</summary>
        public string si_name { get; set; }

        /// <summary>剪影類型，例如「地標」</summary>
        public string si_type { get; set; }

        /// <summary>未解鎖時顯示的剪影圖片網址</summary>
        public string si_silhouette_image { get; set; }

        /// <summary>解鎖後顯示的正常圖片網址</summary>
        public string si_image_url { get; set; }

        /// <summary>剪影的提示文字，例如「城市裡最高的那根針」</summary>
        public string si_hint { get; set; }
    }
}
