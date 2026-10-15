// 檔案路徑：System\ViewModels\Npc\NpcViewModels.cs
// NPC 語音（劇情前傳、節點對話）：轉接外部 AI 服務 POST /api/npc/speak（文字轉語音，排隊模式），
// 送出後用 GET /api/jobs/{task_id} 查進度。
using System.Text.Json;

namespace backend.ViewModels
{
    public class NpcSpeakRequest
    {
        /// <summary>要唸的文字（必填，最多 1000 字）</summary>
        public string text { get; set; }

        /// <summary>
        /// 發聲的 NPC，不帶時用薯光。
        /// 可用：薯光、珍奶奶、阿達力、墨先生、霓霓、阿吉伯（劇本詳情的 npc.name）
        /// </summary>
        public string npc_name { get; set; }
    }

    /// <summary>NPC 語音的進度（前端每隔幾秒查一次，直到 completed 或 failed）</summary>
    public class NpcSpeakJobResponse
    {
        /// <summary>任務 ID，拿去查進度</summary>
        public string task_id { get; set; }

        /// <summary>waiting＝等待中、processing＝生成中、completed＝完成、failed＝失敗</summary>
        public string status { get; set; }

        /// <summary>狀態的中文：等待中／生成中／完成／失敗</summary>
        public string status_text { get; set; }

        /// <summary>AI 服務給的目前階段說明，例如「等待處理」</summary>
        public string stage { get; set; }

        /// <summary>等待中時排在第幾位；其他狀態為 null</summary>
        public int? queue_position { get; set; }

        /// <summary>失敗原因；沒有失敗時為 null</summary>
        public string message { get; set; }

        /// <summary>語音檔（mp3）完整網址，前端直接播放；還沒完成時為 null</summary>
        public string audio_url { get; set; }
    }

    /// <summary>送出 NPC 語音後的回應：已排入 AI 佇列，拿 task_id 查進度</summary>
    public class NpcSpeakResponse : NpcSpeakJobResponse
    {
        /// <summary>實際唸的文字</summary>
        public string text { get; set; }

        /// <summary>發聲的 NPC</summary>
        public string npc_name { get; set; }
    }

    /// <summary>
    /// 外部 AI 的原始回應：POST /api/npc/speak 的 202（排入佇列）、GET /api/jobs/{task_id} 都是這個格式。
    /// AI 文件沒有定義欄位，以下照實際收到的回應寫。
    /// </summary>
    public class NpcSpeakApiResponse
    {
        public string task_id { get; set; }

        /// <summary>任務種類，語音是 npc_tts</summary>
        public string task_type { get; set; }

        /// <summary>目前只確認過 queued；其他狀態字串見 NpcVoiceService.ParseState</summary>
        public string status { get; set; }

        public string stage { get; set; }

        /// <summary>Unix 時間（秒）</summary>
        public double? created_at { get; set; }
        public double? updated_at { get; set; }

        /// <summary>完成後的結果，還沒完成時為 null</summary>
        public JsonElement? result { get; set; }

        /// <summary>失敗原因（字串或物件），沒有失敗時為 null</summary>
        public JsonElement? error { get; set; }

        /// <summary>查進度的相對路徑：/api/jobs/{task_id}</summary>
        public string check_url { get; set; }

        public int? queue_position { get; set; }
    }
}
