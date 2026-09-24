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
        /// <summary>明信片代號</summary>
        public int p_id { get; set; }

        /// <summary>擁有者的帳號代號</summary>
        public int au_id { get; set; }

        /// <summary>所屬劇本代號，沒有時為 null</summary>
        public int? s_id { get; set; }

        /// <summary>取得明信片時完成的節點代號，沒有時為 null</summary>
        public int? sn_id { get; set; }

        /// <summary>明信片名稱</summary>
        public string p_name { get; set; }

        /// <summary>明信片簡介</summary>
        public string p_summary { get; set; }

        /// <summary>明信片圖片網址（AI 生成）</summary>
        public string p_imag_url { get; set; }

        /// <summary>是否為夜間模式：1 = 是、2 = 否（注意不是 0/1）</summary>
        public int is_night { get; set; }

        /// <summary>建立時間</summary>
        public DateTime created_at { get; set; }

        /// <summary>最後更新時間</summary>
        public DateTime updated_at { get; set; }
    }

    // ===================== 明信片 =====================
    /// <summary>（舊版，尚未完成）明信片</summary>
    public class PostcardResponse
    {
        /// <summary>明信片代號</summary>
        public string postcard_id { get; set; }

        /// <summary>明信片標題</summary>
        public string title { get; set; }

        /// <summary>明信片副標題</summary>
        public string subtitle { get; set; }

        /// <summary>AI 生成的正面圖片網址</summary>
        public string front_image_url { get; set; }

        /// <summary>玩家拍攝的背面照片網址</summary>
        public string back_photo_url { get; set; }

        /// <summary>文化解說內容</summary>
        public string culture_note { get; set; }

        /// <summary>獲得日期</summary>
        public DateTime found_date { get; set; }

        /// <summary>是否為夜晚限定版</summary>
        public bool is_night_edition { get; set; }
    }

    public class PostcardPrintRequest
    {
        public string postcard_id { get; set; }   // 欲列印的明信片代號
    }

    /// <summary>ibon 列印結果</summary>
    public class PostcardPrintResponse
    {
        /// <summary>ibon 取件代碼，到 7-11 ibon 機台輸入即可列印</summary>
        public string ibon_pickup_code { get; set; }

        /// <summary>列印用的 PDF 網址</summary>
        public string pdf_url { get; set; }

        /// <summary>列印期限（超過就無法列印）</summary>
        public string deadline { get; set; }

        /// <summary>取件 QR Code 圖片（Base64 編碼，可直接當 img src 的 data URL 使用）</summary>
        public string qrcode_base64 { get; set; }
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
