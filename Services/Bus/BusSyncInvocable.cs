// 檔案路徑：System\Services\Bus\BusSyncInvocable.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coravel.Invocable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace backend.Services
{
    /// <summary>
    /// 公車資料排程同步（Coravel）：依 appsettings.json 的 BusSync 設定，
    /// 同步指定縣市的市區公車與台灣好行，再重算景點附近站牌。任何一步失敗只記 log，不影響其他步驟。
    /// </summary>
    public class BusSyncInvocable : IInvocable
    {
        private readonly BusService _busService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BusSyncInvocable> _logger;

        public BusSyncInvocable(BusService busService, IConfiguration configuration, ILogger<BusSyncInvocable> logger)
        {
            _busService = busService;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>完整排程：同步設定的縣市與台灣好行，再重算景點附近站牌</summary>
        public async Task Invoke()
        {
            foreach (string city in Cities())
                await RunAsync($"同步 {city} 公車", async () => _logger.LogInformation("{Result}", Describe(await _busService.SyncCityAsync(city))));

            if (IncludeTaiwanTrip())
                await RunAsync("同步台灣好行", async () => _logger.LogInformation("{Result}", Describe(await _busService.SyncTaiwanTripAsync())));

            await BuildPlaceStopsAsync();
        }

        /// <summary>
        /// 啟動時補資料：完全沒有公車路線 → 跑完整排程；有路線但還沒有景點附近站牌 → 只補算站牌
        /// （例如第一次同步時 AI service 的 Neo4j 連不上）。
        /// </summary>
        public async Task InvokeIfMissingAsync()
        {
            if (await _busService.CountActiveRoutesAsync() == 0)
            {
                _logger.LogInformation("資料庫還沒有公車資料，開始第一次同步");
                await Invoke();
            }
            else if (await _busService.CountPlaceBusStopsAsync() == 0)
            {
                _logger.LogInformation("還沒有景點附近站牌資料，開始計算");
                await BuildPlaceStopsAsync();
            }
        }

        private async Task BuildPlaceStopsAsync()
        {
            if (!_configuration.GetValue("BusSync:BuildPlaceStops", true)) return;

            var scopes = Cities().ToList();
            if (IncludeTaiwanTrip()) scopes.Add("台灣好行");

            foreach (string scope in scopes)
            {
                await RunAsync($"計算 {scope} 景點附近站牌", async () =>
                {
                    var r = await _busService.BuildPlaceBusStopsAsync(scope);
                    _logger.LogInformation("{Scope} 景點附近站牌：{Places} 個景點中 {WithStop} 個有站牌，共 {Pairs} 筆（{Seconds} 秒）",
                        r.scope, r.place_count, r.place_with_stop_count, r.pair_count, r.elapsed_seconds);
                });
            }
        }

        private IEnumerable<string> Cities() =>
            (_configuration.GetSection("BusSync:Cities").Get<List<string>>() ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c));

        private bool IncludeTaiwanTrip() => _configuration.GetValue("BusSync:IncludeTaiwanTrip", true);

        private async Task RunAsync(string name, Func<Task> action)
        {
            try
            {
                _logger.LogInformation("公車排程：開始{Name}", name);
                await action();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "公車排程：{Name}失敗", name);
            }
        }

        private static string Describe(ViewModels.BusSyncResult r) =>
            $"{r.source}：路線 {r.route_count}、行駛型態 {r.pattern_count}（重建 {r.rebuilt_pattern_count}）、站牌 {r.stop_count}、班次 {r.trip_count}、停用 {r.deactivated_route_count}（{r.elapsed_seconds} 秒）";
    }
}
