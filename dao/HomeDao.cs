using System.Collections.Generic;
using backend.utils;
using backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class HomeDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        private readonly HttpContext _ipContext;

        public HomeDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 首頁目前總覽
        public HomeOverviewResponse GetOverview()
        {
            // TODO: 資料表尚未建置，預計欄位:
            // ep_story_progress(ep_id, story_id, completed_at)
            // ep_postcard(ep_id, postcard_id)
            // ep_badge(ep_id, badge_id)
            // ep_vlog(ep_id, vlog_id)
            return new HomeOverviewResponse
            {
                completed_story_count = 1,
                postcard_count = 10,
                badge_count = 1,
                vlog_count = 1,
                recent_cards = new List<HomeCardItem>()
            };
        }
        #endregion
    }
}