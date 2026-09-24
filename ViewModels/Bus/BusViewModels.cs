// 檔案路徑：System\ViewModels\Bus\BusViewModels.cs
using System.Collections.Generic;

namespace backend.ViewModels
{
    /// <summary>公車資料同步結果</summary>
    public class BusSyncResult
    {
        /// <summary>同步來源，例如「臺中市」、「台灣好行」</summary>
        public string source { get; set; }

        /// <summary>營運中路線數</summary>
        public int route_count { get; set; }

        /// <summary>子路線數</summary>
        public int sub_route_count { get; set; }

        /// <summary>行駛型態數（子路線 × 方向）</summary>
        public int pattern_count { get; set; }

        /// <summary>這次重建站序的行駛型態數（站序沒變的不動，避免舊劇本的公車方案失效）</summary>
        public int rebuilt_pattern_count { get; set; }

        /// <summary>站牌數</summary>
        public int stop_count { get; set; }

        /// <summary>班次數</summary>
        public int trip_count { get; set; }

        /// <summary>這次停用的路線數（TDX 已經沒有的路線）</summary>
        public int deactivated_route_count { get; set; }

        /// <summary>花費秒數</summary>
        public double elapsed_seconds { get; set; }
    }

    /// <summary>景點附近站牌計算結果</summary>
    public class PlaceBusStopBuildResult
    {
        /// <summary>計算範圍，例如「臺中市」</summary>
        public string scope { get; set; }

        /// <summary>範圍內的景點數</summary>
        public int place_count { get; set; }

        /// <summary>至少有一個站牌在步行範圍內的景點數</summary>
        public int place_with_stop_count { get; set; }

        /// <summary>寫入的景點-站牌對照筆數</summary>
        public int pair_count { get; set; }

        /// <summary>步行距離是用 Valhalla 真實路網算的（false 代表 Valhalla 無法使用，改用直線距離 × 1.3 估算）</summary>
        public bool used_valhalla { get; set; }

        /// <summary>花費秒數</summary>
        public double elapsed_seconds { get; set; }
    }

    /// <summary>附近的公車站牌</summary>
    public class BusStopNearbyItem
    {
        /// <summary>站牌代號（bus_stop.bs_id）</summary>
        public int bs_id { get; set; }

        /// <summary>站牌名稱</summary>
        public string stop_name { get; set; }

        /// <summary>站牌緯度</summary>
        public double lat { get; set; }

        /// <summary>站牌經度</summary>
        public double lng { get; set; }

        /// <summary>與查詢位置的直線距離（公尺）</summary>
        public int distance_m { get; set; }

        /// <summary>經過這個站牌的路線</summary>
        public List<BusRouteBrief> routes { get; set; }
    }

    /// <summary>公車路線詳情</summary>
    public class BusRouteDetail
    {
        /// <summary>路線代號（bus_route.br_id）</summary>
        public int br_id { get; set; }

        /// <summary>TDX 路線代碼</summary>
        public string route_uid { get; set; }

        /// <summary>路線名稱</summary>
        public string route_name { get; set; }

        /// <summary>路線類型：1 = 市區公車、2 = 公路客運、3 = 台灣好行</summary>
        public int route_type { get; set; }

        /// <summary>路線類型名稱</summary>
        public string route_type_name { get; set; }

        /// <summary>所屬縣市</summary>
        public string city_name { get; set; }

        /// <summary>起站名稱</summary>
        public string departure_stop_name { get; set; }

        /// <summary>迄站名稱</summary>
        public string destination_stop_name { get; set; }

        /// <summary>票價說明</summary>
        public string fare_desc { get; set; }

        /// <summary>官方路線圖網址</summary>
        public string route_map_url { get; set; }

        /// <summary>班距說明（只有固定班距的路線才有），例如「平常日 07:00-16:00 每 20 分」</summary>
        public string headway_desc { get; set; }

        /// <summary>台灣好行專屬資訊，一般公車為 null</summary>
        public TaiwanTripperInfo tripper { get; set; }

        /// <summary>各行駛型態（子路線 × 方向）的完整站牌、線形與班表</summary>
        public List<BusRoutePatternDetail> patterns { get; set; }
    }

