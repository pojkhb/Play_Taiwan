// 檔案路徑：System\Services\Story\ItineraryRules.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>
    /// 行程規劃的規則。都是不碰資料庫、不連外的純計算（StoryService 負責查資料與呼叫 Valhalla）：
    /// 1. 等時圈：玩家選「最遠願意花幾分鐘」，切成近、中、遠三圈
    /// 2. 抵達時間：依出發時間、車程與每站停留時間，估算每一站幾點到
    /// 3. 依抵達時間調整景點：到的時候沒開的景點換掉；天黑後才到的站改挑適合晚上的景點，只適合晚上的景點（夜市）不排在白天
    /// </summary>
    public static class ItineraryRules
    {
        #region 等時圈分鐘數

        /// <summary>玩家沒選時，最遠的那一圈是幾分鐘（跟原本固定的 10／20／30 一樣）</summary>
        public const int DefaultMaxMinutes = 30;
        public const int MinMaxMinutes = 10;
        public const int MaxMaxMinutes = 60;

        /// <summary>範圍內景點少於這個數量就不生成，請玩家把時間調長或換交通方式</summary>
        public const int MinReachablePlaces = 3;

        /// <summary>
        /// 最遠願意花的分鐘數 → 三圈的分鐘數（三分之一、三分之二、全部）。
        /// 例如 30 → 10、20、30；45 → 15、30、45。沒帶（null 或 0）用預設 30，超出 10～60 會收進範圍內。
        /// </summary>
        public static List<int> ContourMinutes(int? maxMinutes)
        {
            int max = maxMinutes is int m && m > 0 ? Math.Clamp(m, MinMaxMinutes, MaxMaxMinutes) : DefaultMaxMinutes;
            return new List<int> { (int)Math.Round(max / 3.0), (int)Math.Round(max * 2 / 3.0), max };
        }

        #endregion

        #region 抵達時間

        /// <summary>每一站預估停留幾分鐘（看劇情、做任務），跟加入行事曆時估算遊玩時間用同一個數字</summary>
        public const int StayMinutesPerStop = 40;

        /// <summary>幾點以後算天黑、幾點以前算還沒天亮</summary>
        public const int NightStartHour = 18;
        public const int NightEndHour = 5;

        public static bool IsNight(DateTime time) => time.Hour >= NightStartHour || time.Hour < NightEndHour;

        // 步行大約每分鐘 75 公尺，換成直線距離（實際要繞路）約 60；只在沒有任何真實交通時間可以參考時用
        private const double WalkingMetersPerMinute = 60;

        /// <summary>
        /// 這趟行程的平均移動速度（直線距離的公尺／分鐘）：用 Valhalla 算出的「出發點到各景點的真實交通時間」反推，
        /// 所以已經包含玩家選的交通方式、當地路況與繞路的影響。
        /// </summary>
        public static double AverageSpeed(IEnumerable<ReachableAttractionNode> reachable)
        {
            double meters = 0, minutes = 0;

            foreach (ReachableAttractionNode a in reachable.Where(a => a.distance_m > 0 && a.travel_minutes > 0))
            {
                meters += a.distance_m;
                minutes += a.travel_minutes;
            }

            return meters > 0 && minutes > 0 ? meters / minutes : WalkingMetersPerMinute;
        }

        /// <summary>
        /// 一段路要幾分鐘。從出發點到景點（from 為 null）直接用 Valhalla 算好的真實交通時間；
        /// 景點到景點用直線距離除以平均速度估算。
        /// </summary>
        public static double LegMinutes(ReachableAttractionNode from, ReachableAttractionNode to, double metersPerMinute)
        {
            if (from == null) return to.travel_minutes;
            return Geo.DistanceMeters(from.lat, from.lon, to.lat, to.lon) / metersPerMinute;
        }

        #endregion

        #region 營業時間

        /// <summary>一個營業時段。跨夜（例如 18:00～02:00）時 close 會比 open 小；open 等於 close 當作全天</summary>
        public readonly record struct OpenHours(TimeSpan open, TimeSpan close);

        /// <summary>到的時候有開還不夠，至少要還能再待這麼久才排進去</summary>
        public const int MinOpenMinutes = 30;

        /// <summary>
        /// 把 Neo4j 的營業時間（"HH:mm" 或 "HH:mm:ss"）轉成時段；看不懂的那一筆略過。"24:00" 當成隔天 00:00。
        /// 不分星期幾：一週裡任何一天的時段都算。
        /// </summary>
        public static List<OpenHours> ParseHours(IEnumerable<(string open, string close)> raw)
        {
            var hours = new List<OpenHours>();

            foreach (var (open, close) in raw ?? Enumerable.Empty<(string, string)>())
            {
                if (TryParseTime(open, out TimeSpan o) && TryParseTime(close, out TimeSpan c))
                    hours.Add(new OpenHours(o, c));
            }

            return hours.Distinct().ToList();
        }

        private static readonly Regex TimeRange = new(@"(\d{1,2})[:：](\d{2})\s*[-~～－–至到]\s*(\d{1,2})[:：](\d{2})");

        /// <summary>
        /// 把 MySQL 的營業時間文字（例如「09:00-17:00」「11:00-14:00、17:00-21:00」「24小時」）轉成時段；看不懂時回傳空的。
        /// </summary>
        public static List<OpenHours> ParseHoursText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<OpenHours>();
            if (text.Contains("24小時") || text.Contains("24 小時"))
                return new List<OpenHours> { new(TimeSpan.Zero, TimeSpan.Zero) };

            return ParseHours(TimeRange.Matches(text)
                .Select(m => ($"{m.Groups[1].Value}:{m.Groups[2].Value}", $"{m.Groups[3].Value}:{m.Groups[4].Value}")));
        }

        private static bool TryParseTime(string text, out TimeSpan time)
        {
            time = default;
            string[] parts = (text ?? "").Trim().Split(':', '：');
            if (parts.Length < 2 || !int.TryParse(parts[0], out int hour) || !int.TryParse(parts[1], out int minute)) return false;
            if (hour < 0 || hour > 24 || minute < 0 || minute > 59) return false;

            time = new TimeSpan(hour % 24, minute, 0);
            return true;
        }

        /// <summary>這個時間有沒有開</summary>
        public static bool IsOpenAt(IEnumerable<OpenHours> hours, DateTime time)
        {
            TimeSpan t = time.TimeOfDay;

            return hours.Any(h =>
                h.open == h.close ? true                            // 全天
                : h.open < h.close ? t >= h.open && t < h.close     // 一般
                : t >= h.open || t < h.close);                      // 跨夜
        }

        #endregion

        #region 景點排在這個時間合不合適

        // 名稱裡有這些字：只適合晚上去，白天不排
        private static readonly string[] NightOnlyKeywords = { "夜市", "夜景", "星空", "賞星", "螢火蟲" };

        // 名稱裡有這些字：晚上去也適合
        private static readonly string[] NightFriendlyKeywords =
        {
            "觀景", "觀夕", "夕陽", "夕照", "老街", "商圈", "徒步區", "廣場", "百貨", "碼頭", "河岸", "河濱", "水岸",
            "溫泉", "光廊", "摩天輪", "電影", "劇場", "酒吧"
        };

        /// <summary>
        /// 這個景點排在這個抵達時間合不合適。
        /// 有營業時間資料：到的時候有開，而且至少還會再開 30 分鐘。
        /// 沒有營業時間資料（多數戶外景點）：改看名稱——天黑後要是適合晚上去的（夜市、觀景台、老街、商圈這類），
        /// 白天則不能是只適合晚上的（夜市、夜景這類）。
        /// </summary>
        public static bool Fits(string name, IReadOnlyCollection<OpenHours> hours, DateTime arrival)
        {
            if (hours != null && hours.Count > 0)
                return IsOpenAt(hours, arrival) && IsOpenAt(hours, arrival.AddMinutes(MinOpenMinutes));

            return IsNight(arrival)
                ? ContainsAny(name, NightOnlyKeywords) || ContainsAny(name, NightFriendlyKeywords)
                : !ContainsAny(name, NightOnlyKeywords);
        }

        private static bool ContainsAny(string name, string[] keywords) =>
            !string.IsNullOrWhiteSpace(name) && keywords.Any(name.Contains);

        #endregion

        #region 依抵達時間調整景點

        /// <summary>排好的一站：景點、預計幾點抵達</summary>
        public record PlannedStop(ReachableAttractionNode place, DateTime arrival)
        {
            /// <summary>是不是天黑後才到的站</summary>
            public bool is_night => IsNight(arrival);
        }

        /// <summary>
        /// 照排好的順序一站一站往下走，估算每一站幾點到，不合適的站換掉：
        /// 1. 抵達時間 ＝ 上一站抵達 ＋ 停留 40 分鐘 ＋ 車程（第一站 ＝ 出發時間 ＋ 車程）
        /// 2. 這一站合適（見 Fits）就照原本的景點；不合適就從 spare（範圍內這次沒被排進任何一份劇本的景點）裡，
        ///    挑一個「依它自己的抵達時間算是合適」而且離上一站最近的來換，行程才不會繞路
        /// 3. spare 裡沒有合適的就維持原本的景點（景點不夠時，寧可照舊也不要少一站）
        /// 用掉的景點會從 spare 移除，被換下來的放回 spare，後面的站或其他份劇本還可以用。
        /// 回傳的站數、順序與傳入的相同。
        /// </summary>
        public static List<PlannedStop> ArrangeByArrival(
            IReadOnlyList<ReachableAttractionNode> ordered, List<ReachableAttractionNode> spare,
            IReadOnlyDictionary<string, List<OpenHours>> hours, DateTime start, double metersPerMinute)
        {
            var stops = new List<PlannedStop>();
            HashSet<string> inThisStory = ordered.Select(a => a.uid).ToHashSet();

            ReachableAttractionNode previous = null;
            DateTime clock = start.AddTicks(-(start.Ticks % TimeSpan.TicksPerMinute));   // 抵達時間只算到分鐘

            DateTime ArrivalAt(ReachableAttractionNode place) =>
                clock.AddMinutes(Math.Max(0, Math.Round(LegMinutes(previous, place, metersPerMinute))));

            bool FitsHere(ReachableAttractionNode place) =>
                Fits(place.name, place.uid != null ? hours.GetValueOrDefault(place.uid) : null, ArrivalAt(place));

            foreach (ReachableAttractionNode planned in ordered)
            {
                ReachableAttractionNode stop = planned;

                if (!FitsHere(stop))
                {
                    ReachableAttractionNode from = previous;
                    ReachableAttractionNode replacement = spare
                        .Where(c => !inThisStory.Contains(c.uid) && FitsHere(c))
                        .OrderBy(c => LegMinutes(from, c, metersPerMinute))
                        .FirstOrDefault();

                    if (replacement != null)
                    {
                        spare.Remove(replacement);
                        spare.Add(stop);
                        inThisStory.Remove(stop.uid);
                        inThisStory.Add(replacement.uid);
                        stop = replacement;
                    }
                }

                DateTime arrival = ArrivalAt(stop);
                stops.Add(new PlannedStop(stop, arrival));

                previous = stop;
                clock = arrival.AddMinutes(StayMinutesPerStop);
            }

            return stops;
        }

        #endregion
    }
}
