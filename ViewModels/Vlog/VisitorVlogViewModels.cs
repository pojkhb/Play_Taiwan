// 檔案路徑：System\ViewModels\Vlog\VisitorVlogViewModels.cs
// VlogCreateFinalApiResponse 與 VlogTaskStatusApiResponse 定義在 MerchantVlogViewModels.cs（商家、遊客共用同一組外部端點）。
using System;
using System.Collections.Generic;

namespace backend.ViewModels
{
    #region Preview（遊戲結束 → 旁白草稿）

    public class VisitorVlogPreviewRequest
    {
        /// <summary>玩家剛完成的劇本 ID，對應 story.s_id。必填。</summary>
        public int story_id { get; set; }
    }

    /// <summary>遊客 VLOG 草稿：給玩家確認旁白</summary>
    public class VisitorVlogPreviewResponse
    {
        /// <summary>遊客 VLOG ID（au_vlog.av_id）</summary>
        public int av_id { get; set; }
        public int story_id { get; set; }
        public string story_title { get; set; }

        /// <summary>遊玩時長，例如「2.5小時」</summary>
        public string play_time { get; set; }

        /// <summary>這次遊玩拍的照片總數；0 張不能合成影片</summary>
        public int photo_count { get; set; }

        /// <summary>走過的景點（依劇本順序），含每個景點的照片數</summary>
        public List<VisitorVlogSpot> spots { get; set; }

        /// <summary>AI 旁白草稿，玩家確認或修改後送 CreateFinal 的 final_script</summary>
        public string script { get; set; }

        /// <summary>宣傳文案</summary>
        public string promo_copy { get; set; }

        /// <summary>SEO 關鍵字</summary>
        public List<string> seo_keywords { get; set; }

        /// <summary>AI 整理的行程（含景點資料庫介紹）</summary>
        public List<ItineraryItem> itinerary { get; set; }
    }

    /// <summary>VLOG 裡的一個景點；同時也是送給 AI 的 spot_meta_json 元素格式</summary>
    public class VisitorVlogSpot
    {
        /// <summary>劇本節點順序</summary>
        public int order { get; set; }

        /// <summary>真實景點名稱，例如「宮原眼科」</summary>
        public string spot_name { get; set; }

        /// <summary>遊戲中的地點代號，例如「紅磚眼科」</summary>
        public string location_codename { get; set; }

        /// <summary>劇本節點標題</summary>
        public string node_title { get; set; }

        public string address { get; set; }
        public double? lat { get; set; }
        public double? lng { get; set; }

        /// <summary>抵達時間，格式 yyyy-MM-dd HH:mm；沒有作答紀錄時為 null</summary>
        public string visit_time { get; set; }

        /// <summary>這個景點的照片在 image_zip 裡的檔名（Preview 時為空，只看 photo_count）</summary>
        public List<string> images { get; set; }

        public int photo_count { get; set; }
    }

    /// <summary>送給外部 AI /api/visitor/vlog/preview 的單一景點</summary>
    public class SpotHistoryItem
    {
        /// <summary>真實景點名稱，用於查詢 Neo4j 抓取知識（如："臺南孔廟"）。必填。</summary>
        public string spot_name { get; set; }

        /// <summary>遊戲中的代號或別稱（如："全臺首學"）。選填。</summary>
        public string location_codename { get; set; }

        /// <summary>抵達時間，格式如 "2026-08-27 14:00"。選填，未提供時外部 API 會用預設值。</summary>
        public string visit_time { get; set; }
    }

    /// <summary>外部 API /api/visitor/vlog/preview 的原始回應結構。</summary>
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

    #region CreateFinal（確認旁白 → 打包照片送出合成）

    public class VisitorVlogCreateFinalRequest
    {
        /// <summary>劇本 ID，對應 story.s_id。必填。</summary>
        public int story_id { get; set; }

        /// <summary>玩家確認或修改後的最終旁白。必填。</summary>
        public string final_script { get; set; }

        /// <summary>選填：修改後的宣傳文案；不帶就沿用草稿</summary>
        public string promo_copy { get; set; }

        /// <summary>選填：修改後的關鍵字；不帶就沿用草稿</summary>
        public List<string> seo_keywords { get; set; }
    }

    public class VisitorVlogTaskResponse
    {
        public int av_id { get; set; }
        public int story_id { get; set; }

        /// <summary>外部 AI 任務 ID，輪詢 Status 時要帶回來</summary>
        public string task_id { get; set; }

        /// <summary>送出的照片張數</summary>
        public int photo_count { get; set; }

        /// <summary>1=待處理、2=處理中、3=已完成、4=失敗</summary>
        public int status { get; set; }
        public string status_text { get; set; }
    }

    #endregion

    #region Status（影片結果）

    public class VisitorVlogStatusResponse
    {
        public int av_id { get; set; }
        public int story_id { get; set; }

        /// <summary>1=待處理、2=處理中、3=已完成、4=失敗</summary>
        public int status { get; set; }
        public string status_text { get; set; }

        public string title { get; set; }
        public string final_script { get; set; }
        public string promo_copy { get; set; }
        public List<string> seo_keywords { get; set; }

        /// <summary>完成的影片網址（status=3 才有值）</summary>
        public string video_url { get; set; }
        public string thumbnail { get; set; }

        /// <summary>status=4 時的失敗原因</summary>
        public string error_message { get; set; }

        public DateTime updated_at { get; set; }
    }

    #endregion
}
