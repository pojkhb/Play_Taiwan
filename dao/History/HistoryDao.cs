// 檔案路徑：System\dao\History\HistoryDao.cs
// 對應新資料表 `story_session` + `story` + `au_vlog` + `story_node`（照片：`user_task_record` + `record_media`），
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

            // story_node.place_id 是 Neo4j UUID，透過 place_type 橋接到 place 取真正的景點名稱；橋接不到時用節點標題
            string spotSql = $@"
                SELECT COALESCE(p.p_name, sn.sn_title, sn.location_codename) AS place_name
                FROM story_node sn
                {MapDao.PlaceBridgeJoin}
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

        #region 玩家在過往劇本拍的照片
        /// <summary>玩家拍的一個素材（照片、影片或錄音都有，由 Service 篩出照片）</summary>
        public class MediaRow
        {
            public int story_id { get; set; }
            public int node_id { get; set; }
            public int node_order { get; set; }
            public string place_name { get; set; }
            public string url { get; set; }
        }

        /// <summary>
        /// 玩家在這些劇本作答時上傳的素材：第一個存在 user_task_record.answer_media_url，其餘在 record_media。
        /// 依劇本、站的順序、作答時間排列。
        /// </summary>
        public List<MediaRow> GetMedia(int auId, List<int> storyIds)
        {
            if (storyIds == null || storyIds.Count == 0) return new List<MediaRow>();

            string sql = $@"
                SELECT m.story_id, sn.sn_id AS node_id, sn.sn_order AS node_order,
                       COALESCE(p.p_name, sn.sn_title, sn.location_codename) AS place_name, m.url
                FROM (
                    SELECT t.story_id, t.node_id, utr.answered_at, utr.record_id, 0 AS media_seq, utr.answer_media_url AS url
                    FROM user_task_record utr
                    INNER JOIN task t ON t.task_id = utr.task_id
                    WHERE utr.au_id = @auId AND t.story_id IN @storyIds
                      AND utr.answer_media_url IS NOT NULL AND utr.answer_media_url <> ''
                    UNION ALL
                    SELECT t.story_id, t.node_id, utr.answered_at, utr.record_id, rm.media_id, rm.media_url
                    FROM user_task_record utr
                    INNER JOIN task t          ON t.task_id = utr.task_id
                    INNER JOIN record_media rm ON rm.record_id = utr.record_id
                    WHERE utr.au_id = @auId AND t.story_id IN @storyIds AND rm.media_url <> ''
                ) m
                INNER JOIN story_node sn ON sn.sn_id = m.node_id
                {MapDao.PlaceBridgeJoin}
                ORDER BY m.story_id, sn.sn_order, m.answered_at, m.record_id, m.media_seq;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.Query<MediaRow>(sql, new { auId, storyIds }).ToList();
            }
        }
        #endregion

        #region 過往劇本的每一站
        /// <summary>過往劇本的一站；景點照片由 HistoryService 依 place_id 向 Neo4j 拿</summary>
        public class StationRow
        {
            public int story_id { get; set; }
            public int node_id { get; set; }
            public int node_order { get; set; }
            public string place_name { get; set; }
            public string place_id { get; set; }
        }

        /// <summary>
        /// 玩家玩完的這些劇本的每一站，依劇本、站的順序排列。
        /// 只回傳玩完的劇本：還沒玩完的劇本，沒抵達的站還在迷霧中，不能給景點照片。
        /// </summary>
        public List<StationRow> GetCompletedStations(int auId, List<int> storyIds)
        {
            if (storyIds == null || storyIds.Count == 0) return new List<StationRow>();

            string sql = $@"
                SELECT sn.s_id AS story_id, sn.sn_id AS node_id, sn.sn_order AS node_order,
                       COALESCE(p.p_name, sn.sn_title, sn.location_codename) AS place_name, sn.place_id
                FROM story_node sn
                {MapDao.PlaceBridgeJoin}
                WHERE sn.s_id IN @storyIds
                  AND EXISTS (SELECT 1 FROM story_session ss
                               WHERE ss.s_id = sn.s_id AND ss.au_id = @auId AND ss.ss_status = 'completed')
                ORDER BY sn.s_id, sn.sn_order;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.Query<StationRow>(sql, new { auId, storyIds }).ToList();
            }
        }
        #endregion
    }
}
