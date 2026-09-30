// 檔案路徑：System\ViewModels\Npc\NpcViewModels.cs
// NPC 語音（劇情前傳、節點對話）：轉接外部 AI 服務 POST /api/npc/speak（文字轉語音）。
namespace backend.ViewModels
{
    public class NpcSpeakRequest
    {
        /// <summary>要唸的文字（必填，最多 1000 字）</summary>
        public string text { get; set; }

        /// <summary>
        /// 聲音，不帶時用 zh-TW-HsiaoChenNeural（女聲）。
        /// 其他可用：zh-TW-YunJheNeural（男聲）、zh-TW-HsiaoYuNeural（女聲）
        /// </summary>
        public string voice { get; set; }
    }

    public class NpcSpeakResponse
    {
        /// <summary>實際唸的文字</summary>
        public string text { get; set; }

        public string voice { get; set; }

        /// <summary>語音檔（mp3）完整網址，前端直接播放</summary>
        public string audio_url { get; set; }
    }

    /// <summary>外部 AI /api/npc/speak 的原始回應</summary>
    public class NpcSpeakApiResponse
    {
        public string status { get; set; }
        public string task_id { get; set; }
        public string download_url { get; set; }
        public string message { get; set; }
    }
}
