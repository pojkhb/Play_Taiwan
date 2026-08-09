using System;
using System.Collections.Generic;
using backend.utils;
using backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class HistoryDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        private readonly HttpContext _ipContext;

        public HistoryDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 取得所有過往劇本
        public List<HistoryStoryItem> GetHistoryList()
        {
            // TODO: 資料表尚未建置，預計欄位:
            // ep_story_progress(ep_id, story_id, started_at, completed_at)
            return new List<HistoryStoryItem>
            {
                new HistoryStoryItem
                {
                    story_id = "MOCK-STORY-001",
                    title = "府城儒生失落卷",
                    synopsis = "尋著百年軌跡，找回失落記憶……",
                    completed_date = new DateTime(2026, 8, 9),
                    region = "台南永康區",
                    route_summary = new List<string> { "臺南孔廟", "鎮生堂文學文史學院", "林百貨", "澎湖武廟", "看西街武廟", "大天后宮" },
                    vlog_id = "MOCK-VLOG-001",
                    postcard_review_url = null
                }
            };
        }
        #endregion

        #region 取得過往劇本詳情
        public HistoryStoryItem GetHistoryDetail(string story_id)
        {
            // TODO: SELECT * FROM ep_story_progress JOIN story ON ... WHERE story_id = @story_id
            return GetHistoryList()[0];
        }
        #endregion
    }
}