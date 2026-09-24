// 檔案路徑：System\Services\Route\RoutePlanService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend.dao;
using backend.ViewModels;
using Microsoft.Extensions.Logging;

namespace backend.Services
{
    /// <summary>
    /// 劇本交通路線：起點 → 各節點（去程）→ 回到起點（返程），每一段把勾選的交通方式都算一次，採用最快的。
    /// - 步行 / 腳踏車 / 機車 / 汽車：Valhalla 路線（腳踏車會走自行車道、機車不上快速道路）
    /// - 公車：附近站牌之間的直達路線（步行到站 → 候車 → 搭乘 → 步行到目的地）
    /// - 捷運：車站圖最短路徑，可轉乘（步行到站 → 候車 → 搭乘/轉乘 → 步行到目的地）
    /// </summary>
    public class RoutePlanService
    {
        private const double WalkMetersPerMinute = 75;     // 步行約 4.5 km/h
        private const double DetourFactor = 1.25;          // 直線距離換算實際步行距離

        private const int BusStopRadiusM = 500;            // 最遠走多遠去搭公車
        private const int BusAvailableRadiusM = 600;       // 附近多遠內有站牌才算「有公車」
        private const double BusWaitMinutes = 8;           // 市區公車平均候車
        private const double TripperWaitMinutes = 15;      // 台灣好行、客運班距較長
        private const double BusSpeedKmh = 18;             // 市區公車平均速度（含停靠）
        private const double MinMinutesPerBusStop = 1.2;
        private const double MinBusTripMeters = 600;       // 太近的兩點不搭公車

        private const int MetroStationRadiusM = 1200;      // 最遠走多遠去搭捷運
        private const int MetroAvailableRadiusM = 1000;    // 「有捷運的區域」：步行約 15 分鐘內有捷運站
        private const double MetroWaitMinutes = 4;
        private const double MinMetroTripMeters = 800;

        private static readonly string[] RoadModes = { TransportModes.Walk, TransportModes.Bicycle, TransportModes.Scooter, TransportModes.Car };

        private readonly ValhallaService _valhalla;
        private readonly MetroService _metro;
        private readonly RouteDao _dao;
        private readonly Neo4jService _neo4j;
        private readonly ILogger<RoutePlanService> _logger;

        public RoutePlanService(ValhallaService valhalla, MetroService metro, RouteDao dao, Neo4jService neo4j, ILogger<RoutePlanService> logger)
        {
            _valhalla = valhalla;
            _metro = metro;
            _dao = dao;
            _neo4j = neo4j;
            _logger = logger;
        }


        #region 交通方式可用性

