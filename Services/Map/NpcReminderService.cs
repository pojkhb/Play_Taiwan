// 檔案路徑：System\Services\Map\NpcReminderService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    /// <summary>
    /// 地圖上 NPC 的主動提醒（GET api/Map/Reminders）：由後端判斷現在有沒有值得 NPC 自己跳出來講的事，
    /// 前端地圖開著時定時帶目前位置來問，有提醒才讓 NPC 跳出對話。
    /// 1. 天氣（WeatherService）：打雷下雨、正在下雨、等一下可能下雨、太熱、偏涼才提醒；天氣好就不講
    /// 2. 用餐（NearbyFoodService）：到了早餐、午餐、晚餐時間，而且附近找得到店，才跳出來推薦去哪裡吃
    /// 每則提醒有一個代號（key）：同一種天氣 3 小時內、同一餐一天內代號都一樣，前端跳過一次就不再跳，不會一直吵。
    /// </summary>
    public class NpcReminderService
    {
        /// <summary>建議前端隔多久再問一次（秒）</summary>
        public const int CheckAfterSeconds = 300;

        /// <summary>用餐提醒最多推薦幾間</summary>
        public const int MealPlaces = 5;

        /// <summary>同一種天氣提醒隔幾個小時才會再跳一次</summary>
        private const int WeatherRepeatHours = 3;

        private readonly WeatherService _weather;
        private readonly NearbyFoodService _food;
        private readonly NearbyFoodDao _dao;
        private readonly TaiwanClock _clock;

        public NpcReminderService(WeatherService weather, NearbyFoodService food, NearbyFoodDao dao, TaiwanClock clock)
        {
            _weather = weather;
            _food = food;
            _dao = dao;
            _clock = clock;
        }

        /// <param name="storyId">正在玩的劇本；有帶的話，推薦的店家會排除這份劇本自己的站（還在迷霧中的站不能從這裡洩漏）</param>
        public async Task<NpcReminderResponse> GetAsync(double lat, double lng, int? storyId)
        {
            if (lat < -90 || lat > 90 || lng < -180 || lng > 180 || (lat == 0 && lng == 0))
                throw new BadRequestException("請帶目前位置的經緯度（lat、lng）");

            DateTime taiwanNow = _clock.Now;
            var reminders = new List<NpcReminder>();

            try
            {
                NpcReminder weather = WeatherReminder(await _weather.GetCurrentAsync(lat, lng), taiwanNow);
                if (weather != null) reminders.Add(weather);
            }
            catch (Exception)
            {
                // 天氣由外部服務提供，查不到（WeatherService 已經記錄原因）就當作沒有天氣提醒，用餐提醒照常
            }

            if (MealTimeOf(taiwanNow) is (string kind, string meal))
            {
                NearbyFoodResponse food = await _food.GetNearbyFoodAsync(lat, lng, null, NearbyFoodService.MaxLimit);
                List<NearbyFoodDao.StoryPlaceRow> storyPlaces = storyId.HasValue
                    ? await _dao.GetStoryPlacesAsync(storyId.Value)
                    : new List<NearbyFoodDao.StoryPlaceRow>();

                List<NearbyFoodPlace> places = food.places
                    .Where(p => !NearbyPlaceService.IsStoryPlace(p, storyPlaces))
                    .Take(MealPlaces)
                    .ToList();

                // 附近沒有店就不跳；玩家走到有店的地方，下一次問的時候才跳
                if (places.Count > 0) reminders.Add(MealReminder(kind, meal, places, food.radius_m, taiwanNow));
            }

            return new NpcReminderResponse { check_after_seconds = CheckAfterSeconds, reminders = reminders };
        }

        #region 判斷（純函式，單元測試用）

        /// <summary>
        /// 現在是不是用餐時間（台灣時間）：早餐 07:00–09:00、午餐 11:30–13:30、晚餐 17:30–19:30。
        /// 回傳（代號, 餐別名稱）；不是用餐時間回傳 null
        /// </summary>
        internal static (string kind, string meal)? MealTimeOf(DateTime taiwanNow) => (taiwanNow.Hour * 60 + taiwanNow.Minute) switch
        {
            >= 7 * 60 and < 9 * 60 => ("breakfast", "早餐"),
            >= 11 * 60 + 30 and < 13 * 60 + 30 => ("lunch", "午餐"),
            >= 17 * 60 + 30 and < 19 * 60 + 30 => ("dinner", "晚餐"),
            _ => null
        };

        /// <summary>天氣有值得提醒的狀況（alert）才有提醒；天氣好回傳 null</summary>
        internal static NpcReminder WeatherReminder(WeatherResponse weather, DateTime taiwanNow)
        {
            if (weather?.alert == null) return null;

            int block = taiwanNow.Hour / WeatherRepeatHours * WeatherRepeatHours;
            return new NpcReminder
            {
                key = $"weather-{taiwanNow:yyyyMMdd}-{block:00}-{weather.alert}",
                type = "weather",
                kind = weather.alert,
                message = weather.message,
                weather = weather
            };
        }

        /// <summary>用餐提醒：NPC 講的話跟附近美食（NearbyFood）的 message 同一套，推薦第一間、後面幾間帶過</summary>
        internal static NpcReminder MealReminder(string kind, string meal, List<NearbyFoodPlace> places, int radiusMeters, DateTime taiwanNow) => new()
        {
            key = $"meal-{taiwanNow:yyyyMMdd}-{kind}",
            type = "meal",
            kind = kind,
            message = NearbyFoodService.BuildMessage(meal, places, radiusMeters),
            places = places
        };

        #endregion
    }
}
