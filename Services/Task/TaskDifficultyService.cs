using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.Services.Neo4j;
using Microsoft.Extensions.Logging;

namespace backend.Services
{
    /// <summary>玩家近期的答題表現（全國一起算），決定抽題權重與提示開放門檻</summary>
    public enum PlayerPerformance
    {
        Normal,
        Struggling,
        Smooth
    }

    /// <summary>
    /// 任務動態難度：
    /// 1. 地區解鎖：玩家還沒在某個鄉鎮市區抵達過任何一站時，那個區的景點不出 e人訪談型（8）。
    ///    景點的鄉鎮市區向 Neo4j 查，查不到時用劇本生成時的縣市／鄉鎮區。
    /// 2. 依表現調整：最近有對錯的作答中第一次就答對的比例，決定 6／7／8 的抽題權重，
    ///    並和題目難易度一起決定提示開放門檻。
    /// 生成劇本（StoryService.DecideTaskTypes）、節點遊玩畫面與取提示（TaskService）、解鎖進度（GET api/Task/Progress）共用。
    /// </summary>
    public class TaskDifficultyService
    {
        // ── 地區解鎖 ──

        /// <summary>第一次到某個鄉鎮市區時不出的題型：e人訪談型（要跟陌生人互動，門檻最高）</summary>
        public static readonly HashSet<int> FirstVisitLockedTypeIds = new() { 8 };

        /// <summary>解鎖進度要列出的題型</summary>
        private static readonly int[] ProgressTypeIds = { 2, 3, 4, 5, 6, 7, 8, 9 };

        // ── 表現判斷 ──

        /// <summary>有對錯的題型：協作解謎、文化問答、景點猜猜樂、商家知識問答、圖像地理猜謎（其他題型有交就通過，不列入表現）</summary>
        private static readonly int[] GradedTypeIds = { 5, 6, 7, 9, 10 };
        private const int RecentTaskCount = 20;              // 看最近幾題
        private const int MinTaskCountToJudge = 5;           // 題數少於此值一律當一般
        private const double StrugglingFirstTryRate = 0.5;   // 第一次就答對的比例低於此值 → 卡關
        private const double StrugglingAvgAttempts = 3;      // 平均作答次數達此值 → 卡關
        private const double SmoothFirstTryRate = 0.8;       // 第一次就答對的比例達此值 → 順利

        // ── 抽題權重（基本權重 1，條件符合時相乘）──

        private static readonly HashSet<int> ChoiceTypeIds = new() { 6, 7 };   // 文化問答型、景點猜猜樂
        private const int InterviewTypeId = 8;                                   // e人訪談型
        private const double NoveltyWeight = 2;                // 從沒作答過的題型
        private const double StrugglingChoiceWeight = 2;       // 卡關：選擇題
        private const double StrugglingInterviewWeight = 0.5;  // 卡關：e人訪談
        private const double SmoothInterviewWeight = 2;        // 順利：e人訪談

        // ── 提示開放門檻（答錯幾次後開放）＝ 題目難易度的基本門檻 ＋ 玩家表現調整（卡關 -1、順利 +1）──

        /// <summary>
        /// 有對錯的題型的基本門檻，越難越早開。其他題型（2、3、4、8）交了就通過、不會答錯，提示一開始就開放。
        /// </summary>
        private static readonly Dictionary<int, int> HintBaseWrongCount = new()
        {
            [5] = 0,    // 協作解謎型（難）：兩人合作推出精確答案，可以一直答錯
            [6] = 1,    // 文化問答型（中）
            [9] = 1,    // 商家知識問答（中）
            [10] = 1,   // 圖像地理猜謎型（中）
            [7] = 2     // 景點猜猜樂（易）：人在現場，從 4 張照片認出眼前的景點
        };
        private const int ChoiceMaxHintWrongCount = 2;   // 4 選 1 答錯 2 次後只剩 2 個選項，門檻再高提示就沒意義

        private readonly TaskDifficultyDao _dao;
        private readonly PlaceLookupService _placeLookup;
        private readonly GeocodingService _geocoding;
        private readonly ILogger<TaskDifficultyService> _logger;

        public TaskDifficultyService(
            TaskDifficultyDao dao,
            PlaceLookupService placeLookup,
            GeocodingService geocoding,
            ILogger<TaskDifficultyService> logger)
        {
            _dao = dao;
            _placeLookup = placeLookup;
            _geocoding = geocoding;
            _logger = logger;
        }

        /// <summary>生成劇本時用的玩家資料</summary>
        public class PlayerProfile
        {
            public PlayerPerformance performance { get; set; }
            public HashSet<int> answered_type_ids { get; set; } = new();
            public HashSet<string> visited_district_keys { get; set; } = new();
        }