        /// <summary>
        /// 各交通方式在這些點附近能不能用：步行/腳踏車/機車/汽車都能用；
        /// 公車要有站牌（且匯入了該縣市的公車資料）；捷運要有至少兩個點步行 15 分鐘內有捷運站（只有一個點時該點附近有站即可）。
        /// </summary>
        public async Task<List<TransportAvailability>> GetAvailabilityAsync(List<RoutePoint> points)
        {
            points = (points ?? new List<RoutePoint>()).Where(p => p != null && p.lat != 0 && p.lng != 0).ToList();

            var result = RoadModes.Select(m => new TransportAvailability
            {
                mode = m, enabled = true, reason = "隨時可用", color = TransportModes.Color(m)
            }).ToList();

            // ── 公車 ──
            RouteDao.StopRow nearestStop = null;
            foreach (RoutePoint p in points)
            {
                var hub = (await _dao.GetBusHubsNearAsync(p.lat, p.lng, BusAvailableRadiusM, 30)).OrderBy(s => s.distance_m).FirstOrDefault();
                if (hub != null && (nearestStop == null || hub.distance_m < nearestStop.distance_m)) nearestStop = hub;
            }

            result.Add(new TransportAvailability
            {
                mode = TransportModes.Bus,
                enabled = nearestStop != null,
                reason = nearestStop != null
                    ? $"附近 {Math.Round(nearestStop.distance_m)} 公尺有「{nearestStop.stop_name}」站牌"
                    : $"這一帶 {BusAvailableRadiusM} 公尺內沒有公車站牌，或尚未匯入這個縣市的公車資料",
                nearest_name = nearestStop?.stop_name,
                nearest_distance_m = nearestStop == null ? (int?)null : (int)Math.Round(nearestStop.distance_m),
                color = TransportModes.Color(TransportModes.Bus)
            });

            // ── 捷運 ──
            MetroNetwork network = await _metro.GetNetworkAsync();
            var nearest = points
                .Select(p => network.StationsNear(p.lat, p.lng, MetroAvailableRadiusM).FirstOrDefault())
                .Where(x => x.station != null)
                .ToList();
            int required = Math.Min(2, Math.Max(1, points.Count));
            var closest = nearest.OrderBy(x => x.meters).FirstOrDefault();
            bool metroEnabled = nearest.Count >= required;

            result.Add(new TransportAvailability
            {
                mode = TransportModes.Metro,
                enabled = metroEnabled,
                reason = metroEnabled
                    ? $"附近 {Math.Round(closest.meters)} 公尺有{StationLabel(network, closest.station)}"
                    : closest.station != null
                        ? $"只有一個地點附近有捷運（{StationLabel(network, closest.station)}），搭不到其他地點"
                        : "這一帶步行 15 分鐘內沒有捷運站",
                nearest_name = closest.station == null ? null : StationLabel(network, closest.station),
                nearest_distance_m = closest.station == null ? (int?)null : (int)Math.Round(closest.meters),
                color = TransportModes.Color(TransportModes.Metro)
            });

            return result;
        }

        public async Task<List<TransportAvailability>> GetAvailabilityAsync(double lat, double lng, int? storyId)
        {
            var points = new List<RoutePoint>();
            if (lat != 0 && lng != 0) points.Add(new RoutePoint { lat = lat, lng = lng });

            if (storyId.HasValue)
            {
                var (_, stops, _) = await ResolveStoryAsync(storyId.Value);
                points.AddRange(stops.Select(s => new RoutePoint { lat = s.lat, lng = s.lng, name = s.name }));
            }

            return await GetAvailabilityAsync(points);
        }

        private static string StationLabel(MetroNetwork network, MetroNetwork.Station s)
        {
            string system = network.LineOf(s.mst_id)?.system_name ?? "捷運";
            return $"{system}「{s.name}」站";
        }

        #endregion


        #region 劇本清單

        /// <summary>可以規劃交通路線的劇本（含節點座標與建議起點）</summary>
        public async Task<List<RouteStoryItem>> GetStoriesAsync(int auId)
        {
            List<RouteDao.StoryRow> stories = await _dao.GetStoriesAsync(auId, 30);
            List<RouteDao.NodeRow> nodes = await _dao.GetStoryNodesAsync(stories.Select(s => s.s_id));
            var points = await ResolvePlacePointsAsync(nodes.Select(n => n.place_id));

            var result = new List<RouteStoryItem>();
            foreach (RouteDao.StoryRow s in stories)
            {
                List<RouteStopPoint> stops = ToStops(nodes.Where(n => n.s_id == s.s_id), points, null);
                if (stops.Count == 0) continue;

                result.Add(new RouteStoryItem
                {
                    story_id = s.s_id,
                    story_title = s.story_title,
                    city_name = s.city_name,
                    district_name = s.district_name,
                    node_count = stops.Count,
                    nodes = stops,
                    suggested_start = await SuggestStartAsync(stops[0])
                });
            }
            return result;
        }

        /// <summary>建議起點：第一站附近的捷運站，沒有的話找附近最多路線經過的公車站，都沒有就用第一站</summary>
        private async Task<RoutePoint> SuggestStartAsync(RouteStopPoint first)
        {
            MetroNetwork network = await _metro.GetNetworkAsync();
            var station = network.StationsNear(first.lat, first.lng, MetroStationRadiusM).FirstOrDefault();
            if (station.station != null)
                return new RoutePoint { lat = station.station.lat, lng = station.station.lng, name = StationLabel(network, station.station) };

            var hub = (await _dao.GetBusHubsNearAsync(first.lat, first.lng, 800, 1)).FirstOrDefault();
            if (hub != null)
                return new RoutePoint { lat = hub.lat, lng = hub.lng, name = $"「{hub.stop_name}」公車站" };

            return new RoutePoint { lat = first.lat, lng = first.lng, name = first.name };
        }

