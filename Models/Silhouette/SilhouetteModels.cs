// 檔案路徑：System\Models\Silhouette\SilhouetteModels.cs
// 對應新資料表 `silhouette`（剪影主表，素材可跨劇本節點重複使用）。
using System.Collections.Generic;

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

    /// <summary>為劇本節點產生剪影的結果</summary>
    public class SilhouetteGenerateResult
    {
        public int story_id { get; set; }

        /// <summary>這次新做的剪影數</summary>
        public int generated { get; set; }

        /// <summary>用了其他劇本已經做過的同一張照片剪影</summary>
        public int reused { get; set; }

        public List<SilhouetteNodeResult> nodes { get; set; } = new List<SilhouetteNodeResult>();
    }

    public class SilhouetteNodeResult
    {
        public int sn_id { get; set; }
        public int sn_order { get; set; }
        public string place_name { get; set; }

        /// <summary>
        /// generated = 新做的、reused = 沿用同一張照片的剪影、already_linked = 節點之前就有剪影、
        /// no_photo = 景點沒有照片、failed = 下載或處理失敗（看 message）
        /// </summary>
        public string status { get; set; }

        /// <summary>剪影圖片網址（相對路徑），例如 /images/silhouettes/generated/xxx.png</summary>
        public string silhouette_image_url { get; set; }

        /// <summary>剪影佔畫面比例；接近 1 代表照片沒有天空，剪影會是一整塊（只有這次新做的才有值）</summary>
        public double? area_ratio { get; set; }

        public string message { get; set; }
    }
}
