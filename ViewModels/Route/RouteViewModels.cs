// 檔案路徑：System\ViewModels\Route\RouteViewModels.cs
// 劇本交通路線：步行 / 腳踏車 / 機車 / 汽車 / 公車 / 捷運 合併規劃（去程 + 返程）
using System.Collections.Generic;

namespace backend.ViewModels
{
    #region 交通方式

    /// <summary>交通方式代表色與名稱（前後端共用同一組顏色，路線才畫得一致）</summary>
    public static class TransportModes
    {
        public const string Walk = "步行";
        public const string Bicycle = "腳踏車";
        public const string Scooter = "機車";
        public const string Car = "汽車";
        public const string Bus = "公車";
        public const string Metro = "捷運";
        public const string Transfer = "轉乘";

        /// <summary>前端可勾選的交通方式，依顯示順序</summary>
        public static readonly string[] All = { Walk, Bicycle, Scooter, Car, Bus, Metro };

        public static string Color(string mode)
        {
            switch (mode)
            {
                case Walk: return "#2E7D32";
                case Bicycle: return "#EF6C00";
                case Scooter: return "#7B1FA2";
                case Car: return "#37474F";
                case Bus: return "#C62828";
                case Metro: return "#00897B";
                default: return "#757575";
            }
        }
    }

    /// <summary>某個交通方式在這個區域能不能用</summary>
    public class TransportAvailability
    {
        /// <summary>交通方式：步行 / 腳踏車 / 機車 / 汽車 / 公車 / 捷運</summary>
        public string mode { get; set; }

        /// <summary>是否可勾選；false 時前端反灰不能選</summary>
        public bool enabled { get; set; }

        /// <summary>可用 / 不可用的原因，例如「附近 350 公尺有捷運市政府站」、「這一帶步行 15 分鐘內沒有捷運站」</summary>
        public string reason { get; set; }

        /// <summary>最近的站名（公車站牌或捷運站），沒有時為 null</summary>
        public string nearest_name { get; set; }

        /// <summary>到最近站的直線距離（公尺），沒有時為 null</summary>
        public int? nearest_distance_m { get; set; }

        /// <summary>路線代表色（畫地圖、按鈕顏色用）</summary>
        public string color { get; set; }
    }

    #endregion


    #region 規劃請求

    /// <summary>地圖上的一個點</summary>
    public class RoutePoint
    {
        /// <summary>緯度</summary>
        public double lat { get; set; }

        /// <summary>經度</summary>
        public double lng { get; set; }

        /// <summary>名稱，例如「臺中州廳」</summary>
        public string name { get; set; }
    }

    /// <summary>劇本交通路線規劃條件</summary>
    public class RoutePlanRequest
    {
        /// <summary>劇本代號（story.s_id）；有帶時依劇本節點順序規劃，沒帶時改用 points</summary>
        public int? story_id { get; set; }

        /// <summary>起點（使用者目前位置）；沒帶時用劇本建議起點</summary>
        public RoutePoint start { get; set; }

        /// <summary>沒有劇本時要依序經過的點（不含起點）</summary>
        public List<RoutePoint> points { get; set; }

        /// <summary>交通方式（可複選）：步行、腳踏車、機車、汽車、公車、捷運。每一段會各自挑最快的方式</summary>
        public List<string> transportation { get; set; }

        /// <summary>是否規劃返程（最後一站回到起點），預設 true</summary>
        public bool include_return { get; set; } = true;
    }

    #endregion


    #region 規劃結果

    /// <summary>劇本交通路線規劃結果</summary>
    public class RoutePlanResponse
    {
        /// <summary>劇本代號，沒有劇本時為 null</summary>
        public int? story_id { get; set; }

        /// <summary>劇本標題</summary>
        public string story_title { get; set; }

        /// <summary>起點</summary>
        public RoutePoint start { get; set; }

        /// <summary>依序要去的節點（不含起點）</summary>
        public List<RouteStopPoint> stops { get; set; }

        /// <summary>每一段路線，依 leg_order 排序（去程在前、返程最後）</summary>
        public List<RouteLeg> legs { get; set; }