        public async Task<PlayerProfile> GetPlayerProfileAsync(int auId)
        {
            return new PlayerProfile
            {
                performance = GetPerformance(auId),
                answered_type_ids = _dao.GetAnsweredTypeIds(auId),
                visited_district_keys = await GetVisitedDistrictKeysAsync(auId)
            };
        }

        #region 表現與提示

        /// <summary>
        /// 最近 20 題有對錯的任務：第一次就答對的比例 &lt; 50% 或平均作答 3 次以上為卡關、≥ 80% 為順利，其他為一般。
        /// 不到 5 題一律當一般。
        /// </summary>
        public PlayerPerformance GetPerformance(int auId)
        {
            List<TaskDifficultyDao.GradedTaskRow> tasks = _dao.GetRecentGradedTasks(auId, GradedTypeIds, RecentTaskCount);
            if (tasks.Count < MinTaskCountToJudge) return PlayerPerformance.Normal;

            double firstTryRate = tasks.Count(t => t.attempts == 1 && t.solved == 1) / (double)tasks.Count;
            double avgAttempts = tasks.Average(t => t.attempts);

            if (firstTryRate < StrugglingFirstTryRate || avgAttempts >= StrugglingAvgAttempts) return PlayerPerformance.Struggling;
            if (firstTryRate >= SmoothFirstTryRate) return PlayerPerformance.Smooth;
            return PlayerPerformance.Normal;
        }

        /// <summary>
        /// 答錯幾次後開放提示：題目難易度的基本門檻（難 0、中 1、易 2）加上玩家表現調整（卡關 -1、順利 +1），最少 0 次；
        /// 選擇題最多 2 次。沒有對錯的題型一律 0 次（一開始就開放）。
        /// </summary>
        public static int HintUnlockWrongCount(int typeId, PlayerPerformance performance)
        {
            if (!HintBaseWrongCount.TryGetValue(typeId, out int baseCount)) return 0;

            int adjust = performance switch
            {
                PlayerPerformance.Struggling => -1,
                PlayerPerformance.Smooth => 1,
                _ => 0
            };

            int count = Math.Max(0, baseCount + adjust);
            return AnswerModes.ForType(typeId) == AnswerModes.Choice ? Math.Min(count, ChoiceMaxHintWrongCount) : count;
        }

        #endregion

        #region 抽題

        /// <summary>
        /// 從景點可出的隨機題型（6／7／8）依權重抽一個，沒有可抽的回傳 null：
        /// 第一次到這個區時拿掉未解鎖的題型；從沒作答過的題型權重加倍；
        /// 卡關時偏向選擇題（6、7）、少出 e人訪談，順利時多出 e人訪談。
        /// </summary>
        public static int? PickRandomType(IEnumerable<int> pool, PlayerProfile player, bool isFirstVisit)
        {
            player ??= new PlayerProfile();

            List<int> candidates = pool
                .Distinct()
                .Where(id => !(isFirstVisit && FirstVisitLockedTypeIds.Contains(id)))
                .ToList();
            if (candidates.Count == 0) return null;

            List<double> weights = candidates.Select(id => TypeWeight(id, player)).ToList();
            double roll = Random.Shared.NextDouble() * weights.Sum();

            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0) return candidates[i];
            }

