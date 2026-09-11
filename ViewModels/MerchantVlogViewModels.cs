// 檔案路徑：System\ViewModels\MerchantVlogViewModels.cs
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace backend.ViewModels
{
    #region Preview（腳本預覽）

    /// <summary>
    /// 商家 Vlog 行銷預覽請求。對應畫面：店家名稱、敘事語氣、推薦資訊。
    /// </summary>
    public class MerchantVlogPreviewRequest
    {
        /// <summary>店家名稱，需與 Neo4j 裡的店家名稱一致，才能撈到官方介紹。</summary>
        public string merchant_name { get; set; }

        /// <summary>推薦資訊／優惠重點（畫面上的「推薦資訊」欄位）。</summary>
        public string promo_focus { get; set; }

        /// <summary>
        /// 敘事語氣（畫面上的「幽默詼諧 / 質感專業 / 溫情走心」）。
        /// 文件本身沒有獨立參數，這裡併進 promo_focus 一起送給外部 API 當作提示詞修飾。
        /// </summary>
        public string narration_tone { get; set; }
    }

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

    #endregion

    #region CreateFinal（正式合成影片）

    /// <summary>
    /// 商家 Vlog 正式合成影片請求。對應畫面：確認/微調後的旁白、上傳素材（多張照片）。
    /// </summary>
    public class MerchantVlogCreateFinalRequest
    {
        /// <summary>使用者確認或微調後的最終旁白文字（通常來自 Preview 回傳的 tw_script）。</summary>
        public string final_script { get; set; }

        /// <summary>畫面「上傳素材」欄位選取的多張照片，後端會即時打包成 zip 再送給外部 API。</summary>
        public List<IFormFile> images { get; set; }

        /// <summary>選填：圖片對應地點時間的中繼資料 JSON 字串。</summary>
        public string spot_meta_json { get; set; }
    }

    /// <summary>外部 API /api/visitor/vlog/create_final 的原始回應結構。</summary>
    public class VlogCreateFinalApiResponse
    {
        public string status { get; set; }
        public string task_id { get; set; }
        public string message { get; set; }
        public string check_url { get; set; }
    }

    #endregion

    #region 任務狀態查詢

    /// <summary>外部 API /api/check_status/{task_id} 的原始回應結構。</summary>
    public class VlogTaskStatusApiResponse
    {
        public string status { get; set; }
        public string task_id { get; set; }
        public string filename { get; set; }
        public string download_url { get; set; }
    }

    #endregion
}