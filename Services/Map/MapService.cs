// 檔案路徑：System\Services\Map\MapService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.Services.Neo4j;
using backend.utils;
using Microsoft.Extensions.Configuration;

namespace backend.Services
{
    public class MapService
    {
        // 抵達判定距離，與任務作答共用（PlayRules）
        private const double UnlockRadiusMeters = PlayRules.ArrivalRadiusMeters;

        /// <summary>fog 資料表沒有資料時用的迷霧圖</summary>
        internal const string DefaultFogImage = "/images/fog/default_fog.png";

        private readonly MapDao _dao;
        private readonly GeocodingService _geocodingService;
        private readonly PlaceLookupService _placeLookup;
        private readonly FogGenerationQueue _fogQueue;
        private readonly double _fogRadiusMeters;
        private readonly string _fogSecret;

        /// <summary>示範模式（設定 Demo:AllowStageSkip = true）：可以不到現場、不作答就破關，展示用，預設關閉</summary>
        private readonly bool _demoMode;

        public MapService(MapDao dao, GeocodingService geocodingService, PlaceLookupService placeLookup,
            FogGenerationQueue fogQueue, IConfiguration configuration)
        {
            _dao = dao;
            _geocodingService = geocodingService;
            _placeLookup = placeLookup;
            _fogQueue = fogQueue;
            _fogRadiusMeters = double.TryParse(configuration["Fog:RadiusMeters"], out double radius) && radius > 0 ? radius : 300;
            _fogSecret = configuration["AppSettings:jwt_secret"] ?? "";
            _demoMode = bool.TryParse(configuration["Demo:AllowStageSkip"], out bool demo) && demo;
        }

        #region 取得地圖

