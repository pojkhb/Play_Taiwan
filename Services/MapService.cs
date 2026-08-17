using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    public class MapService
    {
        private const double UnlockRadiusMeters = 50.0;

        private readonly MapDao _dao;

        public MapService(MapDao dao)
        {
            _dao = dao;
        }

        #region 取得地圖

        public MapResponse GetMap(
            string storyId,
            ClaimsPrincipal user)
        {
            if (string.IsNullOrWhiteSpace(storyId))
            {
                throw new ArgumentException("story_id 不可為空白。");
            }

            string epId = GetCurrentEpId(user);

            List<MapNode> nodes = _dao.GetStoryNodes(storyId);

            if (nodes == null || nodes.Count == 0)
            {
                throw new KeyNotFoundException("此劇本沒有可用的地圖節點。");
            }

            int currentNodeOrder =
                _dao.GetCurrentNodeOrder(epId, storyId);

            nodes = nodes
                .OrderBy(x => x.node_order)
                .ToList();

            foreach (MapNode node in nodes)
            {
                node.is_unlocked =
                    node.node_order == 1 ||
                    node.node_order <= currentNodeOrder + 1;

                node.fog_hint = node.is_unlocked
                    ? null
                    : "前方仍被迷霧籠罩，完成前一站任務後即可探索。";

                node.child_node_ids ??= new List<string>();
            }

            for (int i = 0; i < nodes.Count - 1; i++)
            {
                nodes[i].child_node_ids.Add(nodes[i + 1].node_id);
            }

            return new MapResponse
            {
                story_id = storyId,
                unlocked_node_count = nodes.Count(x => x.is_unlocked),
                total_node_count = nodes.Count,
                postcard_unlocked_count =
                    _dao.GetUnlockedPostcardCount(epId, storyId),
                postcard_total_count =
                    _dao.GetTotalPostcardCount(storyId),
                nodes = nodes,
                day_index = 1,
                total_days = 1
            };
        }

        #endregion

        #region GPS 確認抵達

        public NodeDetailResponse ArriveNode(
            string nodeId,
            double userLat,
            double userLng,
            ClaimsPrincipal user)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("node_id 不可為空白。");
            }

            string epId = GetCurrentEpId(user);

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

            _dao.UnlockNode(epId, nodeId);

            return GetNodeDetail(nodeId);
        }

        #endregion

        #region 取得節點詳情

        public NodeDetailResponse GetNodeDetail(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("node_id 不可為空白。");
            }

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

        public NpcInteractionResponse GetNpcInteraction(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("node_id 不可為空白。");
            }

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
            if (req == null || string.IsNullOrWhiteSpace(req.node_id))
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
            string storyId,
            string category)
        {
            if (string.IsNullOrWhiteSpace(storyId))
            {
                throw new ArgumentException("story_id 不可為空白。");
            }

            return _dao.GetNearbyPlaces(storyId, category ?? "");
        }

        #endregion

        #region 私有邏輯

        private string GetCurrentEpId(ClaimsPrincipal user)
        {
            if (user?.Identity?.IsAuthenticated != true)
            {
                throw new UnauthorizedAccessException("請先登入後再使用地圖功能。");
            }

            string epId =
                user.FindFirst("ep_id")?.Value ??
                user.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                user.FindFirst("sub")?.Value;

            if (string.IsNullOrWhiteSpace(epId))
            {
                throw new UnauthorizedAccessException(
                    "JWT 中沒有 ep_id、NameIdentifier 或 sub Claim。");
            }

            return epId;
        }

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
    }
}