        #endregion


        #region 路線規劃

        public async Task<RoutePlanResponse> PlanAsync(RoutePlanRequest req)
        {
            if (req == null) throw new ArgumentException("請提供規劃條件");

            var warnings = new List<string>();
            var response = new RoutePlanResponse { story_id = req.story_id, warnings = warnings };

            // ── 1. 要去的點 ──
            List<RouteStopPoint> stops;
            if (req.story_id.HasValue)
            {
                var (story, storyStops, storyWarnings) = await ResolveStoryAsync(req.story_id.Value);
                response.story_title = story.story_title;
                stops = storyStops;
                warnings.AddRange(storyWarnings);
            }
            else
            {
                stops = (req.points ?? new List<RoutePoint>())
                    .Where(p => p != null && p.lat != 0 && p.lng != 0)
                    .Select((p, i) => new RouteStopPoint { order = i + 1, name = string.IsNullOrWhiteSpace(p.name) ? $"第 {i + 1} 站" : p.name, lat = p.lat, lng = p.lng })
                    .ToList();
            }

            if (stops.Count == 0)
                throw new ArgumentException(req.story_id.HasValue ? "這個劇本的節點都查不到座標，無法規劃路線" : "請提供至少 1 個要去的點");

            RoutePoint start = req.start != null && req.start.lat != 0 && req.start.lng != 0
                ? new RoutePoint { lat = req.start.lat, lng = req.start.lng, name = string.IsNullOrWhiteSpace(req.start.name) ? "起點" : req.start.name }
                : req.story_id.HasValue ? await SuggestStartAsync(stops[0]) : null;

            if (start == null) throw new ArgumentException("請提供起點 start");

            response.start = start;
            response.stops = stops;

            // ── 2. 交通方式（公車/捷運在這一帶不能用就略過）──
            List<string> modes = (req.transportation ?? new List<string>())
                .Select(t => (t ?? "").Trim())
                .Select(t => t == "自行車" ? TransportModes.Bicycle : t == "開車" || t == "自駕" ? TransportModes.Car : t)
                .Where(t => TransportModes.All.Contains(t))
                .Distinct()
                .ToList();
            if (modes.Count == 0) modes.Add(TransportModes.Walk);

            var allPoints = new List<RoutePoint> { start }.Concat(stops.Select(s => new RoutePoint { lat = s.lat, lng = s.lng })).ToList();
            if (modes.Contains(TransportModes.Bus) || modes.Contains(TransportModes.Metro))
            {
                var availability = await GetAvailabilityAsync(allPoints);
                foreach (string mode in new[] { TransportModes.Bus, TransportModes.Metro })
                {
                    var a = availability.First(x => x.mode == mode);
                    if (modes.Contains(mode) && !a.enabled)
                    {
                        modes.Remove(mode);
                        warnings.Add($"{mode}：{a.reason}，已略過");
                    }
                }
                if (modes.Count == 0) modes.Add(TransportModes.Walk);
            }

            // ── 3. 每一段各自規劃 ──
            var waypoints = new List<(RoutePoint point, string direction)> { (start, null) };
            waypoints.AddRange(stops.Select(s => (new RoutePoint { lat = s.lat, lng = s.lng, name = s.name }, "去程")));
            if (req.include_return) waypoints.Add((start, "返程"));

            RouteLeg[] legs = await Task.WhenAll(Enumerable.Range(1, waypoints.Count - 1).Select(i =>
                PlanLegAsync(i, waypoints[i - 1].point, waypoints[i].point, waypoints[i].direction, modes)));

            response.legs = legs.ToList();
            response.outbound_minutes = Math.Round(legs.Where(l => l.direction == "去程").Sum(l => l.minutes), 1);
            response.return_minutes = Math.Round(legs.Where(l => l.direction == "返程").Sum(l => l.minutes), 1);
            response.total_minutes = Math.Round(response.outbound_minutes + response.return_minutes, 1);
            response.total_distance_km = Math.Round(legs.Sum(l => l.distance_km), 2);
            response.modes_used = legs.SelectMany(l => l.segments).Select(s => s.mode)
                .Where(m => m != TransportModes.Transfer).Distinct()
                .OrderBy(m => Array.IndexOf(TransportModes.All, m)).ToList();

            return response;
        }


