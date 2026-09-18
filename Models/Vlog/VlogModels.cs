using System;

namespace backend.Models
{
    // ===================== Vlog (對應資料表 au_vlog) =====================
    public class VlogGenerateRequest
    {
        public string story_id { get; set; }   // 欲生成 Vlog 的故事代號
    }

    public class VlogResponse
    {
        public string vlog_id { get; set; }              // Vlog 代號
        public string story_id { get; set; }               // 所屬故事代號
        public string video_url { get; set; }                // 影片網址
        public string thumbnail_url { get; set; }              // 影片縮圖網址
        public DateTime completed_date { get; set; }            // 生成完成日期
    }
}
