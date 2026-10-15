// 檔案路徑：System\dao\Home\HomeDao.cs
// 對應新資料表 `story_session`、`postcard`、`au_badge`、`au_vlog`，
// 取代舊的 ep_story_progress / ep_postcard / ep_badge / ep_vlog。
using System.Collections.Generic;
using System.Linq;
using Dapper;
using backend.utils;
using backend.Models;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class HomeDao
    {
        private readonly AppSettings _appSettings;

        public HomeDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        #region 首頁目前總覽
        public HomeOverviewResponse GetOverview(int auId)
        {
            string countSql = @"
                SELECT
                    (SELECT COUNT(*) FROM story_session
                      WHERE au_id = @auId AND ss_status = 'completed')      AS ho_completed_story,
                    (SELECT COUNT(*) FROM postcard  WHERE au_id = @auId)     AS ho_postcard_count,
                    (SELECT COUNT(*) FROM au_badge  WHERE au_id = @auId)     AS ho_badge_count,
                    (SELECT COUNT(*) FROM au_vlog
                      WHERE au_id = @auId AND av_vlog_status = 3)            AS ho_vlog_count;
            ";

            // 最近的劇本/明信片/VLOG 混合排序，取最新 10 筆當首頁卡片。
            string cardSql = @"
                SELECT hc_id, hc_type, hc_title, hc_image FROM (
                    SELECT s.s_id   AS hc_id, 'story'    AS hc_type,
                           s.story_title AS hc_title, NULL AS hc_image,
                           s.updated_at  AS sort_at
                      FROM story s
                     WHERE s.au_id = @auId
                    UNION ALL
                    SELECT p.p_id, 'postcard', p.p_name, p.p_imag_url, p.created_at
                      FROM postcard p
                     WHERE p.au_id = @auId
                    UNION ALL
                    SELECT v.av_id, 'vlog', v.av_title, v.av_thumbnail, v.created_at
                      FROM au_vlog v
                     WHERE v.au_id = @auId AND v.av_vlog_status = 3
                ) AS recent
                ORDER BY recent.sort_at DESC
                LIMIT 10;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();

                HomeOverviewResponse overview =
                    conn.QueryFirst<HomeOverviewResponse>(countSql, new { auId });

                overview.ho_recent_cards =
                    conn.Query<HomeCardItem>(cardSql, new { auId }).ToList();

                return overview;
            }
        }
        #endregion
    }
}