        /// <summary>一段路：每個交通方式各算一個方案，採用最快的</summary>
        private async Task<RouteLeg> PlanLegAsync(int order, RoutePoint from, RoutePoint to, string direction, List<string> modes)
        {
            var tasks = new List<Task<RouteOption>>();
            foreach (string mode in RoadModes.Where(modes.Contains))
                tasks.Add(SafeAsync(mode, () => PlanRoadAsync(from, to, mode)));
            if (modes.Contains(TransportModes.Bus))
                tasks.Add(SafeAsync(TransportModes.Bus, () => PlanBusAsync(from, to)));
            if (modes.Contains(TransportModes.Metro))
                tasks.Add(SafeAsync(TransportModes.Metro, () => PlanMetroAsync(from, to)));

            List<RouteOption> options = (await Task.WhenAll(tasks)).Where(o => o != null).ToList();

            // 只勾公車/捷運但這段搭不到時，退回步行
            if (options.Count == 0)
                options.Add(await SafeAsync(TransportModes.Walk, () => PlanRoadAsync(from, to, TransportModes.Walk)) ?? StraightWalkOption(from, to));

            RouteOption chosen = options.OrderBy(o => o.minutes).First();
            chosen.is_chosen = true;

            return new RouteLeg
            {
                leg_order = order,
                direction = direction,
                from_name = from.name,
                to_name = to.name,
                mode = chosen.mode,
                minutes = chosen.minutes,
                distance_km = chosen.distance_km,
                summary = chosen.summary,
                segments = chosen.segments,
                options = options.OrderByDescending(o => o.is_chosen).ThenBy(o => o.minutes).ToList()
            };
        }

        private async Task<RouteOption> SafeAsync(string mode, Func<Task<RouteOption>> plan)
        {
            try
            {
                return await plan();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("規劃{Mode}路線失敗：{Message}", mode, ex.Message);
                return null;
            }
        }


        /// <summary>步行 / 腳踏車 / 機車 / 汽車：Valhalla 直接算</summary>
        private async Task<RouteOption> PlanRoadAsync(RoutePoint from, RoutePoint to, string mode)
        {
            ValhallaRoute route = await _valhalla.GetRouteAsync(new List<(double lat, double lng)> { (from.lat, from.lng), (to.lat, to.lng) }, ValhallaService.ToCosting(mode));
            ValhallaRouteLeg leg = route.legs.FirstOrDefault();
            if (leg == null) return null;

            double minutes = Math.Round(leg.seconds / 60, 1);
            double km = Math.Round(leg.km, 2);
            string verb = mode == TransportModes.Walk ? "步行" : mode == TransportModes.Car ? "開車" : $"騎{mode}";

            return new RouteOption
            {
                mode = mode,
                minutes = minutes,
                distance_km = km,
                summary = $"{verb} {km} 公里，約 {Math.Ceiling(minutes)} 分鐘",
                segments = new List<RouteSegment>
                {
                    new RouteSegment
                    {
                        mode = mode, color = TransportModes.Color(mode), minutes = minutes, distance_km = km,
                        coordinates = leg.coordinates,
                        instruction = $"從「{from.name}」{verb}到「{to.name}」，{km} 公里約 {Math.Ceiling(minutes)} 分鐘"
                    }
                }
            };
        }


