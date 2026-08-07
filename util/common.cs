using System;
using System.Collections.Generic;
using System.Text;

namespace backend.utils
{
    public class common
    {

        public static string DateFormat_full(DateTime date)
        {
            return date.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public static string DateFormat_simple(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }

        public static string DateFormatForDateSlash(DateTime date)
        {
            return date.ToString("yyyy/MM/dd");
        }

        public static string DateStrFormatForDateSlashSixLength(string date)
        {
            return string.Concat(date.Substring(0, 4), "/", date.Substring(4, 2), "/", date.Substring(6, 2));
        }

        public static string DateFormat_report(DateTime date)
        {
            return date.ToString("yyyyMMdd");
        }

        public static string LaneTypeChangeDashboardReport(string value)
        {
            switch (value)
            {
                case "環島":
                    return "環島路網事故總件(A1+A2)";
                case "多元":
                    return "多元路網事故總件(A1+A2)";
                case "串聯":
                    return "串聯路網事故總件(A1+A2)";
                default:
                    return string.Empty;
            }
        }

        /// 座標格式調整，小數位數自訂
        public static double CoordinateFormatDecimal(double Coordinate, int Place)
        {
            return Math.Round(Coordinate, Place);
        }

        /// A1 : 死亡 ； A2 : 受傷
        public static string AccidentTypeFormat(string AccidentType)
        {
            switch(AccidentType){
                case "A1":
                return "死亡事故";
                case "A2":
                return "受傷事故";
                default:
                return string.Empty;
            }
        }
    }


}