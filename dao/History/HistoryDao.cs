// 檔案路徑：System\dao\History\HistoryDao.cs
// 對應新資料表 `story_session` + `story` + `au_vlog` + `story_node`，
// 取代舊的 ep_story_progress / md_story / md_region / ep_vlog / md_story_node。
using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using backend.utils;
using backend.Models;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class HistoryDao
    {
        private readonly AppSettings _appSettings;

        public HistoryDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        // 新資料庫沒有地區主表，地區名稱改由 story 的城市 + 鄉鎮欄位組合。
        private const string RegionColumn =
            "CONCAT(COALESCE(s.city_name, ''), COALESCE(s.district_name, ''))";

        #region 取得所有過往劇本
        public List<HistoryStoryItem> GetHistoryList(int auId)
        {
            string sql = $@"
                SELECT
                    ss.s_id            AS story_id,
                    s.story_title      AS title,
                    s.story_synopsis   AS synopsis,
                    ss.completed_at    AS completed_date,
                    {RegionColumn}     AS region,
                    v.av_id            AS vlog_id
                FROM story_session ss
                INNER JOIN story s   ON s.s_id = ss.s_id
                LEFT  JOIN au_vlog v ON v.s_id = ss.s_id AND v.au_id = ss.au_id
                WHERE ss.au_id = @auId
                  AND ss.ss_status = 'completed'
                ORDER BY ss.completed_at DESC;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.Query<HistoryStoryItem>(sql, new { auId }).ToList();
            }
        }
        #endregion

        #region 取得過往劇本詳情 (包含景點清單)
        public HistoryStoryItem GetHistoryDetail(int storyId, int auId)
        {
            string sql = $@"
                SELECT
                    ss.s_id            AS story_id,
                    s.story_title      AS title,
                    s.story_synopsis   AS synopsis,
                    ss.completed_at    AS completed_date,
                    {RegionColumn}     AS region,
                    v.av_id            AS vlog_id
                FROM story_session ss
                INNER JOIN story s   ON s.s_id = ss.s_id
                LEFT  JOIN au_vlog v ON v.s_id = ss.s_id AND v.au_id = ss.au_id
                WHERE ss.s_id = @storyId
                  AND ss.au_id = @auId;
            ";

            // story_node.place_id 存的是 Neo4j UUID，place 主表的主鍵是 int p_id，
            // 兩者無法直接 JOIN，景點名稱先取節點標題。
            string spotSql = @"
                SELECT COALESCE(sn.sn_title, sn.location_codename) AS place_name
                FROM story_node sn
                WHERE sn.s_id = @storyId
                ORDER BY sn.sn_order;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();

                HistoryStoryItem item =
                    conn.QueryFirstOrDefault<HistoryStoryItem>(sql, new { storyId, auId });

                if (item == null)
                {
                    throw new Exception("找不到此過往劇本：" + storyId);
                }

                item.spots = conn.Query<string>(spotSql, new { storyId }).ToList();

                return item;
            }
        }
        #endregion
    }
}