        /// <summary>公車：起點附近站牌上車、終點附近站牌下車的直達路線，挑總時間最短的</summary>
        private async Task<RouteOption> PlanBusAsync(RoutePoint from, RoutePoint to)
        {
            if (Geo.DistanceMeters(from.lat, from.lng, to.lat, to.lng) < MinBusTripMeters) return null;

            var boardStops = await _dao.GetBusStopsNearAsync(from.lat, from.lng, BusStopRadiusM, 30);
            var alightStops = await _dao.GetBusStopsNearAsync(to.lat, to.lng, BusStopRadiusM, 30);
            if (boardStops.Count == 0 || alightStops.Count == 0) return null;

            var candidates = await _dao.FindDirectBusAsync(boardStops.Select(s => s.bs_id).ToList(), alightStops.Select(s => s.bs_id).ToList());
            if (candidates.Count == 0) return null;

            var boardById = boardStops.ToDictionary(s => s.bs_id);
            var alightById = alightStops.ToDictionary(s => s.bs_id);

            var best = candidates
                .Select(c =>
                {
                    var b = boardById[c.board_bs_id];
                    var a = alightById[c.alight_bs_id];
                    int stopCount = c.alight_seq - c.board_seq;
                    double ride = Math.Max(stopCount * MinMinutesPerBusStop,
                        Geo.DistanceMeters(b.lat, b.lng, a.lat, a.lng) * 1.3 / 1000 / BusSpeedKmh * 60);
                    double total = WalkMinutes(b.distance_m) + WaitMinutes(c.route_type) + ride + WalkMinutes(a.distance_m);
                    return (row: c, board: b, alight: a, total);
                })
                .OrderBy(x => x.total)
                .First();

            RouteDao.DirectBusRow info = best.row;
            var stops = await _dao.GetPatternStopsAsync(info.brp_id, info.board_seq, info.alight_seq);
            if (stops.Count < 2) return null;

            // 有線形就切出上下車站之間那段；沒有線形（部分台灣好行）或切不出來時，沿道路依序經過站牌，不畫直線
            List<double[]> coords = BusDao.SliceShape(await _dao.GetPatternShapeAsync(info.brp_id),
                    stops[0].lat, stops[0].lng, stops[^1].lat, stops[^1].lng)
                ?? await RoadPathThroughStopsAsync(stops);
            double rideKm = Math.Round(Geo.LengthKm(coords), 2);
            int stopCountFinal = stops.Count - 1;
            double rideMinutes = Math.Round(Math.Max(stopCountFinal * MinMinutesPerBusStop, rideKm / BusSpeedKmh * 60), 1);
            double wait = WaitMinutes(info.route_type);

            var boardPoint = new RoutePoint { lat = stops[0].lat, lng = stops[0].lng, name = stops[0].stop_name };
            var alightPoint = new RoutePoint { lat = stops[^1].lat, lng = stops[^1].lng, name = stops[^1].stop_name };

            RouteSegment walkA = await WalkSegmentAsync(from, boardPoint, $"步行到「{boardPoint.name}」站牌");
            RouteSegment walkB = await WalkSegmentAsync(alightPoint, to, $"下車後步行到「{to.name}」");

            string routeTypeName = StoryDao.RouteTypeName(info.route_type);
            string headsign = string.IsNullOrWhiteSpace(info.headsign) ? null : $"往 {info.headsign.Replace("往", "").Trim()}";

            var busSegment = new RouteSegment
            {
                mode = TransportModes.Bus,
                color = TransportModes.Color(TransportModes.Bus),
                minutes = rideMinutes,
                wait_minutes = wait,
                distance_km = rideKm,
                coordinates = coords,
                route_name = info.route_name,
                route_type_name = routeTypeName,
                headsign = headsign,
                board_name = boardPoint.name,
                alight_name = alightPoint.name,
                stop_count = stopCountFinal,
                stops = stops.Select(s => new RoutePoint { lat = s.lat, lng = s.lng, name = s.stop_name }).ToList(),
                instruction = $"在「{boardPoint.name}」搭 {((info.route_name ?? "").StartsWith(routeTypeName) ? "" : routeTypeName + " ")}{info.route_name}{(headsign == null ? "" : $"（{headsign}）")}，坐 {stopCountFinal} 站到「{alightPoint.name}」下車"
            };

            return BuildTransitOption(TransportModes.Bus, walkA, new List<RouteSegment> { busSegment }, walkB,
                $"{info.route_name} {stopCountFinal} 站");
        }

