using System;
using System.Text;

namespace backend.util
{
    /// <summary>
    /// Google 行事曆的「新增活動」連結：用瀏覽器或 Google 日曆 App 打開後，標題、時間、地點、說明都已填好，
    /// 使用者按「儲存」就會加進自己的行事曆。不需要 Google 帳號授權，也不需要 API 金鑰。
    /// </summary>
    public static class GoogleCalendarLink
    {
        public const string TaiwanTimeZone = "Asia/Taipei";

        /// <param name="start">開始時間（台灣時間）</param>
        /// <param name="end">結束時間（台灣時間）</param>
        public static string Build(string title, DateTime start, DateTime end, string details, string location)
        {
            var url = new StringBuilder("https://calendar.google.com/calendar/render?action=TEMPLATE");
            url.Append("&text=").Append(Uri.EscapeDataString(title ?? ""));
            // 時間不帶 Z，配合 ctz 表示是台灣時間
            url.Append("&dates=").Append(start.ToString("yyyyMMdd'T'HHmmss")).Append('/').Append(end.ToString("yyyyMMdd'T'HHmmss"));
            url.Append("&ctz=").Append(Uri.EscapeDataString(TaiwanTimeZone));
            if (!string.IsNullOrWhiteSpace(details))
                url.Append("&details=").Append(Uri.EscapeDataString(details));
            if (!string.IsNullOrWhiteSpace(location))
                url.Append("&location=").Append(Uri.EscapeDataString(location));
            return url.ToString();
        }
    }
}
