// 檔案路徑：System\Services\Merchant\OperatingHoursRules.cs
// 商家填的營業時間：檢查格式並整理成 Neo4j (:OperatingHours) 節點的寫法（跟政府開放資料一致），
// 寫入後景點資訊的 operating_hours 就能直接讀到，前端不用分政府景點、商家景點兩種處理。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using backend.Models;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>
    /// Neo4j 營業時間的格式（政府開放資料）：一個時段一顆節點，dayOfWeek 為 Monday～Sunday，openTime / closeTime 為 "HH:mm"；
    /// 跨夜營業 closeTime 比 openTime 早（例如 18:00～02:00，營業到半夜 12 點是 00:00），全天營業為 00:00～23:59，
    /// 公休日沒有節點，同一天可以有多個時段（例如中午休息）。
    /// </summary>
    public static class OperatingHoursRules
    {
        /// <summary>Neo4j 的星期寫法，也是排序順序</summary>
        private static readonly string[] Days = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        /// <summary>同一天最多幾個營業時段</summary>
        public const int MaxSegmentsPerDay = 4;

        // 只收半形數字（\d 會吃到全形數字，int.Parse 轉不了）
        private static readonly Regex TimePattern = new(@"^([0-9]{1,2}):([0-9]{2})$");

        /// <summary>
        /// 檢查並整理營業時間：星期統一成首字大寫的英文、時間補成兩位數，依星期與開始時間排序。
        /// 沒填回傳空清單（不建立營業時間）；格式不對、同一天時段重疊時丟 BadRequestException。
        /// </summary>
        public static List<PlaceOperatingHourItem> Normalize(List<PlaceOperatingHourItem> items)
        {
            var segments = new List<(int day, int open, int close)>();
            if (items == null) return new List<PlaceOperatingHourItem>();

            for (int i = 0; i < items.Count; i++)
            {
                string field = $"operating_hours[{i}]";
                PlaceOperatingHourItem item = items[i] ?? throw new BadRequestException($"{field} 不可為空");

                int day = Array.FindIndex(Days, d => string.Equals(d, item.day_of_week?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (day < 0)
                {
                    throw new BadRequestException($"{field}.day_of_week 只能是 {string.Join("、", Days)}");
                }

                int open = ParseMinutes(item.open_time, $"{field}.open_time");
                int close = ParseMinutes(item.close_time, $"{field}.close_time");
                if (open == close)
                {
                    throw new BadRequestException($"{field} 開始與結束時間相同；全天營業請填 00:00～23:59");
                }

                segments.Add((day, open, close));
            }

            foreach (var sameDay in segments.GroupBy(s => s.day))
            {
                if (sameDay.Count() > MaxSegmentsPerDay)
                {
                    throw new BadRequestException($"{Days[sameDay.Key]} 最多 {MaxSegmentsPerDay} 個營業時段");
                }

                // 跨夜的時段把結束時間算到隔天（+24 小時）再比較
                var ordered = sameDay.OrderBy(s => s.open).ToList();
                for (int i = 1; i < ordered.Count; i++)
                {
                    var prev = ordered[i - 1];
                    int prevEnd = prev.close < prev.open ? prev.close + 24 * 60 : prev.close;
                    if (ordered[i].open < prevEnd)
                    {
                        throw new BadRequestException(
                            $"{Days[sameDay.Key]} 的營業時段重疊：{Format(prev.open)}～{Format(prev.close)} 與 {Format(ordered[i].open)}～{Format(ordered[i].close)}");
                    }
                }
            }

            return segments
                .OrderBy(s => s.day).ThenBy(s => s.open)
                .Select(s => new PlaceOperatingHourItem { day_of_week = Days[s.day], open_time = Format(s.open), close_time = Format(s.close) })
                .ToList();
        }

        /// <summary>"HH:mm"（24 小時制，小時可以只寫一位數）→ 當天第幾分鐘</summary>
        private static int ParseMinutes(string value, string field)
        {
            Match m = TimePattern.Match(value?.Trim() ?? "");
            if (m.Success)
            {
                int hour = int.Parse(m.Groups[1].Value);
                int minute = int.Parse(m.Groups[2].Value);
                if (hour == 24 && minute == 0)
                {
                    throw new BadRequestException($"{field} 營業到半夜 12 點請填 00:00");
                }
                if (hour <= 23 && minute <= 59)
                {
                    return hour * 60 + minute;
                }
            }
            throw new BadRequestException($"{field} 請用 24 小時制 HH:mm，例如 09:00、21:30");
        }

        private static string Format(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";
    }
}