        /// <summary>公車沿道路（Valhalla bus）依序經過每個站牌的路線；Valhalla 失敗時才用站牌直線連線</summary>
        private async Task<List<double[]>> RoadPathThroughStopsAsync(List<RouteDao.PatternStopRow> stops)
        {
            try
            {
                List<double[]> coords = await _valhalla.GetPathThroughAsync(stops.Select(s => (s.lat, s.lng)).ToList(), "bus");
                if (coords.Count >= 2) return coords;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("公車沿道路路線計算失敗，改用站牌連線：{Message}", ex.Message);
            }
            return stops.Select(s => new[] { s.lng, s.lat }).ToList();
        }

        /// <summary>捷運：車站圖最短路徑（可轉乘）</summary>
        private async Task<RouteOption> PlanMetroAsync(RoutePoint from, RoutePoint to)
        {
            if (Geo.DistanceMeters(from.lat, from.lng, to.lat, to.lng) < MinMetroTripMeters) return null;

            MetroNetwork network = await _metro.GetNetworkAsync();
            MetroNetwork.Path path = network.FindPath(from.lat, from.lng, to.lat, to.lng, MetroStationRadiusM,
                meters => WalkMinutes(meters) * 60, MetroWaitMinutes * 60);
            if (path == null) return null;

            // 連續同一條線的邊合併成一段搭乘，轉乘邊獨立成一段
            var groups = new List<List<MetroNetwork.Edge>>();
            foreach (var edge in path.edges)
            {
                if (groups.Count > 0 && !edge.is_transfer && !groups[^1][0].is_transfer && groups[^1][0].ml_id == edge.ml_id)
                    groups[^1].Add(edge);
                else
                    groups.Add(new List<MetroNetwork.Edge> { edge });
            }

            var rides = new List<RouteSegment>();
            bool firstRide = true;
            foreach (var group in groups)
            {
                MetroNetwork.Station a = network.Stations[group[0].from];
                MetroNetwork.Station b = network.Stations[group[^1].to];

                if (group[0].is_transfer)
                {
                    string toLine = network.LineOf(b.mst_id)?.line_name ?? "其他路線";
                    rides.Add(new RouteSegment
                    {
                        mode = TransportModes.Transfer,
                        color = TransportModes.Color(TransportModes.Transfer),
                        minutes = MetroNetwork.TransferSeconds / 60.0,
                        distance_km = Math.Round(Geo.DistanceMeters(a.lat, a.lng, b.lat, b.lng) / 1000, 2),
                        coordinates = new List<double[]> { new[] { a.lng, a.lat }, new[] { b.lng, b.lat } },
                        board_name = a.name,
                        alight_name = b.name,
                        instruction = $"在「{a.name}」站轉乘{toLine}"
                    });
                    continue;
                }

                MetroNetwork.Line line = network.Lines[group[0].ml_id];
                var stations = new List<MetroNetwork.Station> { a };
                stations.AddRange(group.Select(e => network.Stations[e.to]));

                var coords = new List<double[]>();
                for (int i = 1; i < stations.Count; i++)
                {
                    var hop = network.SliceShape(line.ml_id, stations[i - 1], stations[i]);
                    coords.AddRange(i == 1 ? hop : hop.Skip(1));
                }

                double minutes = Math.Round(group.Sum(e => e.seconds) / 60.0, 1);
                rides.Add(new RouteSegment
                {
                    mode = TransportModes.Metro,
                    color = string.IsNullOrWhiteSpace(line.color) ? TransportModes.Color(TransportModes.Metro) : line.color,
                    minutes = minutes,
                    wait_minutes = firstRide ? MetroWaitMinutes : 0,
                    distance_km = Math.Round(Geo.LengthKm(coords), 2),
                    coordinates = coords,
                    route_name = line.line_name,
                    route_type_name = line.system_name,
                    board_name = a.name,
                    alight_name = b.name,
                    stop_count = group.Count,
                    stops = stations.Select(s => new RoutePoint { lat = s.lat, lng = s.lng, name = s.name }).ToList(),
                    instruction = $"在「{a.name}」站搭{line.system_name}{line.line_name}，坐 {group.Count} 站到「{b.name}」站"
                });
                firstRide = false;
            }

            var boardPoint = new RoutePoint { lat = path.board.lat, lng = path.board.lng, name = path.board.name };
            var alightPoint = new RoutePoint { lat = path.alight.lat, lng = path.alight.lng, name = path.alight.name };
            RouteSegment walkA = await WalkSegmentAsync(from, boardPoint, $"步行到捷運「{boardPoint.name}」站");
            RouteSegment walkB = await WalkSegmentAsync(alightPoint, to, $"出站後步行到「{to.name}」");

            var lineNames = rides.Where(r => r.mode == TransportModes.Metro).Select(r => r.route_name).Distinct();
            int transfers = rides.Count(r => r.mode == TransportModes.Transfer);
            return BuildTransitOption(TransportModes.Metro, walkA, rides, walkB,
                $"{string.Join("→", lineNames)}{(transfers > 0 ? $"（轉乘 {transfers} 次）" : "")}");
        }


