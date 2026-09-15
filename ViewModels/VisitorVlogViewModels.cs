// 檔案路徑：System\ViewModels\VisitorVlogViewModels.cs
// 注意：VlogCreateFinalApiResponse 與 VlogTaskStatusApiResponse 已在 MerchantVlogViewModels.cs 定義過，
// 因為商家端與遊客端共用同一組外部 create_final / check_status 端點與回應格式，這裡不重複宣告，直接沿用即可。
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace backend.ViewModels
{
    #region Preview（回憶預覽）

    /// <summary>遊戲結束時，玩家走過的單一景點紀錄。</summary>
    public class SpotHistoryItem
    {
        /// <summary>真實景點名稱，用於查詢 Neo4j 抓取知識（如："臺南孔廟"）。必填。</summary>
        public string spot_name { get; set; }

        /// <summary>遊戲中的代號或別稱（如："全臺首學"）。選填。</summary>
        public string location_codename { get; set; }

        /// <summary>抵達時間，格式如 "2026-08-27 14:00"。選填，未提供時外部 API 會用預設值。</summary>
        public string visit_time { get; set; }
    }

    /// <summary>
    /// 產生 Vlog 專屬回憶預覽請求。對應「遊戲結束」時呼叫。
    /// </summary>
    public class VisitorVlogPreviewRequest
    {
       /// <summary>玩家剛完成的劇本 ID。必填。</summary>
    public string story_id { get; set; }
    }

    public class VisitorVlogPreviewApiResponse
    {
        public string status { get; set; }
        public VisitorDraftPreview draft_preview { get; set; }
    }

    public class VisitorDraftPreview
    {
        public List<ItineraryItem> itinerary { get; set; }
        public string suggested_script { get; set; }
        public List<string> seo_keywords { get; set; }
        public string promo_copy { get; set; }
    }

    public class ItineraryItem
    {
        public string spot_name { get; set; }
        public string location_codename { get; set; }
        public string visit_time { get; set; }
        public string db_description { get; set; }
    }

    #endregion

    #region CreateFinal（正式合成影片）

    /// <summary>
    /// 正式合成 Vlog 影片請求。對應「玩家確認或微調旁白後」呼叫。
    /// 回應共用 MerchantVlogViewModels.cs 裡的 VlogCreateFinalApiResponse，不重複定義。
    /// </summary>
    public class VisitorVlogCreateFinalRequest
    {
          /// <summary>經使用者確認或微調後的最終旁白文字。必填。</summary>
    public string final_script { get; set; }

    /// <summary>此次 Vlog 關聯的劇本 ID。必填（用來抓照片、寫入 ep_vlog.story_id）。</summary>
    public string story_id { get; set; }

    /// <summary>選填：圖片對應地點時間的中繼資料 JSON 字串。</summary>
    public string spot_meta_json { get; set; }
    }

    #endregion

    #region 探員 Vlog 完成紀錄（對應 ep_vlog 表）

    /// <summary>探員的 Vlog 完成紀錄，欄位對應資料庫 ep_vlog 表實際結構。</summary>
    public class EpVlog
    {
        public string EpId { get; set; }
        public string VlogId { get; set; }
        public string StoryId { get; set; }
        public string VideoUrl { get; set; }
        public string ThumbnailUrl { get; set; }
        public System.DateTime CompletedAt { get; set; }
    }

    #endregion
}