            return candidates[^1];
        }

        private static double TypeWeight(int typeId, PlayerProfile player)
        {
            double weight = 1;

            if (!player.answered_type_ids.Contains(typeId)) weight *= NoveltyWeight;

            if (player.performance == PlayerPerformance.Struggling)
            {
                if (ChoiceTypeIds.Contains(typeId)) weight *= StrugglingChoiceWeight;
                if (typeId == InterviewTypeId) weight *= StrugglingInterviewWeight;
            }
            else if (player.performance == PlayerPerformance.Smooth && typeId == InterviewTypeId)
            {
                weight *= SmoothInterviewWeight;
            }

            return weight;
        }

        #endregion

        #region 地區解鎖

        /// <summary>
        /// 這些景點中，玩家第一次到所在鄉鎮市區的景點（place_id）。
        /// 景點查不到行政區時用這次生成劇本的縣市／鄉鎮區；那也沒有就當第一次到。
        /// </summary>
        public async Task<HashSet<string>> GetFirstVisitPlaceIdsAsync(
            PlayerProfile player, IEnumerable<string> placeIds, string fallbackCity, string fallbackDistrict)
        {
            List<string> ids = (placeIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            Dictionary<string, PlaceLookupService.PlaceDistrict> districts = await GetPlaceDistrictsSafeAsync(ids);
            string fallbackKey = DistrictKey(fallbackCity, fallbackDistrict);

            return ids
                .Where(id =>
                {
                    string key = districts.TryGetValue(id, out var d) ? DistrictKey(d.city_name, d.district_name) : fallbackKey;
                    return key == null || !player.visited_district_keys.Contains(key);
                })
                .ToHashSet();
        }

        /// <summary>玩家去過的鄉鎮市區（DistrictKey）</summary>
        private async Task<HashSet<string>> GetVisitedDistrictKeysAsync(int auId)
        {
            List<TaskDifficultyDao.VisitedPlaceRow> visited = _dao.GetVisitedPlaces(auId);
            Dictionary<string, PlaceLookupService.PlaceDistrict> districts =
                await GetPlaceDistrictsSafeAsync(visited.Select(v => v.place_id));

            return visited
                .Select(v => districts.TryGetValue(v.place_id, out var d)
                    ? DistrictKey(d.city_name, d.district_name)
                    : DistrictKey(v.city_name, v.district_name))
                .Where(key => key != null)
                .ToHashSet();
        }

        /// <summary>Neo4j 查不到時回傳空的，呼叫端改用劇本生成時的縣市／鄉鎮區</summary>
        private async Task<Dictionary<string, PlaceLookupService.PlaceDistrict>> GetPlaceDistrictsSafeAsync(IEnumerable<string> placeIds)
        {
            try
            {
                return await _placeLookup.GetPlaceDistrictsAsync(placeIds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "向 Neo4j 查景點行政區失敗，改用劇本生成時的縣市／鄉鎮區判斷。");
                return new Dictionary<string, PlaceLookupService.PlaceDistrict>();
            }
        }

        /// <summary>
        /// 比對用的鄉鎮市區代號「縣市_鄉鎮市區」（同 Neo4j Town.id 的格式），統一「台／臺」、去掉空白。
        /// 區名在不同縣市會重複（例如東區），所以一定連縣市一起比；缺任何一個回傳 null。
        /// </summary>
        public static string DistrictKey(string city, string district)
        {
            string c = NormalizeAreaName(city);
            string d = NormalizeAreaName(district);
            return c == null || d == null ? null : $"{c}_{d}";
        }

        private static string NormalizeAreaName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return new string(name.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).Replace('台', '臺');
        }

        #endregion

        #region 解鎖進度

        /// <summary>
        /// 玩家在座標所在鄉鎮市區的題型解鎖進度。查不到所在區時縣市／鄉鎮市區為 null，並當作第一次到。
        /// </summary>
        public async Task<TaskProgressResponse> GetProgressAsync(int auId, double lat, double lng)
        {
            PlaceLookupService.PlaceDistrict here = await ResolveDistrictAsync(lat, lng);
            string key = here == null ? null : DistrictKey(here.city_name, here.district_name);
            HashSet<string> visited = await GetVisitedDistrictKeysAsync(auId);
            bool isFirstVisit = key == null || !visited.Contains(key);

            Dictionary<int, string> typeNames = _dao.GetTypeNames(ProgressTypeIds);
            string areaName = key == null ? "這個區" : here.district_name;

            var response = new TaskProgressResponse
            {
                city_name = key == null ? null : here.city_name,
                district_name = key == null ? null : here.district_name,
                is_first_visit = isFirstVisit
            };

            foreach (int typeId in ProgressTypeIds)
            {
                var type = new TaskTypeUnlock { type_id = typeId, type_name = typeNames.GetValueOrDefault(typeId) };

                if (isFirstVisit && FirstVisitLockedTypeIds.Contains(typeId))
                {
                    type.unlock_hint = $"在{areaName}抵達任一站後解鎖";
                    response.locked_types.Add(type);
                }
                else
                {
                    response.unlocked_types.Add(type);
                }
            }

            return response;
        }

        /// <summary>
        /// 座標所在的鄉鎮市區：先用 Neo4j 最近景點的行政區（和判斷景點行政區的來源一致），查不到再用反向地理編碼。
        /// </summary>
        private async Task<PlaceLookupService.PlaceDistrict> ResolveDistrictAsync(double lat, double lng)
        {
            try
            {
                PlaceLookupService.PlaceDistrict district = await _placeLookup.GetDistrictByCoordinatesAsync(lat, lng);
                if (district != null) return district;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "向 Neo4j 查座標所在行政區失敗，改用反向地理編碼。");
            }

            try
            {
                var (city, district) = await _geocoding.ResolveTaiwanAreaAsync(lat, lng);
                return new PlaceLookupService.PlaceDistrict { city_name = city, district_name = district };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "反向地理編碼查座標所在行政區失敗。");
                return null;
            }
        }

        #endregion
    }
}