        /// <summary>去程總時間（分鐘）</summary>
        public double outbound_minutes { get; set; }

        /// <summary>返程時間（分鐘），沒規劃返程時為 0</summary>
        public double return_minutes { get; set; }

        /// <summary>全程時間（分鐘，不含在景點停留的時間）</summary>
        public double total_minutes { get; set; }

        /// <summary>全程距離（公里）</summary>
        public double total_distance_km { get; set; }

        /// <summary>實際用到的交通方式，例如 ["步行", "公車"]</summary>
        public List<string> modes_used { get; set; }

        /// <summary>提醒訊息，例如「臺中州廳 查不到座標，已略過」</summary>
        public List<string> warnings { get; set; }
    }

    /// <summary>路線要經過的節點</summary>
    public class RouteStopPoint
    {
        /// <summary>劇本節點代號（story_node.sn_id），沒有劇本時為 null</summary>
        public int? sn_id { get; set; }

        /// <summary>第幾站，從 1 開始</summary>
        public int order { get; set; }

        /// <summary>節點名稱（景點真實名稱）</summary>
        public string name { get; set; }

        /// <summary>劇情化的節點標題，沒有劇本時為 null</summary>
        public string title { get; set; }

        /// <summary>緯度</summary>
        public double lat { get; set; }

        /// <summary>經度</summary>
        public double lng { get; set; }
    }

    /// <summary>兩個點之間的一段路線</summary>
    public class RouteLeg
    {
        /// <summary>第幾段，從 1 開始</summary>
        public int leg_order { get; set; }

        /// <summary>方向：去程 / 返程</summary>
        public string direction { get; set; }

        /// <summary>出發點名稱</summary>
        public string from_name { get; set; }

        /// <summary>抵達點名稱</summary>
        public string to_name { get; set; }

        /// <summary>這段採用的主要交通方式（最快的那個選項），例如「公車」</summary>
        public string mode { get; set; }

        /// <summary>這段所需時間（分鐘，公車/捷運含步行與候車）</summary>
        public double minutes { get; set; }

        /// <summary>這段距離（公里）</summary>
        public double distance_km { get; set; }

        /// <summary>一句話說明，例如「步行 4 分 → 搭 300 路 5 站 → 步行 2 分」</summary>
        public string summary { get; set; }

        /// <summary>採用方案的每一小段（公車/捷運會拆成 步行 → 搭車 → 步行），地圖依序畫出</summary>
        public List<RouteSegment> segments { get; set; }

        /// <summary>這段所有算得出來的方案（含採用的那個），前端可讓使用者切換</summary>
        public List<RouteOption> options { get; set; }
    }

    /// <summary>一段路線的其中一種走法</summary>
    public class RouteOption
    {
        /// <summary>主要交通方式</summary>
        public string mode { get; set; }

        /// <summary>所需時間（分鐘）</summary>
        public double minutes { get; set; }

        /// <summary>距離（公里）</summary>
        public double distance_km { get; set; }

        /// <summary>一句話說明</summary>
        public string summary { get; set; }

        /// <summary>是否為目前採用的方案（最快）</summary>
        public bool is_chosen { get; set; }

        /// <summary>每一小段</summary>
        public List<RouteSegment> segments { get; set; }
    }

    /// <summary>路線中的一小段（同一種交通方式）</summary>
    public class RouteSegment
    {
        /// <summary>交通方式：步行 / 腳踏車 / 機車 / 汽車 / 公車 / 捷運 / 轉乘</summary>
        public string mode { get; set; }

        /// <summary>線條顏色；捷運是該路線的代表色</summary>
        public string color { get; set; }

        /// <summary>這一小段的時間（分鐘，不含候車）</summary>
        public double minutes { get; set; }

        /// <summary>候車時間估計（分鐘），只有公車/捷運的搭乘段有值</summary>
        public double wait_minutes { get; set; }

        /// <summary>這一小段的距離（公里）</summary>
        public double distance_km { get; set; }

        /// <summary>路線座標，[經度, 緯度] 清單，依行進方向排序（畫箭頭用）</summary>
        public List<double[]> coordinates { get; set; }

