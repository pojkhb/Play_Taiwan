// 檔案路徑：System\Services\Metro\MetroNetwork.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using backend.dao;

namespace backend.Services
{
    /// <summary>
    /// 捷運車站圖（全台，記憶體內）：
    /// 節點 = 車站；邊 = 相鄰車站（metro_station_link 的行駛 + 停靠秒數）＋ 同名且相近的車站互相轉乘。
    /// 用 Dijkstra 找「步行到站 + 候車 + 搭乘 + 轉乘 + 步行到目的地」最短的走法。
    /// </summary>
    public class MetroNetwork
    {
        public const int TransferSeconds = 300;        // 轉乘（含站內走路與再等一班車）
        private const double TransferMaxMeters = 500;  // 同名車站相距多近才算可以轉乘

        public class Line
        {
            public int ml_id { get; set; }
            public string rail_system { get; set; }
            public string system_name { get; set; }
            public string line_no { get; set; }
            public string line_name { get; set; }
            public string color { get; set; }
            public string city_name { get; set; }
        }

        public class Station
        {
            public int mst_id { get; set; }
            public string rail_system { get; set; }
            public string code { get; set; }
            public string name { get; set; }
            public double lat { get; set; }
            public double lng { get; set; }
        }

        public class Edge
        {
            public int from { get; set; }
            public int to { get; set; }
            public int seconds { get; set; }

            /// <summary>所屬路線；轉乘邊為 0</summary>
            public int ml_id { get; set; }

            public bool is_transfer => ml_id == 0;
        }

        /// <summary>找到的走法：起訖步行（直線估算）與依序經過的邊</summary>
        public class Path
        {
            public Station board { get; set; }
            public Station alight { get; set; }
            public List<Edge> edges { get; set; }
            public double ride_seconds { get; set; }
        }

        public Dictionary<int, Line> Lines { get; } = new Dictionary<int, Line>();
        public Dictionary<int, Station> Stations { get; } = new Dictionary<int, Station>();
        public Dictionary<int, List<int>> LineStations { get; } = new Dictionary<int, List<int>>();
        public Dictionary<int, List<List<double[]>>> Shapes { get; } = new Dictionary<int, List<List<double[]>>>();
        private readonly Dictionary<int, List<Edge>> _edges = new Dictionary<int, List<Edge>>();

        public DateTime LoadedAt { get; } = DateTime.UtcNow;


        public MetroNetwork(
            List<MetroDao.LineRow> lines, List<MetroDao.StationRow> stations, List<MetroDao.LineStationRow> lineStations,
            List<MetroDao.LinkRow> links, Dictionary<int, string> shapes)
        {
            foreach (var l in lines)
                Lines[l.ml_id] = new Line
                {
                    ml_id = l.ml_id, rail_system = l.rail_system, system_name = l.system_name,
                    line_no = l.line_no, line_name = l.line_name, color = l.line_color, city_name = l.city_name
                };

            foreach (var s in stations)
                Stations[s.mst_id] = new Station { mst_id = s.mst_id, rail_system = s.rail_system, code = s.station_code, name = s.station_name, lat = s.lat, lng = s.lng };

            foreach (var g in lineStations.Where(ls => Lines.ContainsKey(ls.ml_id)).GroupBy(ls => ls.ml_id))
                LineStations[g.Key] = g.OrderBy(ls => ls.station_sequence).Select(ls => ls.mst_id).ToList();

            foreach (var link in links.Where(l => Lines.ContainsKey(l.ml_id) && Stations.ContainsKey(l.from_mst_id) && Stations.ContainsKey(l.to_mst_id)))
                AddEdge(new Edge { from = link.from_mst_id, to = link.to_mst_id, seconds = link.run_seconds + link.stop_seconds, ml_id = link.ml_id });

            // 同名（臺/台、有無「站」字都算同名）且相距 500 公尺內的車站可以互相轉乘，例如板南線與文湖線的忠孝復興
            var used = Stations.Values.Where(s => _edges.ContainsKey(s.mst_id)).ToList();
            foreach (var group in used.GroupBy(s => NormalizeName(s.name)).Where(g => g.Count() > 1))
            {
                var list = group.ToList();
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        if (Geo.DistanceMeters(list[i].lat, list[i].lng, list[j].lat, list[j].lng) > TransferMaxMeters) continue;
                        AddEdge(new Edge { from = list[i].mst_id, to = list[j].mst_id, seconds = TransferSeconds, ml_id = 0 });
                        AddEdge(new Edge { from = list[j].mst_id, to = list[i].mst_id, seconds = TransferSeconds, ml_id = 0 });
                    }
            }

