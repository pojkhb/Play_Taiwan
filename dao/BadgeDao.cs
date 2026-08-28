using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using backend.utils;
using backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class BadgeResponse
    {
        public string badge_id { get; set; }
        public string badge_name { get; set; }
        public string description { get; set; }
        public string image_url { get; set; }
        public bool is_owned { get; set; }
        public DateTime? obtained_at { get; set; }
        public string series_id { get; set; }
        public string series_name { get; set; }
    }

    public class BadgeDao
    {
        private readonly AppSettings _appSettings;
        private readonly HttpContext _ipContext;

        public BadgeDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 取得徽章圖鑑狀態（依系列分組 + 是否擁有）
        public List<BadgeResponse> GetAllBadgeStatus(string ep_id)
        {
            string sql = @"
                SELECT 
                    m.badge_id, 
                    m.badge_name, 
                    m.description, 
                    m.image_url,
                    m.series_id,
                    m.series_name,
                    IF(e.ep_id IS NOT NULL, TRUE, FALSE) AS is_owned,
                    e.obtained_at
                FROM md_badge m
                LEFT JOIN ep_badge e ON m.badge_id = e.badge_id AND e.ep_id = @ep_id
                ORDER BY m.series_id, m.badge_id ASC;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.Query<BadgeResponse>(sql, new { ep_id = ep_id }).ToList();
            }
        }
        #endregion
    }
}