        /// <summary>步行到站 + 搭乘 + 步行到目的地 組成一個方案</summary>
        private static RouteOption BuildTransitOption(string mode, RouteSegment walkA, List<RouteSegment> rides, RouteSegment walkB, string rideLabel)
        {
            var segments = new List<RouteSegment>();
            if (walkA != null) segments.Add(walkA);
            segments.AddRange(rides);
            if (walkB != null) segments.Add(walkB);

            double minutes = Math.Round(segments.Sum(s => s.minutes + s.wait_minutes), 1);
            double km = Math.Round(segments.Sum(s => s.distance_km), 2);

            var parts = new List<string>();
            if (walkA != null) parts.Add($"步行 {Math.Ceiling(walkA.minutes)} 分");
            parts.Add($"{mode} {rideLabel}");
            if (walkB != null) parts.Add($"步行 {Math.Ceiling(walkB.minutes)} 分");

            return new RouteOption
            {
                mode = mode,
                minutes = minutes,
                distance_km = km,
                summary = $"{string.Join(" → ", parts)}，共約 {Math.Ceiling(minutes)} 分鐘（含候車）",
                segments = segments
            };
        }


        /// <summary>兩點之間的步行段（Valhalla），太近（30 公尺內）不算一段；Valhalla 失敗時用直線估算</summary>
        private async Task<RouteSegment> WalkSegmentAsync(RoutePoint a, RoutePoint b, string instruction)
        {
            double straight = Geo.DistanceMeters(a.lat, a.lng, b.lat, b.lng);
            if (straight < 30) return null;

            try
            {
                var route = await _valhalla.GetRouteAsync(new List<(double lat, double lng)> { (a.lat, a.lng), (b.lat, b.lng) }, "pedestrian");
                var leg = route.legs.FirstOrDefault();
                if (leg != null)
                    return new RouteSegment
                    {
                        mode = TransportModes.Walk, color = TransportModes.Color(TransportModes.Walk),
                        minutes = Math.Round(leg.seconds / 60, 1), distance_km = Math.Round(leg.km, 2),
                        coordinates = leg.coordinates, instruction = instruction
                    };
            }
            catch (Exception ex)
            {
                _logger.LogWarning("步行路線計算失敗，改用直線估算：{Message}", ex.Message);
            }

            return new RouteSegment
            {
                mode = TransportModes.Walk, color = TransportModes.Color(TransportModes.Walk),
                minutes = Math.Round(WalkMinutes(straight), 1), distance_km = Math.Round(straight * DetourFactor / 1000, 2),
                coordinates = new List<double[]> { new[] { a.lng, a.lat }, new[] { b.lng, b.lat } }, instruction = instruction
            };
        }

