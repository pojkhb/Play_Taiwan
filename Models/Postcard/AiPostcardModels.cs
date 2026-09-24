using Microsoft.AspNetCore.Http; /* 讓系統認識 IFormFile */
using System.Text.Json.Serialization; /* 讓系統認識 JsonPropertyName */
namespace backend.Models
{
    /* 接收前端 APP 傳來的生成明信片請求 (對應 Swagger 表單格式) */
    public class AiPostcardGenerateRequest
    {
        public IFormFile user_image { get; set; } /* 必須使用 IFormFile 接收前端上傳的圖片檔案 */
        public string spot_name { get; set; } /* 景點名稱 (例如：台北101) */
        public string user_prompt { get; set; } /* 使用者輸入的提示詞 (例如：復古水墨風) */
        public int? story_id { get; set; } /* 額外傳遞的劇本代號 story.s_id，方便後端寫入 DB 時關聯 */
        public int? node_id { get; set; } /* 取得明信片的劇本節點 story_node.sn_id */
        public bool is_night_edition { get; set; }
    }

    /* 解析外部 AI Service 回傳的 JSON 結構 */
    public class AiPostcardApiResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } /* 回傳狀態 (例如：success) */

        [JsonPropertyName("task_id")]
        public string TaskId { get; set; } /* 任務識別碼 (例如：d1863bc6) */

        [JsonPropertyName("postcard_introduction")]
        public string PostcardIntroduction { get; set; } /* AI 生成的明信片介紹說明文字 */

        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; } /* AI 生成的明信片圖片下載網址 */
    }
}
