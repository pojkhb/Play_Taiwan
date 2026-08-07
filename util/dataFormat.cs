namespace backend.utils
{
    public class dataFormat
    {
        public static string dayOfWeekFormat(int Day)
        {
            string DayOfWeek = string.Empty;

            switch (Day)
            {
                case 0:
                    DayOfWeek = "日";
                    break;
                case 1:
                    DayOfWeek = "一";
                    break;
                case 2:
                    DayOfWeek = "二";
                    break;
                case 3:
                    DayOfWeek = "三";
                    break;
                case 4:
                    DayOfWeek = "四";
                    break;
                case 5:
                    DayOfWeek = "五";
                    break;
                case 6:
                    DayOfWeek = "六";
                    break;
                default:
                    DayOfWeek = "錯誤";
                    break;
            }

            return DayOfWeek;
        }
    }
}