        /// <summary>
        /// 取得指定劇本的地圖資訊。只有劇本擁有者或協作隊員可以查看。
        /// 像藏寶圖一樣一關一關開：只有目前這一關和已經破關的站是解鎖的，破完這一關（任務全部通過）才會開下一站
        /// （協作隊伍全隊共用進度，見 TeamProgress.GetStageOrder）。座標依節點的 place_id 向 Neo4j 查。
        /// 未解鎖的站由後端蓋上迷霧：站名換成迷霧提示（地點代號）、座標換成偏移過的迷霧中心，真正的答案不會傳給前端。
        /// 照片與真實名稱要抵達後才給：還沒抵達的站（包含已解鎖的目前這一關）照片都是這一站照片的迷霧版。
        /// 迷霧圖還沒做好的站先用通用迷霧圖，並在背景補做（下次打開地圖就會換上）。
        /// 新資料表沒有 day_index，目前所有節點都歸在第一日。
        /// </summary>
        /// <param name="storyId">劇本代號，對應 story.s_id。</param>
        /// <param name="user">目前登入使用者 JWT Claims。</param>
        /// <param name="baseUrl">後端網址（例如 https://xxx），用來把迷霧圖組成完整網址。</param>
        /// <returns>地圖進度、節點、明信片統計與總天數。</returns>
        public async Task<MapResponse> GetMap(
            int storyId,
            ClaimsPrincipal user,
            string baseUrl)
        {
            int auId = user.GetAuId();

            List<MapNode> nodes = _dao.GetStoryNodes(storyId);

            if (nodes == null || nodes.Count == 0)
            {
                throw new KeyNotFoundException("此劇本沒有可用的地圖節點。");
            }

            if (!_dao.CanPlay(auId, storyId))
            {
                throw new UnauthorizedAccessException("你沒有參與這個劇本，無法查看地圖。");
            }

            Dictionary<string, PlaceLookupService.PlaceInfo> places =
                await _placeLookup.GetPlacesAsync(nodes.Select(x => x.place_id));

            // 景點照片直接用景點 ID 向 Neo4j 拿；迷霧圖依照片網址對應
            Dictionary<string, string> photos = await _placeLookup.GetPlaceImagesAsync(nodes.Select(x => x.place_id));
            Dictionary<string, string> fogImages = _dao.GetFogImages(photos.Values);

            foreach (MapNode node in nodes)
            {
                if (node.place_id != null && places.TryGetValue(node.place_id, out PlaceLookupService.PlaceInfo place))
                {
                    node.lat = place.lat;
                    node.lng = place.lng;
                }

                node.image_url = node.place_id != null ? photos.GetValueOrDefault(node.place_id) : null;
                node.fog_image = node.image_url != null ? fogImages.GetValueOrDefault(node.image_url) : null;
            }

            int stageOrder = _dao.GetStageOrder(auId, storyId);
            int arrivedOrder = _dao.GetArrivedOrder(auId, storyId);

            nodes = nodes
                .OrderBy(x => x.day_index)
                .ThenBy(x => x.node_order)
                .ToList();

            string defaultFog = ToAbsoluteUrl(_dao.GetDefaultFogImage() ?? DefaultFogImage, baseUrl);
            bool missingFog = false;

            foreach (MapNode node in nodes)
            {
                // 第一個節點固定開放；破完第 N 節點（任務全部通過）才開放第 N+1 節點（協作隊伍全隊共用進度）。
                node.is_unlocked = TeamProgress.IsUnlocked(node.node_order, stageOrder);
                node.is_current = node.node_order == stageOrder;
                node.is_arrived = TeamProgress.IsReached(node.node_order, arrivedOrder);
                node.codename = string.IsNullOrWhiteSpace(node.fog_hint) ? null : node.fog_hint.Trim();

                // 景點是哪裡，到了現場才知道：還沒抵達的站（目前這一關、迷霧中的站）不回傳真實名稱，
                // 照片也換成這一站照片的迷霧版（真實照片一看就知道答案）
                if (!node.is_arrived)
                {
                    node.place_name = null;
                    missingFog |= node.image_url != null && node.fog_image == null;
                    node.image_url = node.fog_image != null ? ToAbsoluteUrl(node.fog_image, baseUrl) : defaultFog;
                }

                if (node.is_unlocked)
                {
                    // 已解鎖後不再顯示迷霧文字。
                    node.fog_hint = null;
                    node.fog_radius_m = null;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(node.fog_hint))
                    {
                        node.fog_hint = "前方仍被迷霧籠罩，完成前一站後即可探索。";
                    }

                    // 迷霧中的站：不回傳真正的站名與精確座標（照片在上面已經換成迷霧圖）
                    node.location_name = node.fog_hint;
                    node.fog_radius_m = _fogRadiusMeters;

                    if (node.lat != 0 || node.lng != 0)
                    {
                        (node.lat, node.lng) = FogCenter(node.node_id, node.lat, node.lng, _fogRadiusMeters, _fogSecret);
                    }
                }

                node.child_node_ids ??= new List<int>();
            }

            // 有照片但迷霧圖還沒做好（例如迷霧功能上線前就生成的劇本）：在背景補做
            if (missingFog)
            {
                _fogQueue.Enqueue(storyId);
            }

            // 建立線性路線：第一站 -> 第二站 -> 第三站。
            // 未來若有分支劇情，再改成資料表設定 child_node_ids。
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                nodes[i].child_node_ids.Add(nodes[i + 1].node_id);
            }

            int totalDays = nodes.Max(x => x.day_index);

