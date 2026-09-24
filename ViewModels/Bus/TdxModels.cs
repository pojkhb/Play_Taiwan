// 檔案路徑：System\ViewModels\Bus\TdxModels.cs
// 交通部 TDX 運輸資料流通服務回傳的公車資料（只保留會用到的欄位）
// 市區公車：/v2/Bus/{Route|StopOfRoute|Shape|Schedule}/City/{City}
// 台灣好行：/v2/Tourism/Bus/{Route|StopOfRoute|Shape|Schedule}/TaiwanTrip
using System.Collections.Generic;

namespace backend.ViewModels
{
    public class TdxName
    {
        public string Zh_tw { get; set; }
        public string En { get; set; }
    }

    public class TdxRoute
    {
        public string RouteUID { get; set; }
        public TdxName RouteName { get; set; }           // 台灣好行沒有這欄，名稱在 SubRoutes[].TaiwanTripName
        public List<TdxSubRoute> SubRoutes { get; set; }
        public string DepartureStopNameZh { get; set; }
        public string DestinationStopNameZh { get; set; }
        public string RouteMapImageUrl { get; set; }
        public string TicketPriceDescriptionZh { get; set; }
        public string City { get; set; }
        public string UpdateTime { get; set; }
    }

    public class TdxSubRoute
    {
        public string SubRouteUID { get; set; }
        public TdxName SubRouteName { get; set; }
        public TdxName TaiwanTripName { get; set; }
        public string Headsign { get; set; }
        public int Direction { get; set; }
    }

    public class TdxStopOfRoute
    {
        public string RouteUID { get; set; }
        public string SubRouteUID { get; set; }
        public TdxName SubRouteName { get; set; }
        public TdxName TaiwanTripName { get; set; }
        public int Direction { get; set; }
        public bool? KeyPattern { get; set; }
        public List<TdxRouteStop> Stops { get; set; }
    }

    public class TdxRouteStop
    {
        public string StopUID { get; set; }
        public TdxName StopName { get; set; }
        public int StopSequence { get; set; }
        public TdxPosition StopPosition { get; set; }
        public string StationID { get; set; }
        public string LocationCityCode { get; set; }
    }

    public class TdxPosition
    {
        public double PositionLon { get; set; }
        public double PositionLat { get; set; }
    }

    public class TdxShape
    {
        public string RouteUID { get; set; }
        public string SubRouteUID { get; set; }
        public int Direction { get; set; }
        public string Geometry { get; set; }
    }

    public class TdxSchedule
    {
        public string RouteUID { get; set; }
        public string SubRouteUID { get; set; }
        public int Direction { get; set; }
        public List<TdxTimetable> Timetables { get; set; }
        public List<TdxFrequency> Frequencys { get; set; }
    }

    public class TdxTimetable
    {
        public string TripID { get; set; }
        public TdxServiceDay ServiceDay { get; set; }
        public List<TdxStopTime> StopTimes { get; set; }
    }

    public class TdxStopTime
    {
        public int StopSequence { get; set; }
        public string DepartureTime { get; set; }
        public string ArrivalTime { get; set; }
    }

    public class TdxFrequency
    {
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public int MinHeadwayMins { get; set; }
        public int MaxHeadwayMins { get; set; }
        public TdxServiceDay ServiceDay { get; set; }
    }

    public class TdxServiceDay
    {
        public string ServiceTag { get; set; }
        public int Monday { get; set; }
        public int Tuesday { get; set; }
        public int Wednesday { get; set; }
        public int Thursday { get; set; }
        public int Friday { get; set; }
        public int Saturday { get; set; }
        public int Sunday { get; set; }
    }

    #region 捷運 / 輕軌（/v2/Rail/Metro/{Line|Station|StationOfLine|S2STravelTime|Shape}/{RailSystem}）

    public class TdxMetroLine
    {
        public string LineNo { get; set; }
        public string LineID { get; set; }
        /// <summary>路線代碼：LineNo，沒有時用 LineID（例如高雄輕軌只有 LineID）</summary>
        public string Key => string.IsNullOrWhiteSpace(LineNo) ? LineID : LineNo;

        public TdxName LineName { get; set; }
        public string LineColor { get; set; }
    }

    public class TdxMetroStation
    {
        public string StationUID { get; set; }
        public string StationID { get; set; }
        public TdxName StationName { get; set; }
        public string StationAddress { get; set; }
        public TdxPosition StationPosition { get; set; }
        public string LocationCity { get; set; }
        public string LocationTown { get; set; }
    }

    public class TdxMetroStationOfLine
    {
        public string LineNo { get; set; }
        public string LineID { get; set; }
        /// <summary>路線代碼：LineNo，沒有時用 LineID（例如高雄輕軌只有 LineID）</summary>
        public string Key => string.IsNullOrWhiteSpace(LineNo) ? LineID : LineNo;

        public List<TdxMetroLineStation> Stations { get; set; }
    }

    public class TdxMetroLineStation
    {
        public int Sequence { get; set; }
        public string StationID { get; set; }
        public TdxName StationName { get; set; }
        public double? CumulativeDistance { get; set; }
    }

    public class TdxMetroTravelTime
    {
        public string LineNo { get; set; }
        public string LineID { get; set; }
        /// <summary>路線代碼：LineNo，沒有時用 LineID（例如高雄輕軌只有 LineID）</summary>
        public string Key => string.IsNullOrWhiteSpace(LineNo) ? LineID : LineNo;

        public string RouteID { get; set; }
        public List<TdxMetroStationTime> TravelTimes { get; set; }
    }

    public class TdxMetroStationTime
    {
        public int Sequence { get; set; }
        public string FromStationID { get; set; }
        public string ToStationID { get; set; }
        public int RunTime { get; set; }
        public int StopTime { get; set; }
    }

    public class TdxMetroShape
    {
        public string LineNo { get; set; }
        public string LineID { get; set; }
        /// <summary>路線代碼：LineNo，沒有時用 LineID（例如高雄輕軌只有 LineID）</summary>
        public string Key => string.IsNullOrWhiteSpace(LineNo) ? LineID : LineNo;

        public string Geometry { get; set; }
    }

    #endregion

    public class TdxEstimatedArrival
    {
        public string StopUID { get; set; }
        public string RouteUID { get; set; }
        public TdxName RouteName { get; set; }
        public string SubRouteUID { get; set; }
        public TdxName SubRouteName { get; set; }
        public int Direction { get; set; }
        public int? EstimateTime { get; set; }        // 秒
        public int StopStatus { get; set; }           // 0 正常、1 尚未發車、2 交管不停靠、3 末班車已過、4 今日未營運
        public string NextBusTime { get; set; }
    }
}
