// 檔案路徑：System\Services\Metro\MetroSyncInvocable.cs
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
    /// 捷運資料排程同步（Coravel）：依 appsettings.json 的 MetroSync:Systems 逐一同步，
    /// 某個系統失敗只記 log，不影響其他系統。沒設定 Systems 時同步全部。
    /// </summary>
    public class MetroSyncInvocable : IInvocable
    {
        private readonly MetroService _metroService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MetroSyncInvocable> _logger;

        public MetroSyncInvocable(MetroService metroService, IConfiguration configuration, ILogger<MetroSyncInvocable> logger)
        {
            _metroService = metroService;
            _configuration = configuration;
            _logger = logger;
        }

        public Task Invoke() => SyncAsync(Systems());

        /// <summary>啟動時補資料：只同步還沒有資料或資料不完整的系統</summary>
        public async Task InvokeIfMissingAsync()
        {
            List<string> systems = await _metroService.GetSystemsNeedingSyncAsync(Systems());
            if (systems.Count == 0) return;
            _logger.LogInformation("捷運資料不完整，開始同步：{Systems}", string.Join("、", systems));
            await SyncAsync(systems);
        }

        private async Task SyncAsync(IEnumerable<string> systems)
        {
            foreach (string system in systems)
            {
                try
                {
                    var r = await _metroService.SyncAsync(system);
                    _logger.LogInformation("捷運排程：{System} 路線 {Lines}、車站 {Stations}、站間連線 {Links}、線形 {Shapes}、估算行駛時間 {Estimated} 條（{Seconds} 秒）",
                        r.system_name, r.line_count, r.station_count, r.link_count, r.shape_count, r.estimated_line_count, r.elapsed_seconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "捷運排程：同步 {System} 失敗", system);
                }
            }
        }

        private IEnumerable<string> Systems()
        {
            var systems = (_configuration.GetSection("MetroSync:Systems").Get<List<string>>() ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            return systems.Count > 0 ? systems : MetroService.SystemNames.Keys;
        }
    }
}
