// 檔案路徑：System\Models\Postcard\PostcardModels.cs
// 對應新資料表 `postcard`。
// 舊的 md_postcard(主檔) + ep_postcard(擁有者) 在新資料庫已合併成這一張表，
// 每一列就是「某位使用者擁有的一張明信片」，所以 au_id 直接在本表上。
using System;

namespace backend.Models
{
    /// <summary>使用者的明信片，對應資料表 `postcard` 的一列。</summary>
    public class PostcardCatalog
    {
        public int p_id { get; set; }                  // 明信片流水號
        public int au_id { get; set; }                   // 擁有者，對應 auth.au_id
        public int? s_id { get; set; }                     // 對應劇本 story.s_id
        public int? sn_id { get; set; }                      // 取得時完成的節點 story_node.sn_id
        public string p_name { get; set; }                     // 明信片名稱
        public string p_summary { get; set; }                    // 明信片簡介
        public string p_imag_url { get; set; }                     // AI service 圖片網址
        /// <summary>是否為夜間模式：1=是、2=否（注意不是 0/1）。</summary>
        public int is_night { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
    }

    // ===================== 明信片 =====================
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

    /// <summary>ibon 列印請求，對應 PostcardCatalogController 的 Print。</summary>
    public class PrintPostcardRequest
    {
        public int postcard_id { get; set; }   // 欲列印的明信片 postcard.p_id
    }

    /// <summary>社群分享紀錄請求，對應 PostcardCatalogController 的 Share。</summary>
    public class StoryShareRequest
    {
        public int story_id { get; set; }      // 劇本 story.s_id
        public string platform { get; set; }     // 分享平台，例如 IG
    }
}
