// 檔案路徑：System\Services\StoryService.cs
using System;
using System.Collections.Generic;
using backend.dao;
using backend.Models;
using backend.ViewModels;
using System.Threading.Tasks;


namespace backend.Services
{
    public class StoryService
    {
        private readonly StoryDao _dao;
        private readonly Neo4jService _neo4jService;


        public StoryService(StoryDao dao, Neo4jService neo4jService)
        {
            _dao = dao;
            _neo4jService = neo4jService;
        }


        public StoryWheelSpinResponse WheelSpin()
        {
            return _dao.WheelSpin();
        }


        public List<StoryWheelSpinResponse> GetRegions(string mode, string cityName)
        {
            return _dao.GetRegions(mode, cityName);
        }


        public List<StoryOptionResponse> GenerateOptions(StoryGenerateRequest req)
        {
            return _dao.GenerateStories(req);
        }


        public StoryDetailResponse GetDetail(string storyId)
        {
            return _dao.GetDetail(storyId);
        }


        // === 變更重點：原本只讀不寫，現在會把該劇本標記為「正在遊玩中」 ===
        public StoryDetailResponse ConfirmStory(StoryConfirmRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.story_id))
                throw new Exception("請提供 story_id");

            bool success = _dao.SetStoryPlaying(req.story_id);
            if (!success)
                throw new Exception($"找不到 story_id = {req.story_id} 的劇本");

            return _dao.GetDetail(req.story_id);
        }

        // === 新增：玩家結束/退出劇本時呼叫 ===
        public bool EndStory(string storyId)
        {
            if (string.IsNullOrWhiteSpace(storyId))
                throw new Exception("請提供 story_id");

            return _dao.ClearStoryPlaying(storyId);
        }

        // === 新增：查目前哪個劇本正在進行中 ===
        public object GetCurrentPlayingStory()
        {
            return _dao.GetCurrentPlayingStory();
        }


        #region GPS 定位生成劇本 相關方法


        public ScriptBlueprintData GetFullDetail(string storyId)
        {
            return _dao.GetFullDetail(storyId);
        }


        public async Task<string> SaveFullAiGeneratedStory(string epId, string regionId, string cityName, ScriptBlueprintData data)
        {
            return await _dao.SaveFullAiGeneratedStory(epId, regionId, cityName, data);
        }


        public string FindRegionIdByName(string cityName, string townName)
        {
            return _dao.FindRegionIdByName(cityName, townName);
        }


        public List<NearbyPlaceDistanceResponse> GetNearbyPlacesByDistance(double lat, double lng, double radiusKm)
        {
            return _dao.GetNearbyPlacesByDistance(lat, lng, radiusKm);
        }


        #endregion


        #region Neo4j 附近景點查詢


        /// <summary>
        /// 依使用者 GPS 座標，透過 Neo4j 查詢半徑範圍內的景點（依距離由近到遠排序，已去除重複節點）。
        /// </summary>
        public async Task<List<NearbyAttractionNode>> GetNearbyAttractionsAsync(double lat, double lng, double radiusKm)
        {
            string cypherQuery = @"
                MATCH (a:Attraction)
                WHERE a.lat IS NOT NULL AND a.lon IS NOT NULL
                WITH a, point({latitude: $lat, longitude: $lng}) AS origin, point({latitude: a.lat, longitude: a.lon}) AS aPoint
                WITH a, point.distance(origin, aPoint) AS distance_m
                WHERE distance_m <= $radius_m
                RETURN DISTINCT a.name AS name, a.lat AS lat, a.lon AS lon, distance_m
                ORDER BY distance_m ASC
            ";


            var parameters = new { lat = lat, lng = lng, radius_m = radiusKm * 1000 };


            var result = await _neo4jService.ExecuteCypherAsync<List<NearbyAttractionNode>>(cypherQuery, parameters);
            return result ?? new List<NearbyAttractionNode>();
        }


        #endregion


        #region Agent 即時推薦（/spin 用）


        /// <summary>
        /// 依城市/行政區名稱，將城市/行政區轉為經緯度，並將 Agent 推薦結果存進 md_agent_recommendation。
        /// </summary>
        public string SaveAgentRecommendation(string epId, string cityName, string townName, double lat, double lng, AgentOrchestrateResponse result)
        {
            return _dao.SaveAgentRecommendation(epId, cityName, townName, lat, lng, result);
        }


        #endregion
    }
}