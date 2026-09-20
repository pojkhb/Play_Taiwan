using System;
using System.Collections.Generic;
using System.Security.Claims;
using backend.Models;
using backend.utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class HomeDao
    {
        private readonly AppSettings _appSettings;
        private readonly HttpContext _ipContext;

        public HomeDao(
            IOptions<AppSettings> appSettings,
            IHttpContextAccessor httpContextAccessor
        )
        {
            _appSettings = appSettings.Value;
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 首頁目前總覽

        public HomeOverviewResponse GetOverview()
        {
            string epId =
                _ipContext?.User?.FindFirst("ep_id")?.Value
                ?? _ipContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(epId))
            {
                throw new UnauthorizedAccessException(
                    "無法取得目前登入的探員資料，請重新登入。"
                );
            }

            using var connection = new MySqlConnection(_appSettings.mydb);

            connection.Open();

            string sql = @"
                SELECT
                    (
                        SELECT COUNT(*)
                        FROM ep_story_progress
                        WHERE ep_id = @ep_id
                          AND progress_status = 'COMPLETED'
                    ) AS completed_story_count,

                    (
                        SELECT COUNT(*)
                        FROM ep_postcard
                        WHERE ep_id = @ep_id
                    ) AS postcard_count,

                    (
                        SELECT COUNT(*)
                        FROM ep_badge
                        WHERE ep_id = @ep_id
                    ) AS badge_count,

                    (
                        SELECT COUNT(*)
                        FROM ep_vlog
                        WHERE ep_id = @ep_id
                    ) AS vlog_count;
            ";

            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@ep_id", epId);

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                throw new Exception("無法讀取首頁總覽資料。");
            }

            return new HomeOverviewResponse
            {
                ho_completed_story =
                    Convert.ToInt32(reader["completed_story_count"]),

                ho_postcard_count =
                    Convert.ToInt32(reader["postcard_count"]),

                ho_badge_count =
                    Convert.ToInt32(reader["badge_count"]),

                ho_vlog_count =
                    Convert.ToInt32(reader["vlog_count"]),

                ho_recent_cards = new List<HomeCardItem>
                {
                    new HomeCardItem
                    {
                        hc_id = 0,
                        hc_type = "START_EXPLORE",
                        hc_title = "出發探險",
                        hc_image = null
                    },
                    new HomeCardItem
                    {
                        hc_id = 1,
                        hc_type = "TRAVEL_HISTORY",
                        hc_title = "過往旅途",
                        hc_image = null
                    }
                }
            };
        }

        #endregion
    }
}