        private static RouteOption StraightWalkOption(RoutePoint from, RoutePoint to)
        {
            double meters = Geo.DistanceMeters(from.lat, from.lng, to.lat, to.lng);
            double minutes = Math.Round(WalkMinutes(meters), 1);
            double km = Math.Round(meters * DetourFactor / 1000, 2);
            return new RouteOption
            {
                mode = TransportModes.Walk,
                minutes = minutes,
                distance_km = km,
                summary = $"步行約 {Math.Ceiling(minutes)} 分鐘（直線估算）",
                segments = new List<RouteSegment>
                {
                    new RouteSegment
                    {
                        mode = TransportModes.Walk, color = TransportModes.Color(TransportModes.Walk), minutes = minutes, distance_km = km,
                        coordinates = new List<double[]> { new[] { from.lng, from.lat }, new[] { to.lng, to.lat } },
                        instruction = $"從「{from.name}」步行到「{to.name}」"
                    }
                }
            };
        }

        private static double WalkMinutes(double straightMeters) => straightMeters * DetourFactor / WalkMetersPerMinute;

        private static double WaitMinutes(int routeType) => routeType == 1 ? BusWaitMinutes : TripperWaitMinutes;

        #endregion


        #region 劇本節點座標

        /// <summary>劇本與依順序排列的節點座標；查不到座標的節點略過並回傳提醒</summary>
        private async Task<(RouteDao.StoryRow story, List<RouteStopPoint> stops, List<string> warnings)> ResolveStoryAsync(int storyId)
        {
            RouteDao.StoryRow story = await _dao.GetStoryAsync(storyId)
                ?? throw new ArgumentException($"找不到 story_id = {storyId} 的劇本");

            List<RouteDao.NodeRow> nodes = await _dao.GetStoryNodesAsync(new[] { storyId });
            var points = await ResolvePlacePointsAsync(nodes.Select(n => n.place_id));

            var warnings = new List<string>();
            List<RouteStopPoint> stops = ToStops(nodes, points, warnings);
            return (story, stops, warnings);
        }

        private static List<RouteStopPoint> ToStops(IEnumerable<RouteDao.NodeRow> nodes, Dictionary<string, (string name, double? lat, double? lng)> points, List<string> warnings)
        {
            var stops = new List<RouteStopPoint>();
            foreach (RouteDao.NodeRow n in nodes.OrderBy(n => n.sn_order).ThenBy(n => n.sn_id))
            {
                if (n.place_id != null && points.TryGetValue(n.place_id, out var p) && p.lat.HasValue && p.lng.HasValue)
                {
                    stops.Add(new RouteStopPoint
                    {
                        sn_id = n.sn_id, order = stops.Count + 1, name = p.name ?? n.sn_title, title = n.sn_title, lat = p.lat.Value, lng = p.lng.Value
                    });
                }
                else
                {
                    warnings?.Add($"「{n.sn_title}」查不到座標，已略過");
                }
            }
            return stops;
        }

        /// <summary>景點 uuid → 名稱與座標：先查 MySQL（place_type → place），查不到的再問 Neo4j</summary>
        private async Task<Dictionary<string, (string name, double? lat, double? lng)>> ResolvePlacePointsAsync(IEnumerable<string> placeIds)
        {
            var ids = placeIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
            var result = await _dao.GetPlacePointsAsync(ids);

            var missing = ids.Where(id => !result.TryGetValue(id, out var p) || !p.lat.HasValue).ToList();
            if (missing.Count == 0) return result;

            var fromNeo4j = await _neo4j.ExecuteCypherAsync<List<ReachableAttractionNode>>(@"
                MATCH (a:Attraction) WHERE a.uid IN $ids AND a.lat IS NOT NULL AND a.lon IS NOT NULL
                RETURN a.uid AS uid, a.name AS name, a.lat AS lat, a.lon AS lon", new { ids = missing });

            foreach (var a in fromNeo4j ?? new List<ReachableAttractionNode>())
                if (!string.IsNullOrWhiteSpace(a.uid)) result[a.uid] = (a.name, a.lat, a.lon);

            return result;
        }

        #endregion
    }
}
