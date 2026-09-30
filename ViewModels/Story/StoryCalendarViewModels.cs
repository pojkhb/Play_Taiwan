// 檔案路徑：System\ViewModels\Story\StoryCalendarViewModels.cs
using System;

namespace backend.ViewModels
{
    /// <summary>把劇本加入 Google 行事曆的請求</summary>
    public class StoryCalendarRequest
    {
        /// <summary>預計出發時間（台灣時間），例如 "2026-10-04T10:00:00"；不帶時預設明天早上 10:00（夜間劇本晚上 19:00）</summary>
        public DateTime? start_time { get; set; }

        /// <summary>預計遊玩多久（分鐘，30～1440）；不帶時依站數估算，每站 40 分鐘</summary>
        public int? duration_minutes { get; set; }
    }

    /// <summary>加入 Google 行事曆的結果</summary>
    public class StoryCalendarResponse
    {
        /// <summary>Google 行事曆的新增活動連結：前端直接用瀏覽器或 Google 日曆 App 打開，使用者按「儲存」即可</summary>
        public string google_calendar_url { get; set; }

        /// <summary>活動標題</summary>
        public string title { get; set; }

        /// <summary>開始時間（台灣時間）</summary>
        public DateTime start_time { get; set; }

        /// <summary>結束時間（台灣時間）</summary>
        public DateTime end_time { get; set; }

        /// <summary>活動地點：第一站（集合點）的名稱與地址；只放第一站，其他站還在迷霧中</summary>
        public string location { get; set; }

        /// <summary>活動說明：劇本簡介、地區、站數</summary>
        public string details { get; set; }
    }
}
