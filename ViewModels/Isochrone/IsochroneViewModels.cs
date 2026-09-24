// 檔案路徑：System\ViewModels\IsochroneViewModels.cs
using System.Collections.Generic;


namespace backend.ViewModels
{
    #region 等時圈 + 真實時間

    /// <summary>交通等時圈查詢條件</summary>
    public class ReachableAttractionsRequest
    {
        /// <summary>中心點緯度（使用者當前定位）。沒有定位時可改傳 city_name/town_name</summary>
        public double lat { get; set; }

        /// <summary>中心點經度</summary>
        public double lng { get; set; }

        /// <summary>沒有 GPS 定位時的備用中心點：城市，例如「臺中市」</summary>
        public string city_name { get; set; }

        /// <summary>沒有 GPS 定位時的備用中心點：行政區，例如「西區」</summary>
        public string town_name { get; set; }

        /// <summary>交通方式（可複選），例如 ["步行", "機車"]。公車/捷運不列入計算</summary>
        public List<string> transportation { get; set; }

        /// <summary>等時圈分鐘數，最多 4 個、每個不超過 120，不帶時預設 [10, 20, 30]</summary>
        public List<int> contour_minutes { get; set; }

        /// <summary>是否回傳等時圈多邊形（前端要在地圖上畫圈時才需要）</summary>
        public bool include_polygons { get; set; }
    }


    /// <summary>單一交通方式、單一分鐘數的等時圈</summary>
    public class IsochroneBand
    {
        /// <summary>交通方式，例如「步行」、「機車」</summary>
        public string transport { get; set; }

        /// <summary>路線引擎內部的交通方式代碼（pedestrian=步行、bicycle=腳踏車、motor_scooter=機車、auto=汽車），前端可忽略</summary>
        public string costing { get; set; }

        /// <summary>這一圈的分鐘數上限</summary>
        public int minutes { get; set; }

        /// <summary>
        /// GeoJSON MultiPolygon 的 coordinates 格式：[多邊形][環][點]，點為 [經度, 緯度]。
        /// 每個多邊形的第一個環是外框，其餘是破洞。
        /// </summary>
        public List<List<List<double[]>>> polygons { get; set; } = new List<List<List<double[]>>>();
    }


    /// <summary>落在等時圈內的景點</summary>
    public class ReachableAttractionNode
    {
        /// <summary>景點唯一代號（Neo4j uid，可直接當 story_node.place_id）</summary>
        public string uid { get; set; }

        /// <summary>景點名稱</summary>
        public string name { get; set; }

        /// <summary>景點緯度</summary>
        public double lat { get; set; }

        /// <summary>景點經度</summary>
        public double lon { get; set; }

        /// <summary>與中心點的直線距離（公尺）</summary>
        public double distance_m { get; set; }

        /// <summary>實際交通時間（分鐘，小數一位），例如 7.4</summary>
        public double travel_minutes { get; set; }

        /// <summary>沿道路走的實際距離（公里）</summary>
        public double travel_distance_km { get; set; }

        /// <summary>交通方式：最快可到達這個景點的交通方式，例如「步行」、「機車」</summary>
        public string reachable_by { get; set; }

        /// <summary>第幾圈，依實際交通時間決定，1 = 最內圈（最近）</summary>
        public int ring { get; set; }

        /// <summary>所在圈層的分鐘數上限，例如 travel_minutes = 7.4 時為 10，代表「10 分鐘內可到」</summary>
        public int reachable_minutes { get; set; }
    }


    /// <summary>交通等時圈查詢結果</summary>
    public class ReachableAttractionsResponse
    {
        /// <summary>中心點緯度（使用者位置，或城市/行政區轉換出的座標）</summary>
        public double center_lat { get; set; }

        /// <summary>中心點經度</summary>
        public double center_lng { get; set; }

        /// <summary>實際使用的等時圈分鐘數，例如 [10, 20, 30]</summary>
        public List<int> contour_minutes { get; set; }

        /// <summary>範圍內可到達的景點總數（已去除重複），attractions 是從中挑出的推薦景點</summary>
        public int total_reachable { get; set; }

        /// <summary>推薦景點 8 個：各圈層輪流隨機抽（每次呼叫結果不同），已依順路的參觀順序排好</summary>
        public List<ReachableAttractionNode> attractions { get; set; }

        /// <summary>等時圈多邊形，用來在地圖上畫範圍；include_polygons = false 時為 null</summary>
        public List<IsochroneBand> isochrones { get; set; }
    }

    #endregion


    #region 路線

    /// <summary>路線上的一個點</summary>
    public class TravelRoutePoint
    {
        public double lat { get; set; }
        public double lng { get; set; }

        /// <summary>選填：點的名稱（例如景點名），會原樣帶回每一段的起訖點</summary>
        public string name { get; set; }
    }


    /// <summary>路線查詢條件</summary>
    public class TravelRouteRequest
    {
        /// <summary>依序經過的點，至少 2 個（第一個是起點）</summary>
        public List<TravelRoutePoint> points { get; set; }

        /// <summary>交通方式（可複選），每一段會各自挑最快的方式</summary>
        public List<string> transportation { get; set; }
    }


    /// <summary>路線中的一段</summary>
    public class TravelRouteLeg
    {
        /// <summary>第幾段，從 1 開始</summary>
        public int leg_order { get; set; }

        /// <summary>這一段的起點名稱（request 有帶 name 才有值）</summary>
        public string from_name { get; set; }

        /// <summary>這一段的終點名稱（request 有帶 name 才有值）</summary>
        public string to_name { get; set; }

        /// <summary>交通方式：這一段用哪種交通方式，例如「步行」</summary>
        public string transport { get; set; }

        /// <summary>真實交通時間（分鐘，小數一位）</summary>
        public double travel_minutes { get; set; }

        /// <summary>沿道路的實際距離（公里）</summary>
        public double distance_km { get; set; }

        /// <summary>路線座標，[經度, 緯度] 清單，前端依序連線即可畫出路線</summary>
        public List<double[]> coordinates { get; set; }
    }


    /// <summary>路線查詢結果</summary>
    public class TravelRouteResponse
    {
        /// <summary>整條路線的總交通時間（分鐘）</summary>
        public double total_minutes { get; set; }

        /// <summary>整條路線的總距離（公里）</summary>
        public double total_distance_km { get; set; }

        /// <summary>每一段路線（第 1 點→第 2 點、第 2 點→第 3 點…）</summary>
        public List<TravelRouteLeg> legs { get; set; }
    }

    #endregion
}