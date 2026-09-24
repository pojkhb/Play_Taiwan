// 檔案路徑：System\Services\Metro\MetroService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using backend.dao;
using backend.ViewModels;
using Microsoft.Extensions.Logging;

namespace backend.Services
{
    /// <summary>
    /// 捷運 / 輕軌：從 TDX 同步資料、提供記憶體內的車站圖給路線規劃。
    /// </summary>
    public class MetroService
    {
        /// <summary>TDX RailSystem 代碼 → 系統名稱（貓空纜車不是通勤運具，不列入）</summary>
        public static readonly Dictionary<string, string> SystemNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "TRTC", "臺北捷運" }, { "NTMC", "新北捷運" }, { "TYMC", "桃園機場捷運" }, { "TMRT", "臺中捷運" },
            { "KRTC", "高雄捷運" }, { "KLRT", "高雄輕軌" }, { "NTDLRT", "淡海輕軌" }, { "NTALRT", "安坑輕軌" }
        };

        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
        private static readonly SemaphoreSlim CacheLock = new SemaphoreSlim(1, 1);
        private static MetroNetwork _network;

        private readonly TdxClient _tdx;
        private readonly MetroDao _dao;
        private readonly ILogger<MetroService> _logger;

        public MetroService(TdxClient tdx, MetroDao dao, ILogger<MetroService> logger)
        {
            _tdx = tdx;
            _dao = dao;
            _logger = logger;
        }


        #region 同步

        /// <summary>同步一個捷運系統（路線、車站、站序、站間行駛時間、線形）</summary>
        public async Task<MetroSyncResult> SyncAsync(string railSystem)
        {
            string key = SystemNames.Keys.FirstOrDefault(k => string.Equals(k, (railSystem ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"不支援的捷運系統：{railSystem}，可用：{string.Join("、", SystemNames.Keys)}");

            var sw = Stopwatch.StartNew();

            var lines = await _tdx.GetAsync<List<TdxMetroLine>>($"v2/Rail/Metro/Line/{key}");
            var stations = await _tdx.GetAsync<List<TdxMetroStation>>($"v2/Rail/Metro/Station/{key}");
            var stationOfLines = await _tdx.GetAsync<List<TdxMetroStationOfLine>>($"v2/Rail/Metro/StationOfLine/{key}");

            // 站間行駛時間、線形不是每個系統都有，拿不到就用估算 / 直線
            var travelTimes = await TryGetAsync<List<TdxMetroTravelTime>>($"v2/Rail/Metro/S2STravelTime/{key}");
            var shapes = await TryGetAsync<List<TdxMetroShape>>($"v2/Rail/Metro/Shape/{key}");

            MetroSyncResult result = await _dao.SaveMetroDataAsync(key, SystemNames[key], lines, stations, stationOfLines, travelTimes, shapes);
            result.elapsed_seconds = Math.Round(sw.Elapsed.TotalSeconds, 1);

            Invalidate();
            return result;
        }

        private async Task<T> TryGetAsync<T>(string path) where T : class
        {
            try
            {
                return await _tdx.GetAsync<T>(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("TDX {Path} 取不到資料，改用估算：{Message}", path, ex.Message);
                return null;
            }
        }

        public Task<int> CountStationsAsync() => _dao.CountStationsAsync();

        /// <summary>
        /// 需要（重新）同步的系統：還沒有資料、沒有任何站間連線，或連線數異常
        /// （平均每站超過 4 條，代表存到了「跳站」的組合，是舊版同步留下的資料）。
        /// </summary>
        public async Task<List<string>> GetSystemsNeedingSyncAsync(IEnumerable<string> systems)
        {
            var stats = await _dao.GetSystemStatsAsync();
            return systems.Where(s =>
                !stats.TryGetValue(s, out var st) || st.links == 0 || st.links > st.stations * 4).ToList();
        }

        #endregion


        #region 車站圖

        /// <summary>取得車站圖（快取 6 小時，同步後立即重建）</summary>
        public async Task<MetroNetwork> GetNetworkAsync()
        {
            var cached = _network;
            if (cached != null && DateTime.UtcNow - cached.LoadedAt < CacheLifetime) return cached;

            await CacheLock.WaitAsync();
            try
            {
                if (_network != null && DateTime.UtcNow - _network.LoadedAt < CacheLifetime) return _network;

                var (lines, stations, lineStations, links, shapes) = await _dao.LoadNetworkAsync();
                _network = new MetroNetwork(lines, stations, lineStations, links, shapes);
                return _network;
            }
            finally
            {
                CacheLock.Release();
            }
        }

        public static void Invalidate() => _network = null;


        /// <summary>座標附近（radiusKm 內有車站）的捷運路線，含車站與線形，畫捷運路網用</summary>
        public async Task<List<MetroLineItem>> GetLinesNearAsync(double lat, double lng, double radiusKm)
        {
            MetroNetwork network = await GetNetworkAsync();
            var nearStations = network.StationsNear(lat, lng, radiusKm * 1000).Select(x => x.station.mst_id).ToHashSet();

            return network.LineStations
                .Where(pair => pair.Value.Any(nearStations.Contains) && network.Lines.ContainsKey(pair.Key))
                .Select(pair =>
                {
                    MetroNetwork.Line line = network.Lines[pair.Key];
                    return new MetroLineItem
                    {
                        ml_id = line.ml_id,
                        system_name = line.system_name,
                        line_no = line.line_no,
                        line_name = line.line_name,
                        line_color = line.color,
                        stations = pair.Value.Where(network.Stations.ContainsKey).Select(id => network.Stations[id]).Select(s => new MetroStationItem
                        {
                            mst_id = s.mst_id, station_code = s.code, station_name = s.name, lat = s.lat, lng = s.lng
                        }).ToList(),
                        shape = network.Shapes.TryGetValue(line.ml_id, out var parts) ? parts : new List<List<double[]>>()
                    };
                })
                .OrderBy(l => l.system_name).ThenBy(l => l.line_no)
                .ToList();
        }

        #endregion
    }
}
