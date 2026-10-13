using System;

namespace backend.Models
{
    /// <summary>目前位置的天氣（GET api/Map/Weather），地圖上 NPC 的天氣提醒用</summary>
    public class WeatherResponse
    {
        /// <summary>天氣現象，例如「晴」、「多雲」、「陰有雨」（氣象署測站的觀測用語）</summary>
        public string description { get; set; }

        /// <summary>
        /// 天氣的大類，前端換天氣圖示用：clear 晴、cloudy 多雲、overcast 陰、fog 霧（含靄、霾）、rain 雨、thunder 雷雨、snow 雪、unknown 不明
        /// </summary>
        public string condition { get; set; }

        /// <summary>氣溫（°C）</summary>
        public double temperature_c { get; set; }

        /// <summary>體感溫度（°C）：依氣溫、濕度、風速換算；缺濕度或風速時跟氣溫一樣</summary>
        public double apparent_temperature_c { get; set; }

        /// <summary>
        /// 接下來幾個小時的降雨機率（%）：氣象署是所在縣市目前這個預報時段的降雨機率（3 小時內會進到下一個時段的話，取兩個時段較高的），
        /// 備援來源是未來 3 小時內最高的；沒有資料時為 null
        /// </summary>
        public int? rain_probability { get; set; }

        /// <summary>現在是不是正在下雨</summary>
        public bool is_raining { get; set; }

        /// <summary>現在是不是白天</summary>
        public bool is_day { get; set; }

        /// <summary>
        /// 值得 NPC 主動提醒的狀況：thunder 打雷下雨、raining 正在下雨、rain_soon 等一下可能下雨（降雨機率 50% 以上）、
        /// hot 太熱（氣溫或體感 32°C 以上）、cold 偏涼（15°C 以下）；天氣沒什麼要特別提醒時為 null
        /// </summary>
        public string alert { get; set; }

        /// <summary>給玩家的提醒，例如「外面正在下雨，記得帶傘，路滑小心走」</summary>
        public string advice { get; set; }

        /// <summary>
        /// 可以直接放進 NPC 對話泡泡的一句話，例如
        /// 「目前天氣：陰有雨，氣溫 22°C，降雨機率 80%。外面正在下雨，記得帶傘，路滑小心走。」
        /// </summary>
        public string message { get; set; }

        /// <summary>這筆天氣的觀測時間（台灣時間）</summary>
        public DateTime observed_at { get; set; }

        /// <summary>觀測的氣象站名稱，例如「臺中」（離使用者最近、有資料的測站）；用備援來源時為 null</summary>
        public string station_name { get; set; }

        /// <summary>資料來源（畫面上要標示）：「中央氣象署」；氣象署查不到時改用備援的「Open-Meteo」</summary>
        public string source { get; set; }
    }
}