            foreach (var pair in shapes.Where(s => Lines.ContainsKey(s.Key)))
                Shapes[pair.Key] = ParseWktParts(pair.Value);
        }

        private void AddEdge(Edge e)
        {
            if (!_edges.TryGetValue(e.from, out var list)) _edges[e.from] = list = new List<Edge>();
            list.Add(e);
        }

        public static string NormalizeName(string name) =>
            (name ?? "").Replace("臺", "台").Replace(" ", "").TrimEnd('站').Trim();

        /// <summary>車站的路線（轉乘站在每條線各有一筆車站，所以一站對一條線）</summary>
        public Line LineOf(int mstId)
        {
            foreach (var pair in LineStations)
                if (pair.Value.Contains(mstId) && Lines.TryGetValue(pair.Key, out var line)) return line;
            return null;
        }


        /// <summary>座標附近有列車停靠的車站（直線距離，由近到遠）</summary>
        public List<(Station station, double meters)> StationsNear(double lat, double lng, double radiusM)
        {
            return Stations.Values
                .Where(s => _edges.ContainsKey(s.mst_id))
                .Select(s => (station: s, meters: Geo.DistanceMeters(lat, lng, s.lat, s.lng)))
                .Where(x => x.meters <= radiusM)
                .OrderBy(x => x.meters)
                .ToList();
        }


        /// <summary>
        /// 最短走法。walkSeconds(公尺) 把直線距離換算成步行秒數；起點附近每個車站都當成候選上車站（成本 = 步行 + 候車）。
        /// 找不到、或根本不用搭車時回傳 null。
        /// </summary>
        public Path FindPath(double fromLat, double fromLng, double toLat, double toLng,
            double radiusM, Func<double, double> walkSeconds, double waitSeconds)
        {
            var origins = StationsNear(fromLat, fromLng, radiusM);
            var destinations = StationsNear(toLat, toLng, radiusM).ToDictionary(x => x.station.mst_id, x => walkSeconds(x.meters));
            if (origins.Count == 0 || destinations.Count == 0) return null;

            var dist = new Dictionary<int, double>();
            var prev = new Dictionary<int, Edge>();
            var queue = new PriorityQueue<int, double>();

            foreach (var (station, meters) in origins)
            {
                double cost = walkSeconds(meters) + waitSeconds;
                if (!dist.TryGetValue(station.mst_id, out double old) || cost < old)
                {
                    dist[station.mst_id] = cost;
                    queue.Enqueue(station.mst_id, cost);
                }
            }

            while (queue.TryDequeue(out int node, out double cost))
            {
                if (cost > dist[node]) continue;
                if (!_edges.TryGetValue(node, out var edges)) continue;

                foreach (Edge e in edges)
                {
                    double next = cost + e.seconds;
                    if (!dist.TryGetValue(e.to, out double old) || next < old)
                    {
                        dist[e.to] = next;
                        prev[e.to] = e;
                        queue.Enqueue(e.to, next);
                    }
                }
            }

            int best = -1;
            double bestCost = double.MaxValue;
            foreach (var pair in destinations)
            {
                if (!dist.TryGetValue(pair.Key, out double d)) continue;
                if (d + pair.Value < bestCost) { bestCost = d + pair.Value; best = pair.Key; }
            }
            if (best < 0) return null;

            var path = new List<Edge>();
            for (int node = best; prev.TryGetValue(node, out Edge e); node = e.from)
                path.Insert(0, e);

            // 頭尾的轉乘邊沒有意義（直接走到另一個月台就好），去掉
            while (path.Count > 0 && path[0].is_transfer) path.RemoveAt(0);
            while (path.Count > 0 && path[^1].is_transfer) path.RemoveAt(path.Count - 1);
            if (!path.Any(e => !e.is_transfer)) return null;

            return new Path
            {
                board = Stations[path[0].from],
                alight = Stations[path[^1].to],
                edges = path,
                ride_seconds = path.Sum(e => e.seconds)
            };
        }


