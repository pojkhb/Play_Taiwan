using System.Collections.Generic;
using backend.utils;
using backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class BadgeDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        private readonly HttpContext _ipContext;

        public BadgeDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 取得我的所有徽章
        public List<BadgeResponse> GetMyBadges()
        {
            // TODO: 資料表尚未建置，預計欄位:
            // badge(badge_id, badge_name, badge_type, image_url)
            // ep_badge(ep_id, badge_id, obtained_date)
            return new List<BadgeResponse>();
        }
        #endregion
    }
}