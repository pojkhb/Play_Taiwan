using System;
namespace backend.utils
{
    public class errormessage
    {
        public static string DateCompare(string startDate, string endDate){
            string Result = string.Empty;
            DateTime start = Convert.ToDateTime(startDate);
            DateTime end = Convert.ToDateTime(endDate);
            if(DateTime.Compare(start, end) > 0){
                Result = " 結束時間不能大於開始時間 ";
            }
            return Result;
        }

        public static string DateWriteCompareOpen(string writeDate_s, string writeDate_e, string openDate_s, string openDate_e){
            string Result = string.Empty;
            DateTime wDate_s = Convert.ToDateTime(writeDate_s);
            DateTime wDate_e = Convert.ToDateTime(writeDate_e);
            DateTime oDate_s = Convert.ToDateTime(openDate_s);
            DateTime oDate_e = Convert.ToDateTime(openDate_e);
            if(DateTime.Compare(wDate_s, oDate_s) >= 0 && DateTime.Compare(wDate_e, oDate_e) <= 0){
                Result = string.Empty;
            }else{
                Result = " 填報時間 須包含在 開放時間內！ ";
            }
            return Result;
        }
    }
}