        /// <summary>
        /// 兩個相鄰車站之間沿路線線形的座標（[經度, 緯度]）；線形離車站太遠時退回直線。
        /// </summary>
        public List<double[]> SliceShape(int mlId, Station a, Station b)
        {
            var straight = new List<double[]> { new[] { a.lng, a.lat }, new[] { b.lng, b.lat } };
            if (!Shapes.TryGetValue(mlId, out var parts)) return straight;

            List<double[]> bestPart = null;
            int bestA = 0, bestB = 0;
            double bestScore = double.MaxValue;

            foreach (var part in parts)
            {
                var (ia, da) = Geo.NearestVertex(part, a.lat, a.lng, 0);
                var (ib, db) = Geo.NearestVertex(part, b.lat, b.lng, 0);
                if (da + db < bestScore) { bestScore = da + db; bestPart = part; bestA = ia; bestB = ib; }
            }

            if (bestPart == null || bestScore > 600) return straight;

            var coords = new List<double[]> { new[] { a.lng, a.lat } };
            if (bestA <= bestB)
                for (int i = bestA; i <= bestB; i++) coords.Add(bestPart[i]);
            else
                for (int i = bestA; i >= bestB; i--) coords.Add(bestPart[i]);
            coords.Add(new[] { b.lng, b.lat });
            return coords;
        }


        /// <summary>WKT LINESTRING / MULTILINESTRING → 每一段各自的 [經度, 緯度] 清單</summary>
        public static List<List<double[]>> ParseWktParts(string wkt)
        {
            var parts = new List<List<double[]>>();
            if (string.IsNullOrWhiteSpace(wkt)) return parts;

            foreach (Match m in Regex.Matches(wkt, @"\(([^()]+)\)"))
            {
                var points = new List<double[]>();
                foreach (string pair in m.Groups[1].Value.Split(','))
                {
                    string[] xy = pair.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (xy.Length >= 2
                        && double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)
                        && double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
                        points.Add(new[] { lon, lat });
                }
                if (points.Count >= 2) parts.Add(points);
            }
            return parts;
        }
    }


    /// <summary>地理計算小工具</summary>
    public static class Geo
    {
        /// <summary>兩點球面距離（公尺）</summary>
        public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000;
            double dLat = (lat2 - lat1) * Math.PI / 180;
            double dLon = (lon2 - lon1) * Math.PI / 180;
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                     * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * R * Math.Asin(Math.Sqrt(h));
        }

        /// <summary>折線（[經度, 緯度]）上離某點最近的頂點（從 startIndex 開始找）</summary>
        public static (int index, double meters) NearestVertex(List<double[]> line, double lat, double lng, int startIndex)
        {
            int best = -1;
            double bestM = double.MaxValue;
            for (int i = Math.Max(0, startIndex); i < line.Count; i++)
            {
                double m = DistanceMeters(lat, lng, line[i][1], line[i][0]);
                if (m < bestM) { bestM = m; best = i; }
            }
            return (best, bestM);
        }

        /// <summary>折線長度（公里）</summary>
        public static double LengthKm(List<double[]> line)
        {
            double m = 0;
            for (int i = 1; i < line.Count; i++)
                m += DistanceMeters(line[i - 1][1], line[i - 1][0], line[i][1], line[i][0]);
            return m / 1000;
        }
    }
}
