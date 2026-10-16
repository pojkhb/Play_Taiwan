// 檔案路徑：System\ViewModels\Npc\NpcViewModels.cs
// NPC 語音（劇情前傳、節點對話、回顧旁白）：轉接外部 AI 服務 POST /api/npc/speak（XTTS，用 NPC 名字挑聲音）。
// AI 是排隊模式：送出後先拿到 job_id，語音做好前用 GET /api/Npc/Speak/{job_id} 查進度。
namespace backend.ViewModels
{
    public class NpcSpeakRequest
    {
        /// <summary>要唸的文字（必填，最多 1000 字）</summary>
        public string text { get; set; }

        /// <summary>由哪位 NPC 唸，例如「墨先生」（選項由 GET /api/Npc/Options 取得）；不帶時用預設的薯光</summary>
        public string npc_name { get; set; }
    }

    /// <summary>語音產生進度：done 直接播放 audio_url；processing 時用 GET /api/Npc/Speak/{job_id} 查進度；failed 時看 error</summary>
    public class NpcSpeakResponse
    {
        public const string Done = "done";
        public const string Processing = "processing";
        public const string Failed = "failed";

        /// <summary>done＝做好了；processing＝排隊或生成中；failed＝失敗（使用者再按一次播放就會重新送出）</summary>
        public string status { get; set; }

        /// <summary>查進度用的代號；同一位 NPC 唸同一段文字永遠是同一個 job_id</summary>
        public string job_id { get; set; }

        /// <summary>目前進度，例如「等待處理」「正在生成 XTTS 語音」；processing 才有</summary>
        public string stage { get; set; }

        /// <summary>前面還有幾個 AI 任務在排隊（1＝下一個就輪到）；不在排隊時為 null</summary>
        public int? queue_position { get; set; }

        /// <summary>語音檔（wav）完整網址，前端直接播放；done 才有</summary>
        public string audio_url { get; set; }

        /// <summary>失敗原因；failed 才有</summary>
        public string error { get; set; }
    }

    /// <summary>NPC 下拉選單的一個選項</summary>
    public class NpcOptionItem
    {
        /// <summary>送給語音 API 的 npc_name，例如「墨先生」</summary>
        public string value { get; set; }

        /// <summary>選單上顯示的文字</summary>
        public string label { get; set; }

        /// <summary>NPC 身分／角色設定，可以當選項下方的小字</summary>
        public string description { get; set; }

        /// <summary>NPC 圖片（完整網址）</summary>
        public string avatar_url { get; set; }

        /// <summary>預設選項（薯光）：Speak 不帶 npc_name 時由它唸</summary>
        public bool is_default { get; set; }
    }
}
