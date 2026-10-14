using System.Collections.Generic;

namespace backend.Models
{
    /// <summary>
    /// 地圖上 NPC 的主動提醒（GET api/Map/Reminders）：後端判斷現在有沒有值得 NPC 自己跳出來講的事。
    /// 前端地圖開著時定時帶目前位置來問，有提醒才讓 NPC 跳出對話，沒有就不用動。
    /// </summary>
    public class NpcReminderResponse
    {
        /// <summary>建議隔多久再問一次（秒）</summary>
        public int check_after_seconds { get; set; }

        /// <summary>現在值得 NPC 主動講的事，天氣排在用餐前面；沒有就是空陣列（NPC 不用跳出來）</summary>
        public List<NpcReminder> reminders { get; set; } = new();
    }

    /// <summary>NPC 主動講的一件事</summary>
    public class NpcReminder
    {
        /// <summary>
        /// 這則提醒的代號，例如「weather-20261013-12-rain_soon」、「meal-20261013-lunch」。
        /// 前端記住已經跳過的代號，同一個代號不要再跳第二次（同一種天氣 3 小時內、同一餐一天內代號都一樣）
        /// </summary>
        public string key { get; set; }

        /// <summary>weather 天氣、meal 用餐</summary>
        public string type { get; set; }

        /// <summary>
        /// 細項。天氣：thunder 打雷下雨、raining 正在下雨、rain_soon 等一下可能下雨、hot 太熱、cold 偏涼；
        /// 用餐：breakfast 早餐、lunch 午餐、dinner 晚餐
        /// </summary>
        public string kind { get; set; }

        /// <summary>NPC 要講的話，直接放進對話泡泡</summary>
        public string message { get; set; }

        /// <summary>天氣提醒才有：目前的天氣（換圖示、顯示氣溫用）</summary>
        public WeatherResponse weather { get; set; }

        /// <summary>用餐提醒才有：推薦的店家（有優惠券的排前面，其餘由近到遠），可以直接接「前往」；其他提醒是空陣列</summary>
        public List<NearbyFoodPlace> places { get; set; } = new();
    }
}
