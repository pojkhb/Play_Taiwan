// 檔案路徑：System\Models\Merchant\MerchantModels.cs
// 對應新資料表 `merchant_media`（商家影音表）與 `store`（商家資訊表）。
// 舊版商家影音是借用遊客的 ep_vlog，新資料庫已拆成獨立的 merchant_media。
using System;

namespace backend.Models
{
    /// <summary>商家影音專案，對應資料表 `merchant_media` 的一列。</summary>
    public class MerchantMedia
    {
        public int mm_id { get; set; }                   // 商家影音流水號
        public int au_id { get; set; }                     // 建立專案的商家，對應 auth.au_id
        public int? nt_id { get; set; }                      // 敘事語氣，對應 narrative_tone.nt_id
        public string mm_task_id { get; set; }                 // 外部 AI 服務的任務 ID
        public string mm_title { get; set; }                     // 影音專案標題
        public string mm_description { get; set; }                 // 商品或服務說明
        public string mm_text { get; set; }                          // 推廣資訊文字
        public string mm_hashtage { get; set; }                        // 推薦標籤（逗號分隔）
        public string mm_video_url { get; set; }                         // 完成的 reels 影片網址
        public string mm_thumbnail { get; set; }                           // reels 封面網址
        public string mm_aspect { get; set; }                                // 輸出比例
        /// <summary>專案狀態：1=草稿、2=處理中、3=已完成、4=失敗。</summary>
        public int mm_status { get; set; }
        public string error_message { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
    }

    /// <summary>商家「已生成檔案」列表的單筆項目。</summary>
    public class MerchantFileItem
    {
        public int mm_id { get; set; }              // 商家影音流水號
        public string mm_title { get; set; }          // 標題
        public string mm_video_url { get; set; }        // 影片網址
        public int mm_status { get; set; }                // 1=草稿、2=處理中、3=已完成、4=失敗
        public DateTime updated_at { get; set; }            // 最後更新時間
    }

    /// <summary>商家影音最後生成畫面（Reels 影音、推薦配文、標籤）。</summary>
    public class MerchantVlogResult
    {
        public int mm_id { get; set; }                 // 商家影音流水號
        public string mm_title { get; set; }             // 標題
        public string caption { get; set; }                // 推薦配文，取自 mm_text
        public string mm_video_url { get; set; }             // 影片網址
        public string[] hashtags { get; set; }                 // 標籤陣列，由 mm_hashtage 逗號拆開
        public int mm_status { get; set; }                       // 1=草稿、2=處理中、3=已完成、4=失敗
    }
}
