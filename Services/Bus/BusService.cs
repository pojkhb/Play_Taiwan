// 檔案路徑：System\Services\Bus\BusService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using backend.dao;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>
    /// 公車 / 台灣好行：
    /// 1. 同步：從 TDX 抓路線、站序、線形、班表寫進資料庫
    /// 2. 景點附近站牌：算出每個景點步行 500 公尺內的站牌（place_bus_stop），生成劇本規劃公車時使用
    /// 3. 查詢：附近站牌、路線詳情、台灣好行列表、即時到站
    /// </summary>
    public class BusService
    {
        private const int MaxWalkMeters = 500;          // 景點到站牌最多走多遠（直線篩選 + 步行距離）
        private const int MaxStopsPerPlace = 5;         // 每個景點最多存幾個站牌
        private const double WalkMetersPerMinute = 75;  // Valhalla 不能用時的步行速度估算
        private const double WalkDetourFactor = 1.3;    // 直線距離換算步行距離的繞路係數

        private readonly BusDao _dao;
        private readonly TdxClient _tdx;
        private readonly Neo4jService _neo4jService;
        private readonly ValhallaService _valhallaService;

        public BusService(BusDao dao, TdxClient tdx, Neo4jService neo4jService, ValhallaService valhallaService)
        {
            _dao = dao;
            _tdx = tdx;
            _neo4jService = neo4jService;
            _valhallaService = valhallaService;
        }


        #region 同步

        /// <summary>同步某縣市的市區公車，city 可傳中文（臺中市/台中市）或 TDX 參數（Taichung）</summary>
        public async Task<BusSyncResult> SyncCityAsync(string city)
        {
            string tdxCity = TdxClient.ResolveCity(city) ?? throw new ArgumentException($"不支援的縣市：{city}");
            string cityName = TdxClient.CityNames[tdxCity];
            var watch = Stopwatch.StartNew();

            var routes = await _tdx.GetAsync<List<TdxRoute>>($"v2/Bus/Route/City/{tdxCity}");
            var stopOfRoutes = await _tdx.GetAsync<List<TdxStopOfRoute>>($"v2/Bus/StopOfRoute/City/{tdxCity}");
            var shapes = await _tdx.GetAsync<List<TdxShape>>($"v2/Bus/Shape/City/{tdxCity}");
            var schedules = await _tdx.GetAsync<List<TdxSchedule>>($"v2/Bus/Schedule/City/{tdxCity}");

            BusSyncResult result = await _dao.SaveBusDataAsync(BusDao.RouteTypeCity, cityName, routes, stopOfRoutes, shapes, schedules);
            result.source = cityName;
            result.elapsed_seconds = Math.Round(watch.Elapsed.TotalSeconds, 1);
            return result;
        }

        /// <summary>同步全台台灣好行</summary>
        public async Task<BusSyncResult> SyncTaiwanTripAsync()
        {
            var watch = Stopwatch.StartNew();

            var routes = await _tdx.GetAsync<List<TdxRoute>>("v2/Tourism/Bus/Route/TaiwanTrip");
            var stopOfRoutes = await _tdx.GetAsync<List<TdxStopOfRoute>>("v2/Tourism/Bus/StopOfRoute/TaiwanTrip");
            var shapes = await _tdx.GetAsync<List<TdxShape>>("v2/Tourism/Bus/Shape/TaiwanTrip");
            var schedules = await _tdx.GetAsync<List<TdxSchedule>>("v2/Tourism/Bus/Schedule/TaiwanTrip");

            BusSyncResult result = await _dao.SaveBusDataAsync(BusDao.RouteTypeTaiwanTrip, null, routes, stopOfRoutes, shapes, schedules);
            result.source = "台灣好行";
            result.elapsed_seconds = Math.Round(watch.Elapsed.TotalSeconds, 1);
            return result;
        }

        #endregion


        #region 景點附近站牌

        /// <summary>
        /// 重算景點附近站牌。scope 傳縣市（臺中市/Taichung）只算該縣市站牌；傳「台灣好行」只算好行沿線站牌。
        /// 景點來自 Neo4j（:Attraction），步行距離優先用 Valhalla 真實路網。
        /// </summary>
        public async Task<PlaceBusStopBuildResult> BuildPlaceBusStopsAsync(string scope)
        {
            var watch = Stopwatch.StartNew();
            bool tripperOnly = (scope ?? "").Replace("臺", "台").Contains("台灣好行") || string.Equals(scope, "TaiwanTrip", StringComparison.OrdinalIgnoreCase);
            string cityName = null;
            if (!tripperOnly)
            {
                string tdxCity = TdxClient.ResolveCity(scope) ?? throw new ArgumentException($"不支援的範圍：{scope}（請傳縣市名稱或「台灣好行」）");
                cityName = TdxClient.CityNames[tdxCity];
            }

            List<BusDao.StopPoint> stops = await _dao.GetStopsForScopeAsync(cityName, tripperOnly);
            var result = new PlaceBusStopBuildResult { scope = tripperOnly ? "台灣好行" : cityName, used_valhalla = true };
            if (stops.Count == 0)
                throw new Exception($"{result.scope} 還沒有公車站牌資料，請先同步公車");

            // ── 1. 撈範圍內的景點 ──
            double margin = 0.006;
            var places = await _neo4jService.ExecuteCypherAsync<List<PlacePoint>>(@"
                MATCH (a:Attraction)
                WHERE a.uid IS NOT NULL AND a.lat IS NOT NULL AND a.lon IS NOT NULL
                  AND a.lat >= $minLat AND a.lat <= $maxLat AND a.lon >= $minLon AND a.lon <= $maxLon
                RETURN DISTINCT a.uid AS uid, a.lat AS lat, a.lon AS lon",
                new
                {
                    minLat = stops.Min(s => s.lat) - margin, maxLat = stops.Max(s => s.lat) + margin,
                    minLon = stops.Min(s => s.lng) - margin, maxLon = stops.Max(s => s.lng) + margin
                });

            if (places == null)
                throw new Exception("Neo4j 景點查詢失敗（AI service 可能無法連線），請稍後再試");

            places = places.GroupBy(p => p.uid).Select(g => g.First()).ToList();
            result.place_count = places.Count;

            // ── 2. 站牌建網格索引（約 550 公尺一格），每個景點只比對周圍 9 格 ──
            const double cell = 0.005;
            var grid = stops.GroupBy(s => ((int)Math.Floor(s.lat / cell), (int)Math.Floor(s.lng / cell)))
                            .ToDictionary(g => g.Key, g => g.ToList());

            // ── 3. 每個景點：直線 500 公尺內的站牌 → Valhalla 算步行距離 → 取最近 5 個 ──
            var pairs = new List<(string placeId, int bsId, int walkMeters, double walkMinutes)>();
            var pairLock = new object();
            int valhallaFailed = 0;
            var throttle = new SemaphoreSlim(8);

            await Task.WhenAll(places.Select(async p =>
            {
                int gy = (int)Math.Floor(p.lat / cell), gx = (int)Math.Floor(p.lon / cell);
                var candidates = new List<(BusDao.StopPoint stop, double meters)>();
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (grid.TryGetValue((gy + dy, gx + dx), out var list))
                            foreach (var s in list)
                            {
                                double m = DistanceMeters(p.lat, p.lon, s.lat, s.lng);
                                if (m <= MaxWalkMeters) candidates.Add((s, m));
                            }

                if (candidates.Count == 0) return;
                candidates = candidates.OrderBy(c => c.meters).Take(MaxStopsPerPlace * 2).ToList();

                List<(int bsId, int meters, double minutes)> walks = null;
                if (Volatile.Read(ref valhallaFailed) == 0)
                {
                    await throttle.WaitAsync();
                    try
                    {
                        var times = await _valhallaService.GetTravelTimesAsync(
                            p.lat, p.lon, candidates.Select(c => (c.stop.lat, c.stop.lng)).ToList(), "pedestrian");
                        walks = candidates.Select((c, i) => (c.stop.bs_id, times[i]))
                            .Where(x => x.Item2.seconds.HasValue && x.Item2.km.HasValue)
                            .Select(x => (x.bs_id, (int)Math.Round(x.Item2.km.Value * 1000), x.Item2.seconds.Value / 60.0))
                            .ToList();
                    }
                    catch
                    {
                        Interlocked.Exchange(ref valhallaFailed, 1);
                    }
                    finally
                    {
                        throttle.Release();
                    }
                }

                walks ??= candidates.Select(c =>
                {
                    int meters = (int)Math.Round(c.meters * WalkDetourFactor);
                    return (c.stop.bs_id, meters, meters / WalkMetersPerMinute);
                }).ToList();

                var kept = walks.Where(w => w.meters <= MaxWalkMeters * WalkDetourFactor)
                                .OrderBy(w => w.meters).Take(MaxStopsPerPlace).ToList();

                lock (pairLock)
                    pairs.AddRange(kept.Select(w => (p.uid, w.bsId, w.meters, w.minutes)));
            }));

            // ── 4. 寫入（整個範圍的景點都先清掉舊資料，沒有站牌的景點也會被清空）──
            await _dao.SavePlaceBusStopsAsync(places.Select(p => p.uid).ToList(), pairs);

            result.used_valhalla = valhallaFailed == 0;
            result.pair_count = pairs.Count;
            result.place_with_stop_count = pairs.Select(p => p.placeId).Distinct().Count();
            result.elapsed_seconds = Math.Round(watch.Elapsed.TotalSeconds, 1);
            return result;
        }

        private class PlacePoint
        {
            public string uid { get; set; }
            public double lat { get; set; }
            public double lon { get; set; }
        }

        private static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000;
            double dLat = (lat2 - lat1) * Math.PI / 180;
            double dLon = (lon2 - lon1) * Math.PI / 180;
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * R * Math.Asin(Math.Sqrt(h));
        }

        #endregion


        #region 查詢

        public Task<int> CountActiveRoutesAsync() => _dao.CountActiveRoutesAsync();

        public Task<int> CountPlaceBusStopsAsync() => _dao.CountPlaceBusStopsAsync();

        public Task<List<BusStopNearbyItem>> GetNearbyStopsAsync(double lat, double lng, int radiusM)
        {
            radiusM = Math.Clamp(radiusM, 50, 2000);
            return _dao.GetNearbyStopsAsync(lat, lng, radiusM, 30);
        }

        /// <summary>路線詳情；沒有線形的行駛型態（部分台灣好行）沿道路依序經過站牌算一條，地圖上才不會是直線</summary>
        public async Task<BusRouteDetail> GetRouteDetailAsync(int brId)
        {
            BusRouteDetail route = await _dao.GetRouteDetailAsync(brId);
            if (route == null) return null;

            foreach (var p in route.patterns.Where(p => p.shape.Count < 2 && p.stops.Count >= 2))
            {
                try
                {
                    p.shape = await _valhallaService.GetPathThroughAsync(p.stops.Select(s => (s.lat, s.lng)).ToList(), "bus");
                }
                catch
                {
                    // Valhalla 不能用時維持空線形，前端會改用站牌連線
                }
            }
            return route;
        }

        public Task<List<TaiwanTripperRouteItem>> GetTaiwanTripRoutesAsync() => _dao.GetTaiwanTripRoutesAsync();

        /// <summary>站牌即時到站（直接問 TDX，不存資料庫）；查無站牌回傳 null</summary>
        public async Task<List<BusArrivalItem>> GetArrivalsAsync(int bsId)
        {
            string stopUid = await _dao.GetStopUidAsync(bsId);
            if (stopUid == null) return null;

            string prefix = stopUid.Length >= 3 ? stopUid.Substring(0, 3) : "";
            if (!TdxClient.CityCodes.TryGetValue(prefix, out string area))
                throw new Exception($"無法判斷站牌 {stopUid} 所屬縣市");

            string path = area == "InterCity"
                ? "v2/Bus/EstimatedTimeOfArrival/InterCity"
                : $"v2/Bus/EstimatedTimeOfArrival/City/{area}";
            string filter = Uri.EscapeDataString($"StopUID eq '{stopUid.Replace("'", "''")}'");

            var etas = await _tdx.GetAsync<List<TdxEstimatedArrival>>($"{path}?$filter={filter}") ?? new List<TdxEstimatedArrival>();

            return etas
                .Select(e =>
                {
                    int? minutes = e.StopStatus == 0 && e.EstimateTime.HasValue ? (int)Math.Ceiling(e.EstimateTime.Value / 60.0) : (int?)null;
                    return new BusArrivalItem
                    {
                        route_name = e.RouteName?.Zh_tw,
                        sub_route_name = e.SubRouteName?.Zh_tw,
                        direction = e.Direction,
                        estimate_minutes = minutes,
                        status_text = StatusText(e.StopStatus, e.EstimateTime),
                        next_bus_time = DateTimeOffset.TryParse(e.NextBusTime, out var t) ? t.ToLocalTime().ToString("HH:mm") : null
                    };
                })
                .OrderBy(a => a.estimate_minutes ?? int.MaxValue)
                .ThenBy(a => a.route_name)
                .ToList();
        }

        private static string StatusText(int status, int? seconds)
        {
            switch (status)
            {
                case 0:
                    if (!seconds.HasValue) return "資料更新中";
                    return seconds.Value < 60 ? "進站中" : $"約 {(int)Math.Ceiling(seconds.Value / 60.0)} 分鐘";
                case 1: return "尚未發車";
                case 2: return "交管不停靠";
                case 3: return "末班車已過";
                case 4: return "今日未營運";
                default: return "無資料";
            }
        }

        #endregion
    }
}
