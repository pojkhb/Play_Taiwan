// 檔案路徑：System\Services\MapService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.utils;

namespace backend.Services
{
    public class MapService
    {
        private const double UnlockRadiusMeters = 50.0;

        private readonly MapDao _dao;
        private readonly GeocodingService _geocodingService;

        public MapService(MapDao dao, GeocodingService geocodingService)
        {
            _dao = dao;
            _geocodingService = geocodingService;
        }

        #region 取得地圖

        /// <summary>
        /// 取得指定劇本的地圖資訊。
        /// 後端依 story_session.ss_current 判斷節點是否解鎖。
        /// 新資料表沒有 day_index，目前所有節點都歸在第一日。
        /// </summary>
        /// <param name="storyId">劇本代號，對應 story.s_id。</param>
        /// <param name="user">目前登入使用者 JWT Claims。</param>
        /// <returns>地圖進度、節點、明信片統計與總天數。</returns>
        public MapResponse GetMap(
            int storyId,
            ClaimsPrincipal user)
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

            foreach (MapNode node in nodes)
            {
                // 第一個節點固定開放。
                // 玩家完成第 N 節點後，開放第 N+1 節點。
                node.is_unlocked =
                    node.node_order == 1 ||
                    node.node_order <= currentNodeOrder + 1;

                // 已解鎖後不再顯示迷霧文字。
                if (node.is_unlocked)
                {
                    node.fog_hint = null;
                }
                else if (string.IsNullOrWhiteSpace(node.fog_hint))
                {
                    node.fog_hint = "前方仍被迷霧籠罩，完成前一站任務後即可探索。";
                }

                node.child_node_ids ??= new List<int>();
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

            return GetNodeDetail(nodeId);
        }

        #endregion

        #region 取得節點詳情

        public NodeDetailResponse GetNodeDetail(int nodeId)
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

        public NpcInteractionResponse GetNpcInteraction(int nodeId)
        {
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

        public NavigationResponse GetNavigation(NavigationRequest req)
        {
            if (req == null || req.node_id <= 0)
            {
                throw new ArgumentException("node_id 不可為空白。");
            }

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