// 檔案路徑：System\ViewModels\Merchant\MerchantVlogViewModels.cs
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace backend.ViewModels
{
    #region 敘事語氣

    /// <summary>敘事語氣選項（narrative_tone）</summary>
    public class NarrativeToneItem
    {
        /// <summary>敘事語氣流水號，Preview 時帶入 nt_id</summary>
        public int nt_id { get; set; }

        /// <summary>顯示名稱，例如「幽默詼諧」</summary>
        public string nt_name { get; set; }

        /// <summary>語氣說明，會一起送給 AI 當提示</summary>
        public string nt_prompt { get; set; }
    }

    #endregion

    #region Preview（輸入店家資訊 + 照片 → AI 旁白草稿、推薦配文、TAG）

    /// <summary>
    /// 商家 VLOG 生成請求（multipart/form-data）。對應「生成（輸入資訊）」頁面。
    /// </summary>
    public class MerchantVlogPreviewRequest
    {
        /// <summary>選填：要重新產生草稿的專案 ID；不帶就建立新專案</summary>
        public int? mm_id { get; set; }

        /// <summary>店家名稱；不填就用商家資訊表（store）的名稱</summary>
        public string store_name { get; set; }

        /// <summary>營業時間，例如「週二至週日 11:00–21:00，週一公休」</summary>
        public string open_time { get; set; }

        /// <summary>地址；不填就用商家資訊表（store）的地址</summary>
        public string address { get; set; }

        /// <summary>敘事語氣 ID，選項由 GET /api/MerchantVlog/Tones 取得</summary>
        public int? nt_id { get; set; }

        /// <summary>推廣資訊（必填），例如本月主打、優惠內容</summary>
        public string promo_text { get; set; }

        /// <summary>照片（可多張，依上傳順序出現在影片中）。新專案必填；重新產生草稿時不帶就沿用已上傳的照片</summary>
        public List<IFormFile> images { get; set; }
    }

    /// <summary>商家 VLOG 草稿</summary>
    public class MerchantVlogPreviewResponse
    {
        /// <summary>影音專案 ID，之後 CreateFinal / Status 都用它</summary>
        public int mm_id { get; set; }

        public string store_name { get; set; }
        public string open_time { get; set; }
        public string address { get; set; }

        /// <summary>選用的敘事語氣名稱</summary>
        public string tone_name { get; set; }

        /// <summary>AI 旁白草稿，商家確認或修改後送 CreateFinal 的 final_script</summary>
        public string script { get; set; }

        /// <summary>推薦配文</summary>
        public string caption { get; set; }

        /// <summary>推薦 TAG（已補 #）</summary>
        public List<string> hashtags { get; set; }

        /// <summary>AI 建議的目標客群</summary>
        public List<string> target_audience { get; set; }

        /// <summary>這個專案目前的照片（依影片順序）</summary>
        public List<string> image_urls { get; set; }
    }

    #endregion

    #region CreateFinal（確認旁白 → 送出影片合成）

    public class MerchantVlogCreateFinalRequest
    {
        /// <summary>影音專案 ID（Preview 回傳）</summary>
        public int mm_id { get; set; }

        /// <summary>商家確認或修改後的最終旁白（必填）</summary>
        public string final_script { get; set; }

        /// <summary>選填：修改後的推薦配文；不帶就沿用草稿</summary>
        public string caption { get; set; }

        /// <summary>選填：修改後的 TAG；不帶就沿用草稿</summary>
        public List<string> hashtags { get; set; }
    }

    /// <summary>送出合成後的回應</summary>
    public class MerchantVlogTaskResponse
    {
        public int mm_id { get; set; }

        /// <summary>外部 AI 任務 ID</summary>
        public string task_id { get; set; }

        /// <summary>1=草稿、2=處理中、3=已完成、4=失敗</summary>
        public int status { get; set; }
        public string status_text { get; set; }
    }

    #endregion

    #region Status（影片、推薦配文、TAG）

    /// <summary>商家 VLOG 專案目前狀態；status=3 時 video_url 才有值</summary>
    public class MerchantVlogStatusResponse
    {
        public int mm_id { get; set; }

        /// <summary>1=草稿、2=處理中、3=已完成、4=失敗</summary>
        public int status { get; set; }
        public string status_text { get; set; }

        /// <summary>處理中（status=2）時 AI 目前的階段，例如「等待處理」「正在生成 XTTS 旁白」；其他狀態或 AI 暫時連不上時是 null</summary>
        public string stage { get; set; }

        public string title { get; set; }

        /// <summary>推薦配文</summary>
        public string caption { get; set; }

        /// <summary>推薦 TAG</summary>
        public List<string> hashtags { get; set; }

        /// <summary>完成的影片網址</summary>
        public string video_url { get; set; }

        public string thumbnail { get; set; }

        /// <summary>status=4 時的失敗原因</summary>
        public string error_message { get; set; }

        public DateTime updated_at { get; set; }
    }

    #endregion

    #region 外部 AI 服務原始回應（商家 / 遊客共用）

    /// <summary>外部 API /api/merchant/vlog/preview 的原始回應結構。</summary>
    public class MerchantVlogPreviewApiResponse
    {
        public string status { get; set; }
        public MerchantRetrievedData retrieved_data { get; set; }
        public MerchantDraftPreview draft_preview { get; set; }
    }

    public class MerchantRetrievedData
    {
        public string name { get; set; }
        public string description { get; set; }
        public string address { get; set; }
    }

    public class MerchantDraftPreview
    {
        /// <summary>AI 旁白草稿（AI 2026-10 起改用 suggested_script，原本叫 tw_script）</summary>
        public string suggested_script { get; set; }
        public List<string> seo_keywords { get; set; }
        public List<string> target_audience { get; set; }
        public string promo_copy { get; set; }
    }

    /// <summary>
    /// 送給外部 API /api/visitor/vlog/create_final 的 spot_meta_json 一筆：一張照片剛好一筆，陣列順序就是影片播放順序。
    /// AI 會逐一比對 file_name 和 image_zip 裡的檔名，有任何一張對不上整個請求就會被退回（HTTP 400）。
    /// </summary>
    public class VlogSpotMeta
    {
        /// <summary>照片在 image_zip 裡的檔名（含副檔名，區分大小寫）</summary>
        public string file_name { get; set; }

        /// <summary>影片左上角顯示的地點名稱，必填</summary>
        public string spot_name { get; set; }

        /// <summary>選填：景點或任務代號</summary>
        public string location_codename { get; set; }

        /// <summary>選填：造訪時間</summary>
        public string visit_time { get; set; }
    }

    /// <summary>
    /// 送給外部 API /api/visitor/vlog/create_final 的選填合成選項（已檢查、整理過）。
    /// null 的欄位不送，AI 用預設值：不參考感想、bgm_mood=auto 自動配樂、video_orientation=landscape 橫式
    /// </summary>
    public class VlogCreateOptions
    {
        public string user_memory_text { get; set; }
        public string bgm_mood { get; set; }
        public string video_orientation { get; set; }
    }

    /// <summary>外部 API /api/visitor/vlog/create_final 的原始回應結構（排隊模式，HTTP 202，status=queued）。</summary>
    public class VlogCreateFinalApiResponse
    {
        public string status { get; set; }
        public string task_id { get; set; }
        public string message { get; set; }
        public string check_url { get; set; }
    }

    /// <summary>外部 API /api/jobs/{task_id} 的原始回應結構（排隊中的任務查詢）。</summary>
    public class VlogJobApiResponse
    {
        /// <summary>AI 服務查不到任務（HTTP 404：已過期或 AI 服務重啟過）時，閘道填入的狀態</summary>
        public const string NotFound = "not_found";

        /// <summary>queued=排隊中、running=處理中、done=完成、failed=失敗</summary>
        public string status { get; set; }
        public string task_id { get; set; }

        /// <summary>目前階段，例如「等待處理」「正在生成 XTTS 旁白」「完成」</summary>
        public string stage { get; set; }

        /// <summary>排隊中時前面還有幾個任務（1＝下一個就輪到）；不在排隊時為 null</summary>
        public int? queue_position { get; set; }
        public VlogJobResult result { get; set; }

        /// <summary>失敗原因；AI 沒有固定格式，可能是字串或物件</summary>
        public System.Text.Json.JsonElement? error { get; set; }
    }

    public class VlogJobResult
    {
        public string filename { get; set; }
        public string download_url { get; set; }
        public string video_orientation { get; set; }
    }

    /// <summary>
    /// 外部 API /api/check_status/{task_id} 的原始回應結構：只看影片檔在不在（ready + download_url），
    /// 不認得的任務一律回 processing。AI 重啟後 /api/jobs 查不到任務時才用它確認影片是否已經做好。
    /// </summary>
    public class VlogCheckStatusApiResponse
    {
        public string status { get; set; }
        public string task_id { get; set; }
        public string filename { get; set; }
        public string download_url { get; set; }
    }

    #endregion
}
