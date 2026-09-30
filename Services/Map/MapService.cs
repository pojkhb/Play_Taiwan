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
using backend.utils;
using Microsoft.Extensions.Configuration;

namespace backend.Services
{
    public class MapService
    {
        private const double UnlockRadiusMeters = 50.0;

        /// <summary>fog 資料表沒有資料時用的迷霧圖</summary>
        private const string DefaultFogImage = "/images/fog/default_fog.png";

        private readonly MapDao _dao;
        private readonly GeocodingService _geocodingService;
        private readonly FogGenerationQueue _fogQueue;
        private readonly double _fogRadiusMeters;
        private readonly string _fogSecret;

        public MapService(MapDao dao, GeocodingService geocodingService, FogGenerationQueue fogQueue, IConfiguration configuration)
        {
            _dao = dao;
            _geocodingService = geocodingService;
            _fogQueue = fogQueue;
            _fogRadiusMeters = double.TryParse(configuration["Fog:RadiusMeters"], out double radius) && radius > 0 ? radius : 300;
            _fogSecret = configuration["AppSettings:jwt_secret"] ?? "";
        }

        #region 取得地圖

        /// <summary>
        /// 取得指定劇本的地圖資訊。
        /// 後端依 story_session.ss_current 判斷節點是否解鎖，未解鎖的站由後端蓋上迷霧：
        /// 站名換成迷霧提示、照片換成這一站照片的迷霧版、座標換成偏移過的迷霧中心，真正的答案不會傳給前端。
        /// 迷霧圖還沒做好的站先用通用迷霧圖，並在背景補做（下次打開地圖就會換上）。
        /// 新資料表沒有 day_index，目前所有節點都歸在第一日。
        /// </summary>
        /// <param name="storyId">劇本代號，對應 story.s_id。</param>
        /// <param name="user">目前登入使用者 JWT Claims。</param>
        /// <param name="baseUrl">後端網址（例如 https://xxx），用來把迷霧圖組成完整網址。</param>
        /// <returns>地圖進度、節點、明信片統計與總天數。</returns>
        public MapResponse GetMap(
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

            int currentNodeOrder = _dao.GetCurrentNodeOrder(auId, storyId);

            nodes = nodes
                .OrderBy(x => x.day_index)
                .ThenBy(x => x.node_order)
                .ToList();

            string defaultFog = ToAbsoluteUrl(_dao.GetDefaultFogImage() ?? DefaultFogImage, baseUrl);
            bool missingFog = false;

            foreach (MapNode node in nodes)
            {
                node.is_unlocked = IsUnlocked(node.node_order, currentNodeOrder);

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
                        node.fog_hint = "前方仍被迷霧籠罩，完成前一站任務後即可探索。";
                    }

                    // 迷霧中的站：不回傳真正的站名、照片與精確座標
                    missingFog |= node.image_url != null && node.fog_image == null;
                    node.location_name = node.fog_hint;
                    node.image_url = node.fog_image != null ? ToAbsoluteUrl(node.fog_image, baseUrl) : defaultFog;
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

        public NodeDetailResponse ArriveNode(
            int nodeId,
            double userLat,
            double userLng,
            ClaimsPrincipal user)
        {
            int auId = user.GetAuId();

            // 要照順序來：還在迷霧中的站不能打卡
            EnsureUnlocked(auId, nodeId);

            MapNode node = _dao.GetNodeLocation(nodeId);

            if (node == null)
            {
                throw new KeyNotFoundException("找不到指定節點。");
            }

            if (node.lat == 0 || node.lng == 0)
            {
                throw new InvalidOperationException("此節點尚未設定有效座標。");
            }

            double distance = CalculateDistanceMeters(
                userLat,
                userLng,
                node.lat,
                node.lng);

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

        #region 取得節點詳情

        /// <summary>節點詳情；還在迷霧中的站不能查看</summary>
        public NodeDetailResponse GetNodeDetail(int nodeId, ClaimsPrincipal user)
        {
            EnsureUnlocked(user.GetAuId(), nodeId);
            return LoadNodeDetail(nodeId);
        }

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

        /// <summary>NPC 隨機互動；還在迷霧中的站不能互動</summary>
        public NpcInteractionResponse GetNpcInteraction(int nodeId, ClaimsPrincipal user)
        {
            EnsureUnlocked(user.GetAuId(), nodeId);

            NpcInteractionResponse result =
                _dao.GetRandomNpcInteraction(nodeId);

            if (result == null)
            {
                throw new KeyNotFoundException("此節點尚未設定 NPC 互動內容。");
            }

            result.node_id = nodeId;
            result.emotion ??= "normal";
            result.skip_button_text ??= "稍後再說";

            return result;
        }

        #endregion

        #region 導航

        /// <summary>導航；還在迷霧中的站不能導航（否則會直接洩漏精確位置）</summary>
        public NavigationResponse GetNavigation(NavigationRequest req, ClaimsPrincipal user)
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

            if (node.lat == 0 || node.lng == 0)
            {
                throw new InvalidOperationException("此景點尚未設定有效座標。");
            }

            return new NavigationResponse
            {
                maps_deeplink_url =
                    $"https://www.google.com/maps/search/?api=1&query={node.lat},{node.lng}"
            };
        }

        #endregion

        #region 周邊好去

        public List<NearbyPlaceResponse> GetNearbyPlaces(
            int storyId,
            string category)
        {
            return _dao.GetNearbyPlaces(storyId, category ?? "");
        }

        #endregion

        #region 迷霧

        /// <summary>第一站固定開放；玩家抵達第 N 站後，開放第 N+1 站</summary>
        internal static bool IsUnlocked(int nodeOrder, int currentNodeOrder) =>
            nodeOrder == 1 || nodeOrder <= currentNodeOrder + 1;

        /// <summary>這一站還在迷霧中就丟出 NodeLockedException</summary>
        private void EnsureUnlocked(int auId, int nodeId)
        {
            MapDao.NodeProgress progress = _dao.GetNodeProgress(auId, nodeId);

            if (progress == null)
            {
                throw new KeyNotFoundException("找不到指定節點。");
            }

            if (!IsUnlocked(progress.node_order, progress.current_order))
            {
                throw new NodeLockedException();
            }
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