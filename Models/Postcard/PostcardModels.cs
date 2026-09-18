using System;

namespace backend.Models
{
    // ===================== 明信片 (對應資料表 postcard) =====================
    public class PostcardResponse
    {
        public string postcard_id { get; set; }        // 明信片代號
        public string title { get; set; }                // 明信片標題
        public string subtitle { get; set; }               // 明信片副標題
        public string front_image_url { get; set; }         // AI 生成正面圖片網址
        public string back_photo_url { get; set; }            // 玩家拍攝的背面照片網址
        public string culture_note { get; set; }                // 文化解說內容
        public DateTime found_date { get; set; }                 // 獲得日期
        public bool is_night_edition { get; set; }                 // 是否為夜晚限定版
    }

    public class PostcardPrintRequest
    {
        public string postcard_id { get; set; }   // 欲列印的明信片代號
    }

    public class PostcardPrintResponse
    {
        public string ibon_pickup_code { get; set; }    // ibon 取件代碼 (pincode)
        public string pdf_url { get; set; }               // 生成的 PDF 網址
        public string deadline { get; set; }              // 列印期限時間
        public string qrcode_base64 { get; set; }         // 列印用 QRCode 的 Base64 編碼
    }

    public class PostcardShareRequest
    {
        public string postcard_id { get; set; }   // 欲分享的明信片代號
        public string platform { get; set; }        // 分享平台，例如 IG
    }
}
