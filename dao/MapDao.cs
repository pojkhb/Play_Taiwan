using System.Collections.Generic;
using backend.utils;
using backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class MapDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        private readonly HttpContext _ipContext;

        public MapDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 取得地圖
        public MapResponse GetMap(string story_id)
        {
            // TODO: 資料表尚未建置，預計欄位:
            // map_node(node_id, story_id, location_name, lat, lng, is_night_only, parent_node_id, fog_hint)
            // ep_node_unlock(ep_id, node_id, unlocked_at)
            return new MapResponse
            {
                story_id = story_id,
                unlocked_node_count = 1,
                total_node_count = 16,
                postcard_unlocked_count = 1,
                postcard_total_count = 10,
                nodes = new List<MapNode>
                {
                    new MapNode
                    {
                        node_id = "NODE-001",
                        location_name = "臺南孔廟",
                        lat = 22.9908, lng = 120.2039,
                        is_unlocked = true,
                        is_night_only = false,
                        fog_hint = null,
                        child_node_ids = new List<string> { "NODE-002" }
                    }
                },
                day_index = 1,
                total_days = 2
            };
        }
        #endregion

        #region GPS 確認抵達
        public NodeDetailResponse ArriveNode(string node_id, double lat, double lng)
        {
            // TODO: INSERT INTO ep_node_unlock(ep_id, node_id, unlocked_at) ...
            return GetNodeDetail(node_id);
        }
        #endregion

        #region 取得節點詳情
        public NodeDetailResponse GetNodeDetail(string node_id)
        {
            // TODO: 資料表 map_node + node_npc + task 尚未建置
            return new NodeDetailResponse
            {
                node_id = node_id,
                location_name = "臺南孔廟",
                npc_name = "書院先生",
                intro_story = "進入此地，你將看見一方高懸的古老匾額。它們皆出歷代皇帝御筆，跨越不同的歲月，至今仍留在這座古老府學之中。",
                opening_hours = "08:30-17:30",
                nearby_food = new List<string> { "鄭記粽子", "福記粽子" },
                task_id = "TASK-001",
                review_story_url = null
            };
        }
        #endregion

        #region NPC 隨機互動
        public NpcInteractionResponse GetNpcInteraction(string node_id)
        {
            // TODO: 資料表 npc_dialogue_pool(node_id, dialogue_text) 隨機抽取
            return new NpcInteractionResponse
            {
                node_id = node_id,
                npc_dialogue = "台南孔廟是全台灣第一座孔廟喔！"
            };
        }
        #endregion

        #region 導航
        public NavigationResponse GetNavigation(NavigationRequest req)
        {
            // TODO: 依 node_id 查詢 lat/lng 組成 Google Maps deeplink
            return new NavigationResponse
            {
                maps_deeplink_url = $"https://www.google.com/maps/search/?api=1&query=22.9908,120.2039"
            };
        }
        #endregion

        #region 周邊好去
        public List<NearbyPlaceResponse> GetNearbyPlaces(string story_id, string category)
        {
            // TODO: 資料表 nearby_place(place_id, story_id, category, name, address, open_time) 尚未建置
            return new List<NearbyPlaceResponse>
            {
                new NearbyPlaceResponse
                {
                    place_id = "PLACE-001",
                    category = "飲食",
                    name = "福記肉圓（府前路215）",
                    address = "府前路215",
                    open_time = "06:30-18:00",
                    photo_urls = new List<string>(),
                    maps_deeplink_url = null
                }
            };
        }
        #endregion
    }
}