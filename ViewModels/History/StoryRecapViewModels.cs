// 檔案路徑：System\ViewModels\History\StoryRecapViewModels.cs
// 劇本回顧：GET /api/History/{story_id}/Recap（一次回傳整趟旅程）、POST .../Recap/Narration（旁白語音）
using System;
using System.Collections.Generic;

namespace backend.ViewModels
{
    /// <summary>劇本回顧：玩完的劇本一次拿到劇情、每一站、收穫、統計與旁白</summary>
    public class StoryRecapResponse
    {
        public int story_id { get; set; }
        public string title { get; set; }

        /// <summary>劇情前傳</summary>
        public string prologue { get; set; }

        /// <summary>劇本簡介</summary>
        public string synopsis { get; set; }

        public string city_name { get; set; }
        public string district_name { get; set; }
        public bool is_night_mode { get; set; }

        public DateTime? started_at { get; set; }
        public DateTime? completed_at { get; set; }

        /// <summary>從開始到完成總共幾分鐘；缺少時間紀錄時為 null</summary>
        public int? play_minutes { get; set; }

        public StoryRecapStats stats { get; set; }

        /// <summary>每一站（依順序）。玩完了迷霧都已散開，會顯示真正的景點</summary>
        public List<StoryRecapNode> nodes { get; set; }

        /// <summary>這趟拿到的明信片</summary>
        public List<StoryRecapPostcard> postcards { get; set; }

        /// <summary>這趟抽到的勳章；還沒抽時為 null</summary>
        public StoryRecapBadge badge { get; set; }

        /// <summary>這趟的 Vlog；還沒做時為 null</summary>
        public StoryRecapVlog vlog { get; set; }

        /// <summary>回顧旁白：文字直接給；語音要按播放時再呼叫 POST /api/History/{story_id}/Recap/Narration</summary>
        public StoryRecapNarration narration { get; set; }
    }

    public class StoryRecapStats
    {
        /// <summary>總站數</summary>
        public int node_count { get; set; }

        /// <summary>作答過的任務數</summary>
        public int tasks_answered { get; set; }

        /// <summary>答對的任務數</summary>
        public int tasks_correct { get; set; }

        /// <summary>玩家拍的照片數</summary>
        public int photo_count { get; set; }

        /// <summary>拿到的明信片數</summary>
        public int postcard_count { get; set; }
    }

    public class StoryRecapNode
    {
        public int node_id { get; set; }
        public int order { get; set; }

        /// <summary>章節標題，例如「公園裡的希臘神殿」</summary>
        public string chapter_title { get; set; }

        /// <summary>地點代號（謎題），例如「白牆藍瓦」</summary>
        public string location_codename { get; set; }

        /// <summary>真正的景點名稱，例如「國立臺灣博物館」</summary>
        public string place_name { get; set; }

        /// <summary>景點照片</summary>
        public string place_image_url { get; set; }

        /// <summary>到站時的劇情</summary>
        public string opening_text { get; set; }

        /// <summary>完成這站時的劇情</summary>
        public string success_text { get; set; }

        /// <summary>玩家在這站拍的照片</summary>
        public List<string> photos { get; set; }

        /// <summary>這站的任務與作答結果</summary>
        public List<StoryRecapTask> tasks { get; set; }
    }

    public class StoryRecapTask
    {
        public int task_id { get; set; }

        /// <summary>任務類型，例如「文化問答型」</summary>
        public string type_name { get; set; }

        /// <summary>題目</summary>
        public string question { get; set; }

        /// <summary>是否作答過</summary>
        public bool answered { get; set; }

        /// <summary>是否答對；沒有對錯的任務類型（例如拍照）或沒作答時為 null</summary>
        public bool? is_correct { get; set; }
    }

    public class StoryRecapPostcard
    {
        public int postcard_id { get; set; }
        public string name { get; set; }
        public string image_url { get; set; }
        public bool is_night_edition { get; set; }
    }

    public class StoryRecapBadge
    {
        public int badge_id { get; set; }
        public string name { get; set; }

        /// <summary>系列，前面帶圖示，例如「🌙 午夜台灣」</summary>
        public string series { get; set; }

        /// <summary>勳章圖片（完整網址）</summary>
        public string image_url { get; set; }
    }

    public class StoryRecapVlog
    {
        public int vlog_id { get; set; }

        /// <summary>1=待處理、2=處理中、3=已完成、4=失敗</summary>
        public int status { get; set; }

        /// <summary>影片網址，完成後才有</summary>
        public string video_url { get; set; }

        public string thumbnail_url { get; set; }
    }

    public class StoryRecapNarration
    {
        /// <summary>旁白來源：vlog＝玩家確認過的 Vlog 旁白；story＝依各站劇情自動組成的回顧旁白</summary>
        public string source { get; set; }

        /// <summary>旁白文字（最多 1000 字）</summary>
        public string text { get; set; }

        /// <summary>旁白語音（mp3）；還沒產生過時為 null，按播放時呼叫 POST .../Recap/Narration 產生</summary>
        public string audio_url { get; set; }
    }

    /// <summary>產生旁白語音的請求（body 可省略）</summary>
    public class StoryRecapNarrationRequest
    {
        /// <summary>聲音，例如 zh-TW-HsiaoChenNeural（預設）、zh-TW-YunJheNeural</summary>
        public string voice { get; set; }
    }
}