        /// <summary>公車路線名稱或捷運路線名稱，例如「300」、「烏日文心北屯線」</summary>
        public string route_name { get; set; }

        /// <summary>公車路線類型或捷運系統，例如「市區公車」、「臺中捷運」</summary>
        public string route_type_name { get; set; }

        /// <summary>車頭往向，例如「往 臺中車站」</summary>
        public string headsign { get; set; }

        /// <summary>上車站名</summary>
        public string board_name { get; set; }

        /// <summary>下車站名</summary>
        public string alight_name { get; set; }

        /// <summary>搭乘站數</summary>
        public int stop_count { get; set; }

        /// <summary>搭乘途中經過的站（含上下車站），只有公車/捷運有</summary>
        public List<RoutePoint> stops { get; set; }

        /// <summary>給使用者看的說明，例如「在 臺中車站 搭 300 往 靜宜大學，坐 5 站到 科博館 下車」</summary>
        public string instruction { get; set; }
    }

    #endregion


    #region 劇本清單

    /// <summary>可以規劃交通路線的劇本</summary>
    public class RouteStoryItem
    {
        /// <summary>劇本代號</summary>
        public int story_id { get; set; }

        /// <summary>劇本標題</summary>
        public string story_title { get; set; }

        /// <summary>城市</summary>
        public string city_name { get; set; }

        /// <summary>行政區</summary>
        public string district_name { get; set; }

        /// <summary>節點數</summary>
        public int node_count { get; set; }

        /// <summary>建議起點：第一站附近的捷運站或最多路線經過的公車站，前端沒有定位時可以直接用</summary>
        public RoutePoint suggested_start { get; set; }

        /// <summary>節點（含座標），依順序排列</summary>
        public List<RouteStopPoint> nodes { get; set; }
    }

    #endregion


    #region 捷運

    /// <summary>捷運資料同步結果</summary>
    public class MetroSyncResult
    {
        /// <summary>捷運系統代碼，例如 TMRT</summary>
        public string rail_system { get; set; }

        /// <summary>系統名稱，例如「臺中捷運」</summary>
        public string system_name { get; set; }

        /// <summary>路線數</summary>
        public int line_count { get; set; }

        /// <summary>車站數</summary>
        public int station_count { get; set; }

        /// <summary>相鄰車站連線數（雙向）</summary>
        public int link_count { get; set; }

        /// <summary>有線形的路線數</summary>
        public int shape_count { get; set; }

        /// <summary>TDX 沒有站間行駛時間、改用距離估算的路線數</summary>
        public int estimated_line_count { get; set; }

        /// <summary>耗時（秒）</summary>
        public double elapsed_seconds { get; set; }
    }

    /// <summary>捷運路線（含車站與線形），畫捷運路網用</summary>
    public class MetroLineItem
    {
        /// <summary>路線代號（metro_line.ml_id）</summary>
        public int ml_id { get; set; }

        /// <summary>系統名稱，例如「臺北捷運」</summary>
        public string system_name { get; set; }

        /// <summary>路線代碼，例如 BL</summary>
        public string line_no { get; set; }

        /// <summary>路線名稱，例如「板南線」</summary>
        public string line_name { get; set; }

        /// <summary>路線代表色</summary>
        public string line_color { get; set; }

        /// <summary>車站，依站序排列</summary>
        public List<MetroStationItem> stations { get; set; }

        /// <summary>線形，每一段是 [經度, 緯度] 清單（有支線時會有多段）</summary>
        public List<List<double[]>> shape { get; set; }
    }

    /// <summary>捷運車站</summary>
    public class MetroStationItem
    {
        /// <summary>車站代號（metro_station.mst_id）</summary>
        public int mst_id { get; set; }

        /// <summary>車站代碼，例如 BL12</summary>
        public string station_code { get; set; }

        /// <summary>車站名稱</summary>
        public string station_name { get; set; }

        /// <summary>緯度</summary>
        public double lat { get; set; }

        /// <summary>經度</summary>
        public double lng { get; set; }
    }

    #endregion
}
