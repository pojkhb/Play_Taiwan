using System.Collections.Generic;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    public class MapService
    {
        private readonly MapDao _dao;
        public MapService(MapDao dao) { _dao = dao; }

        #region 取得地圖
        public MapResponse GetMap(string story_id)
        {
            return _dao.GetMap(story_id);
        }
        #endregion

        #region GPS 確認抵達
        public NodeDetailResponse ArriveNode(string node_id, double lat, double lng)
        {
            // TODO: 依 node_id 對應 GPS 座標範圍(半徑50m) 驗證 lat/lng 是否在範圍內
            return _dao.ArriveNode(node_id, lat, lng);
        }
        #endregion

        #region 取得節點詳情
        public NodeDetailResponse GetNodeDetail(string node_id)
        {
            return _dao.GetNodeDetail(node_id);
        }
        #endregion

        #region NPC 隨機互動
        public NpcInteractionResponse GetNpcInteraction(string node_id)
        {
            return _dao.GetNpcInteraction(node_id);
        }
        #endregion

        #region 導航
        public NavigationResponse GetNavigation(NavigationRequest req)
        {
            return _dao.GetNavigation(req);
        }
        #endregion

        #region 周邊好去
        public List<NearbyPlaceResponse> GetNearbyPlaces(string story_id, string category)
        {
            return _dao.GetNearbyPlaces(story_id, category);
        }
        #endregion
    }
}