    /// <summary>行駛型態（子路線 × 方向）</summary>
    public class BusRoutePatternDetail
    {
        /// <summary>行駛型態代號（bus_route_pattern.brp_id）</summary>
        public int brp_id { get; set; }

        /// <summary>子路線名稱，例如「300 區間車」</summary>
        public string sub_route_name { get; set; }

        /// <summary>方向：0 = 去程、1 = 返程、2 = 迴圈</summary>
        public int direction { get; set; }

        /// <summary>方向名稱：去程 / 返程 / 迴圈</summary>
        public string direction_name { get; set; }

        /// <summary>車頭往向，例如「往 臺中車站」</summary>
        public string headsign { get; set; }

        /// <summary>依站序排列的所有站牌</summary>
        public List<BusPatternStop> stops { get; set; }

        /// <summary>路線線形，[經度, 緯度] 清單，前端依序連線即可畫出路線；沒有線形資料時為空陣列</summary>
        public List<double[]> shape { get; set; }

        /// <summary>起站發車時間（依行駛日分組）</summary>
        public List<BusDepartureGroup> departures { get; set; }
    }

    /// <summary>行駛型態上的一個站牌</summary>
    public class BusPatternStop
    {
        /// <summary>站牌代號（bus_stop.bs_id），查即時到站用</summary>
        public int bs_id { get; set; }

        /// <summary>站序</summary>
        public int stop_sequence { get; set; }

        /// <summary>站牌名稱</summary>
        public string stop_name { get; set; }

        /// <summary>站牌緯度</summary>
        public double lat { get; set; }

        /// <summary>站牌經度</summary>
        public double lng { get; set; }
    }

    /// <summary>同一種行駛日的發車時間</summary>
    public class BusDepartureGroup
    {
        /// <summary>行駛日說明，例如「週一至週五」、「週六、週日」</summary>
        public string service_days { get; set; }

        /// <summary>起站發車時間清單，例如 ["06:30", "07:00"]</summary>
        public List<string> times { get; set; }
    }

    /// <summary>台灣好行路線專屬資訊</summary>
    public class TaiwanTripperInfo
    {
        /// <summary>路線主題，例如「山林湖光」</summary>
        public string theme { get; set; }

        /// <summary>路線介紹</summary>
        public string introduction { get; set; }

        /// <summary>封面圖片網址</summary>
        public string cover_image { get; set; }

        /// <summary>台灣好行官網路線頁</summary>
        public string website_url { get; set; }
    }

    /// <summary>台灣好行路線列表的一筆</summary>
    public class TaiwanTripperRouteItem
    {
        /// <summary>路線代號（bus_route.br_id），查詳情用</summary>
        public int br_id { get; set; }

        /// <summary>路線名稱，例如「台灣好行 日月潭線」</summary>
        public string route_name { get; set; }

        /// <summary>所屬縣市（跨縣市路線可能為 null）</summary>
        public string city_name { get; set; }

        /// <summary>起站名稱</summary>
        public string departure_stop_name { get; set; }

        /// <summary>迄站名稱</summary>
        public string destination_stop_name { get; set; }

        /// <summary>票價說明</summary>
        public string fare_desc { get; set; }

        /// <summary>路線主題（taiwan_tripper_route 有資料才有）</summary>
        public string theme { get; set; }

        /// <summary>封面圖片（taiwan_tripper_route 有資料才有）</summary>
        public string cover_image { get; set; }

        /// <summary>沿線站牌數</summary>
        public int stop_count { get; set; }
    }

    /// <summary>站牌的即時到站資訊</summary>
    public class BusArrivalItem
    {
        /// <summary>路線名稱</summary>
        public string route_name { get; set; }

        /// <summary>子路線名稱</summary>
        public string sub_route_name { get; set; }

        /// <summary>方向：0 = 去程、1 = 返程、2 = 迴圈</summary>
        public int direction { get; set; }

        /// <summary>預估幾分鐘後到站，沒有車在路上時為 null（看 status_text）</summary>
        public int? estimate_minutes { get; set; }

        /// <summary>狀態說明，例如「約 3 分鐘」、「尚未發車」、「末班車已過」</summary>
        public string status_text { get; set; }

        /// <summary>尚未發車時的下一班發車時間，例如「19:29」</summary>
        public string next_bus_time { get; set; }
    }
}