            return new MapResponse
            {
                story_id = storyId,

                unlocked_node_count = nodes.Count(x => x.is_unlocked),
                total_node_count = nodes.Count,
                current_node_id = nodes.FirstOrDefault(x => x.is_current)?.node_id,
                reveal_radius_m = _fogRadiusMeters,
                demo_mode = _demoMode,

                postcard_unlocked_count =
                    _dao.GetUnlockedPostcardCount(auId, storyId),

                postcard_total_count =
                    _dao.GetTotalPostcardCount(storyId),

                nodes = nodes,

                // 預設進入地圖先顯示第一日。
                // 前端點第二日時，以 node.day_index == 2 過濾即可，不需要再打 API。
                day_index = 1,
                total_days = totalDays
            };
        }

        #endregion

        #region GPS 確認抵達

        /// <summary>
        /// GPS 確認抵達：只有劇本擁有者或協作隊員可以抵達，且只能抵達已解鎖的站；
        /// 距離以節點 place_id 在 Neo4j 的座標計算（與任務作答同一份座標）。
        /// </summary>
        public async Task<NodeDetailResponse> ArriveNode(
            int nodeId,
            double userLat,
            double userLng,
            ClaimsPrincipal user)
        {
            int auId = user.GetAuId();

            MapNode node = _dao.GetNodeLocation(nodeId);

            if (node == null)
            {
                throw new KeyNotFoundException("找不到指定節點。");
            }

            int storyId = _dao.GetNodeStoryId(nodeId) ?? throw new KeyNotFoundException("找不到指定節點。");

            if (!_dao.CanPlay(auId, storyId))
            {
                throw new UnauthorizedAccessException("你沒有參與這個劇本，無法抵達節點。");
            }

            // 只能抵達已解鎖的站（還在迷霧中回 403），避免直接跳到後面的站把進度推過去
            EnsureUnlocked(auId, nodeId);

            PlaceLookupService.PlaceInfo place = await _placeLookup.GetPlaceAsync(node.place_id)
                ?? throw new InvalidOperationException("查不到此節點的景點座標，無法確認抵達。");

            double distance = CalculateDistanceMeters(
                userLat,
                userLng,
                place.lat,
                place.lng);

            if (distance > UnlockRadiusMeters)
            {
                throw new InvalidOperationException(
                    $"尚未抵達指定地點，目前距離約 {Math.Round(distance)} 公尺，"
                    + $"需在 {UnlockRadiusMeters} 公尺內。");
            }

            _dao.UnlockNode(auId, nodeId);

            return LoadNodeDetail(nodeId);
        }

        #endregion

        #region 示範模式

        /// <summary>
        /// 示範用破關：不檢查 GPS、不用作答，直接把這一站當成抵達而且任務全部通過，下一站就會解鎖。
        /// 只有設定 Demo:AllowStageSkip = true 時能用（預設關閉，正式環境不要開）；一樣只能破已解鎖的站，不能跳關。
        /// </summary>
        public Task<MapResponse> DemoClearNode(int nodeId, ClaimsPrincipal user, string baseUrl)
        {
            EnsureDemoMode();
            int auId = user.GetAuId();
            int storyId = _dao.GetNodeStoryId(nodeId) ?? throw new KeyNotFoundException("找不到指定節點。");

            if (!_dao.CanPlay(auId, storyId))
            {
                throw new UnauthorizedAccessException("你沒有參與這個劇本，無法破關。");
            }

            EnsureUnlocked(auId, nodeId);

            _dao.UnlockNode(auId, nodeId);
            _dao.DemoClearNode(storyId, nodeId);

            return GetMap(storyId, user, baseUrl);
        }

        /// <summary>示範用重來：這份劇本回到第一關（示範模式才能用）</summary>
        public Task<MapResponse> DemoResetStory(int storyId, ClaimsPrincipal user, string baseUrl)
        {
            EnsureDemoMode();

            if (!_dao.CanPlay(user.GetAuId(), storyId))
            {
                throw new UnauthorizedAccessException("你沒有參與這個劇本，無法重來。");
            }

            _dao.DemoResetStory(storyId);
            return GetMap(storyId, user, baseUrl);
        }

        private void EnsureDemoMode()
        {
            if (!_demoMode)
            {
                throw new UnauthorizedAccessException("後端沒有開示範模式（設定 Demo:AllowStageSkip），不能直接破關。");
            }
        }

        #endregion

        #region 取得節點詳情

        /// <summary>
        /// 節點詳情；還在迷霧中的站不能查看。已解鎖但還沒抵達的站（目前這一關）只給站名與 NPC，
        /// 景點介紹與營業時間等到了現場才給（那些內容一看就知道是哪裡）
        /// </summary>
        public NodeDetailResponse GetNodeDetail(int nodeId, ClaimsPrincipal user)
        {
            bool arrived = EnsureUnlocked(user.GetAuId(), nodeId);
            NodeDetailResponse result = LoadNodeDetail(nodeId);

            if (!arrived)
            {
                result.intro_story = NotArrivedIntro;
                result.opening_hours = null;
            }

            return result;
        }

        /// <summary>還沒抵達的站，節點詳情的介紹文字</summary>
        internal const string NotArrivedIntro = "到了現場，這裡的故事才會揭曉。";

        private NodeDetailResponse LoadNodeDetail(int nodeId)
        {
            NodeDetailResponse result = _dao.GetNodeDetail(nodeId);

            if (result == null)
            {
                throw new KeyNotFoundException("找不到指定節點詳情。");
            }

            result.nearby_food ??= new List<string>();

            return result;
        }

        #endregion

        #region NPC 隨機互動

        /// <summary>
        /// NPC 隨機互動；還在迷霧中的站不能互動。已解鎖但還沒抵達的站（目前這一關）背景圖是迷霧圖、沒有景點副標題，
        /// 真實照片到了現場才給
        /// </summary>
        public async Task<NpcInteractionResponse> GetNpcInteraction(int nodeId, ClaimsPrincipal user)
        {
            bool arrived = EnsureUnlocked(user.GetAuId(), nodeId);

            NpcInteractionResponse result =
                _dao.GetRandomNpcInteraction(nodeId, arrived);

            if (result == null)
            {
                throw new KeyNotFoundException("此節點尚未設定 NPC 互動內容。");
            }

            // 背景圖：景點照片直接用景點 ID 向 Neo4j 拿；還沒抵達時換成這張照片的迷霧版（還沒做好就用通用迷霧圖）
            string placeId = _dao.GetNodeLocation(nodeId)?.place_id;
            string photo = placeId == null ? null : (await _placeLookup.GetPlaceImagesAsync(new[] { placeId })).GetValueOrDefault(placeId);

            result.scene_image_url = arrived
                ? photo
                : (photo == null ? null : _dao.GetFogImages(new[] { photo }).GetValueOrDefault(photo))
                  ?? _dao.GetDefaultFogImage() ?? DefaultFogImage;

            result.node_id = nodeId;
            result.emotion ??= "normal";
            result.skip_button_text ??= "稍後再說";

            return result;
        }

        #endregion

        #region 導航

        /// <summary>導航；還在迷霧中的站不能導航（否則會直接洩漏精確位置）</summary>
        public async Task<NavigationResponse> GetNavigation(NavigationRequest req, ClaimsPrincipal user)
        {
            if (req == null || req.node_id <= 0)
            {
                throw new ArgumentException("node_id 不可為空白。");
            }

            EnsureUnlocked(user.GetAuId(), req.node_id);

            MapNode node = _dao.GetNodeLocation(req.node_id);

            if (node == null)
            {
                throw new KeyNotFoundException("找不到指定導航節點。");
            }

            PlaceLookupService.PlaceInfo place = await _placeLookup.GetPlaceAsync(node.place_id)
                ?? throw new InvalidOperationException("查不到此景點的座標。");

            return new NavigationResponse
            {
                maps_deeplink_url =
                    $"https://www.google.com/maps/search/?api=1&query={place.lat},{place.lng}"
            };
        }

        #endregion

        #region 迷霧

        /// <summary>第一站固定開放；玩家（協作隊伍全隊共用）破完第 N 站後，才開放第 N+1 站。規則見 TeamProgress</summary>
        internal static bool IsUnlocked(int nodeOrder, int stageOrder) =>
            TeamProgress.IsUnlocked(nodeOrder, stageOrder);

        /// <summary>
        /// 這一站還在迷霧中就丟出 NodeLockedException（進度用 TeamProgress，協作隊伍全隊共用）。
        /// 回傳隊伍抵達這一站了沒：已解鎖但還沒抵達的站，景點的真實照片與介紹還不能給
        /// </summary>
        private bool EnsureUnlocked(int auId, int nodeId)
        {
            MapDao.NodeProgress progress = _dao.GetNodeProgress(auId, nodeId);

            if (progress == null)
            {
                throw new KeyNotFoundException("找不到指定節點。");
            }

            if (!IsUnlocked(progress.node_order, progress.stage_order))
            {
                throw new NodeLockedException();
            }

            return TeamProgress.IsReached(progress.node_order, progress.arrived_order);
        }

        /// <summary>
        /// 迷霧中心：把真正的座標往隨機方向偏移半徑的 20%～80%，真正的點一定落在迷霧範圍內。
        /// 偏移量由節點代號加上後端密鑰算出來，同一站每次都一樣（迷霧不會在地圖上跳動），
        /// 但沒有密鑰就算不回真正的位置（程式碼是公開的）。
        /// </summary>
        internal static (double lat, double lng) FogCenter(int nodeId, double lat, double lng, double radiusMeters, string secret)
        {
            byte[] hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret ?? ""), Encoding.UTF8.GetBytes($"fog:{nodeId}"));
            double u1 = BitConverter.ToUInt32(hash, 0) / (double)uint.MaxValue;
            double u2 = BitConverter.ToUInt32(hash, 4) / (double)uint.MaxValue;

            double distance = radiusMeters * (0.2 + 0.6 * u1);
            double bearing = 2 * Math.PI * u2;
            const double metersPerDegree = 111320.0;

            double fogLat = lat + distance * Math.Cos(bearing) / metersPerDegree;
            double fogLng = lng + distance * Math.Sin(bearing) / (metersPerDegree * Math.Cos(lat * Math.PI / 180));

            return (Math.Round(fogLat, 6), Math.Round(fogLng, 6));
        }

        /// <summary>/ 開頭的路徑接上後端網址；已經是完整網址就不動</summary>
        private static string ToAbsoluteUrl(string path, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith("http", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(baseUrl))
            {
                return path;
            }

            return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        }

        #endregion

        #region 私有邏輯

        private double CalculateDistanceMeters(
            double lat1,
            double lng1,
            double lat2,
            double lng2)
        {
            const double earthRadiusMeters = 6371000.0;

            double latDifference = DegreesToRadians(lat2 - lat1);
            double lngDifference = DegreesToRadians(lng2 - lng1);

            double a =
                Math.Sin(latDifference / 2) *
                Math.Sin(latDifference / 2) +
                Math.Cos(DegreesToRadians(lat1)) *
                Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(lngDifference / 2) *
                Math.Sin(lngDifference / 2);

            double c = 2 * Math.Atan2(
                Math.Sqrt(a),
                Math.Sqrt(1 - a));

            return earthRadiusMeters * c;
        }

        private double DegreesToRadians(double degree)
        {
            return degree * Math.PI / 180.0;
        }

        #endregion

        #region 回報定位

        /// <summary>
        /// 接收前端回報的 GPS 座標，透過共用的 GeocodingService 反向地理編碼為縣市／鄉鎮區。
        /// </summary>
        public async Task<LocationResponse> ReportLocationAsync(LocationRequest req)
        {
            if (req == null)
            {
                throw new ArgumentException("請提供定位資料。");
            }

            if (req.lat < -90 || req.lat > 90 || req.lng < -180 || req.lng > 180)
            {
                throw new ArgumentException("經緯度超出有效範圍。");
            }

            var (cityName, districtName) =
                await _geocodingService.ResolveTaiwanAreaAsync(req.lat, req.lng);

            return new LocationResponse
            {
                lat = req.lat,
                lng = req.lng,
                accuracy = req.accuracy,
                city_name = cityName,
                district_name = districtName,
                received_at = DateTime.UtcNow
            };
        }

        #endregion
    }
}