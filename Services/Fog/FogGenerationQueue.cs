// 檔案路徑：System\Services\Fog\FogGenerationQueue.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace backend.Services
{
    /// <summary>
    /// 在背景產生劇本的迷霧圖（要下載每一站的照片，不擋住 API 回應）。
    /// 生成劇本後會排一次；已經存在的劇本在第一次打開地圖時發現缺圖也會排一次。
    /// 同一個劇本同時只跑一個，而且 10 分鐘內不重跑（避免照片壞掉時每次開地圖都重新下載）。
    /// </summary>
    public class FogGenerationQueue
    {
        private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<FogGenerationQueue> _logger;
        private readonly Dictionary<int, DateTime> _lastRun = new Dictionary<int, DateTime>();

        public FogGenerationQueue(IServiceScopeFactory scopeFactory, ILogger<FogGenerationQueue> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <param name="force">true：不管 10 分鐘內是否跑過都重跑（剛生成的劇本用）</param>
        public void Enqueue(int storyId, bool force = false)
        {
            lock (_lastRun)
            {
                DateTime now = DateTime.UtcNow;
                if (!force && _lastRun.TryGetValue(storyId, out DateTime last) && now - last < RetryAfter)
                    return;
                _lastRun[storyId] = now;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    FogGenerateResult r = await scope.ServiceProvider.GetRequiredService<FogService>().GenerateForStoryAsync(storyId);
                    _logger.LogInformation("劇本 {StoryId} 迷霧圖：新做 {Generated}、沿用 {Reused}、失敗 {Failed}", storyId, r.generated, r.reused, r.failed);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "劇本 {StoryId} 迷霧圖產生失敗", storyId);
                }
            });
        }
    }
}
