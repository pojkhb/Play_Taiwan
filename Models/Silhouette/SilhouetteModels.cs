// 檔案路徑：System\Models\Silhouette\SilhouetteModels.cs
// 對應新資料表 `silhouette`（剪影主表，素材可跨劇本節點重複使用）。
namespace backend.Models
{
    public class Silhouette
    {
        public int si_id { get; set; }                       // 對應 silhouette.si_id
        public string si_name { get; set; }                    // 剪影名稱
        public string si_type { get; set; }                      // 剪影類型
        public string si_silhouette_image { get; set; }            // 未解鎖時顯示的剪影網址
        public string si_image_url { get; set; }                     // 解鎖後顯示的正常圖片網址
        public string si_hint { get; set; }                            // 剪影預設提示文字
    }
}
