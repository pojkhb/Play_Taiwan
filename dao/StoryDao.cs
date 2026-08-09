using System;
using System.Collections.Generic;
using backend.utils;
using backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class StoryDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        private readonly HttpContext _ipContext;
        private static readonly Random _rand = new Random();

        public StoryDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 轉盤抽取地區
        public StoryWheelSpinResponse SpinWheel()
        {
            // TODO: 資料表 region_pool(region_id, region_name) 尚未建置，暫用固定清單
            string[] regions = { "台南安平", "台中一中商圈", "新竹北埔", "台北北投", "花蓮鳳林" };
            return new StoryWheelSpinResponse { region = regions[_rand.Next(regions.Length)] };
        }
        #endregion

        #region 產生劇本選項
        public List<StoryOptionResponse> GenerateStoryOptions(StoryGenerateRequest req)
        {
            // TODO: 資料表尚未建置，預計欄位:
            // story(story_id, title, prologue, category, transport, region, created_by_ai)
            // story_badge_expect(story_id, badge_name)
            // story_route_node(story_id, node_order, location_name)
            return new List<StoryOptionResponse>
            {
                new StoryOptionResponse
                {
                    story_id = "MOCK-STORY-001",
                    title = "府城守護者的隱藏之謎",
                    prologue = "很久以前，府城有一個守護者，他留下了三個線索，遺落的資訊等待人查詢……",
                    category = "文史、古蹟、家庭",
                    transport = "捷運 + 步行",
                    expected_badges = new List<string> { "府城系列" },
                    expected_postcards = 6,
                    region = req.region,
                    route_preview = new List<string> { "赤崁樓", "神農街", "台南孔廟" }
                }
            };
        }
        #endregion

        #region 劇情觀看更多
        public StoryDetailResponse GetStoryDetail(string story_id)
        {
            // TODO: 依 story_id 查詢 story + story_route_node
            return new StoryDetailResponse
            {
                story_id = story_id,
                title = "府城儒生失落卷",
                subtitle = "尋著百年軌跡，找回失落記憶",
                synopsis = "清朝年間，一位府城儒生在府城遺失了他重要的記憶，尋著百年軌跡，找回失落記憶。",
                route_nodes = new List<StoryOptionResponse.RouteNode>()
            };
        }
        #endregion

        #region 確認選卷
        public void ConfirmStory(StoryConfirmRequest req)
        {
            // TODO: INSERT INTO ep_story_progress(ep_id, story_id, started_at) ...
        }
        #endregion

        #region 劇本結束總結
        public StoryEndingResponse GetEnding(string story_id)
        {
            // TODO: 依 story_id + ep_id 統計 ep_task_record, ep_postcard, gps_step_log
            return new StoryEndingResponse
            {
                story_id = story_id,
                title = "府城儒生的失落卷",
                walked_steps = 8432,
                task_completion_ratio = "16/16",
                postcard_completion_ratio = "10/10",
                ending_type = "一般結局"
            };
        }
        #endregion
    }
}