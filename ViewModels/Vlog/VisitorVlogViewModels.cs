// 檔案路徑：System\ViewModels\Vlog\VisitorVlogViewModels.cs
// VlogSpotMeta、VlogCreateFinalApiResponse 與 VlogJobApiResponse 定義在 MerchantVlogViewModels.cs（商家、遊客共用同一組外部端點）。
using System;
using System.Collections.Generic;

namespace backend.ViewModels
{
    #region Options（CreateFinal 選填欄位的下拉選單）

    /// <summary>下拉選單的一個選項</summary>
    public class VlogOptionItem
    {
        /// <summary>送給 CreateFinal 的值，例如 warm</summary>
        public string value { get; init; }

        /// <summary>選單上顯示的文字，例如「溫馨」</summary>
        public string label { get; init; }

        /// <summary>補充說明，可以當選項下方的小字；沒有時為 null</summary>
        public string description { get; init; }

        /// <summary>預設選項：CreateFinal 不帶這個欄位時，AI 用的就是它</summary>
        public bool is_default { get; init; }
    }

    /// <summary>CreateFinal 選填欄位的選項（來自 AI 服務 create_final 的規格）</summary>
    public class VisitorVlogOptionsResponse
    {
        /// <summary>配樂風格，對應 CreateFinal 的 bgm_mood</summary>
        public List<VlogOptionItem> bgm_moods { get; set; }

        /// <summary>影片方向，對應 CreateFinal 的 video_orientation</summary>
        public List<VlogOptionItem> video_orientations { get; set; }

        /// <summary>旅遊感想（CreateFinal 的 user_memory_text）字數上限</summary>
        public int user_memory_text_max_length { get; set; }
    }

    #endregion

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

        /// <summary>可以放進影片的照片總數（玩家拍的 + 沒拍的站補上的景點照片）；0 張不能合成影片</summary>
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

    /// <summary>VLOG 裡的一個景點；送出合成時，這個景點的每張照片各轉成一筆 spot_meta_json（VlogSpotMeta）</summary>
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

        /// <summary>
        /// 這個景點會放進影片的照片。Preview 回傳的是照片網址，可以直接顯示：玩家在這站拍的；
        /// 沒拍照的站是景點照片（uses_place_photo = true），但還沒抵達這一站時不給（空陣列，只看 photo_count）。
        /// HEIC 照片不能合成影片，不會出現在這裡
        /// </summary>
        public List<string> images { get; set; }

        /// <summary>這個景點放進影片的照片數</summary>
        public int photo_count { get; set; }

        /// <summary>玩家在這站沒有照片，影片改用景點照片</summary>
        public bool uses_place_photo { get; set; }
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

        /// <summary>
        /// 選填：玩家自己寫的旅遊感想，最多 3000 字。AI 自動配樂時會跟旁白一起判斷風格，不會出現在影片裡；
        /// 不帶就只看旁白
        /// </summary>
        public string user_memory_text { get; set; }

        /// <summary>
        /// 選填：配樂風格。auto（預設，AI 依旁白自動挑選）、cheerful 輕快、relaxed 悠閒、warm 溫馨、
        /// nostalgic 回憶、adventurous 探索、neutral 中性、none 不配樂
        /// </summary>
        public string bgm_mood { get; set; }

        /// <summary>選填：影片方向。landscape（預設，橫式 1920×1080）、portrait（直式 1080×1920）</summary>
        public string video_orientation { get; set; }
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

        /// <summary>處理中（status=2）時 AI 目前的階段，例如「等待處理」「正在生成 XTTS 旁白」；其他狀態或 AI 暫時連不上時是 null</summary>
        public string stage { get; set; }

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
