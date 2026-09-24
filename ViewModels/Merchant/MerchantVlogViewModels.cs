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
        public string tw_script { get; set; }
        public string en_video_prompt { get; set; }
        public List<string> seo_keywords { get; set; }
        public List<string> target_audience { get; set; }
        public string promo_copy { get; set; }
    }

    /// <summary>外部 API /api/visitor/vlog/create_final 的原始回應結構。</summary>
    public class VlogCreateFinalApiResponse
    {
        public string status { get; set; }
        public string task_id { get; set; }
        public string message { get; set; }
        public string check_url { get; set; }
    }

    /// <summary>外部 API /api/check_status/{task_id} 的原始回應結構。</summary>
    public class VlogTaskStatusApiResponse
    {
        public string status { get; set; }
        public string task_id { get; set; }
        public string filename { get; set; }
        public string download_url { get; set; }
        public string message { get; set; }
    }

    #endregion
}
