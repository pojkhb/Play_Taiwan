// 檔案路徑：System\dao\VisitorVlogDao.cs
// 對應新資料表 `user_task_record` + `task` + `story_node` + `record_media` + `au_vlog`，
// 取代舊的 ep_task_record / md_story_node / md_task_media / ep_vlog。
// 注意：user_task_record 沒有 story_id / node_id，必須 JOIN task 才能篩出某個劇本的作答紀錄。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using backend.ViewModels;
using backend.utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class VisitorVlogDao
    {
        private readonly AppSettings _appSettings;

        public VisitorVlogDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        #region 取得遊玩結果（時長 + 景點清單）
        /// <summary>
        /// 依 au_id + story_id 從 user_task_record 算出實際遊玩時長，
        /// 並 JOIN story_node 組出玩家走過的景點清單（依作答時間排序）。
        /// 新表只有 answered_at 一個時間欄位，時長取最早與最晚作答時間的差。
        /// </summary>
        public async Task<(string playTime, List<SpotHistoryItem> spots)> GetPlayResultAsync(int auId, int storyId)
        {
            string timeSql = @"
                SELECT MIN(utr.answered_at) AS start_time,
                       MAX(utr.answered_at) AS end_time
                FROM user_task_record utr
                INNER JOIN task t ON t.task_id = utr.task_id
                WHERE utr.au_id = @auId AND t.story_id = @storyId;
            ";

            string spotSql = @"
                SELECT sn.sn_title              AS spot_name,
                       sn.location_codename     AS location_codename,
                       MIN(utr.answered_at)     AS visit_at
                FROM user_task_record utr
                INNER JOIN task t        ON t.task_id = utr.task_id
                INNER JOIN story_node sn ON sn.sn_id = t.node_id
                WHERE utr.au_id = @auId AND t.story_id = @storyId
                GROUP BY sn.sn_id, sn.sn_title, sn.location_codename
                ORDER BY visit_at;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                await conn.OpenAsync();

                var range = await conn.QueryFirstOrDefaultAsync<(DateTime? start_time, DateTime? end_time)>(
                    timeSql, new { auId, storyId });

                string playTime = FormatPlayTime(range.start_time, range.end_time);

                var spots = (await conn.QueryAsync<(string spot_name, string location_codename, DateTime? visit_at)>(
                        spotSql, new { auId, storyId }))
                    .Select(r => new SpotHistoryItem
                    {
                        spot_name = string.IsNullOrWhiteSpace(r.spot_name) ? "未知景點" : r.spot_name,
                        location_codename = r.location_codename,
                        visit_time = r.visit_at?.ToString("yyyy-MM-dd HH:mm")
                    })
                    .ToList();

                return (playTime, spots);
            }
        }

        private static string FormatPlayTime(DateTime? startTime, DateTime? endTime)
        {
            if (!startTime.HasValue || !endTime.HasValue || endTime <= startTime)
            {
                return "2.5小時";
            }

            TimeSpan span = endTime.Value - startTime.Value;
            return span.TotalHours >= 1
                ? $"{span.TotalHours:0.0}小時"
                : $"{span.TotalMinutes:0}分鐘";
        }
        #endregion

        #region 取得該劇本的所有任務素材網址
        public async Task<List<string>> GetTaskMediaUrlsAsync(int auId, int storyId)
        {
            string sql = @"
                SELECT DISTINCT rm.media_url
                FROM user_task_record utr
                INNER JOIN task t          ON t.task_id = utr.task_id
                INNER JOIN record_media rm ON rm.record_id = utr.record_id
                WHERE utr.au_id = @auId
                  AND t.story_id = @storyId
                  AND rm.media_url IS NOT NULL;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                await conn.OpenAsync();
                return (await conn.QueryAsync<string>(sql, new { auId, storyId })).ToList();
            }
        }
        #endregion

        #region 寫入 / 更新遊客 VLOG
        /// <summary>
        /// 寫入 au_vlog。新表沒有外部 task_id 欄位，
        /// 因此以「同一位使用者的同一個劇本只有一支 VLOG」為準做更新或新增。
        /// av_vlog_status：1=待處理、2=處理中、3=已完成、4=失敗。
        /// </summary>
        public async Task SaveVlogAsync(int auId, int storyId, string videoUrl)
        {
            string updateSql = @"
                UPDATE au_vlog
                SET av_video_url = @videoUrl, av_vlog_status = 3
                WHERE au_id = @auId AND s_id = @storyId;
            ";

            string insertSql = @"
                INSERT INTO au_vlog (au_id, s_id, av_video_url, av_vlog_status)
                VALUES (@auId, @storyId, @videoUrl, 3);
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                await conn.OpenAsync();

                int rows = await conn.ExecuteAsync(updateSql, new { auId, storyId, videoUrl });
                if (rows == 0)
                {
                    await conn.ExecuteAsync(insertSql, new { auId, storyId, videoUrl });
                }
            }
        }
        #endregion
    }
}
