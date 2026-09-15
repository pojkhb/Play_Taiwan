// 檔案路徑：System\dao\VisitorVlogDao.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using backend.Models;
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

        /// <summary>
        /// 依 ep_id + story_id，從 ep_task_record 算出實際遊玩時長，
        /// 並 JOIN md_story_node / md_place 組出玩家走過的景點清單（依完成時間排序）。
        /// ⚠️ 欄位名稱（task_id、completed_at、created_at、place_id、codename）需依實際 schema 核對。
        /// </summary>
        public async Task<(string playTime, List<SpotHistoryItem> spots)> GetPlayResultAsync(string epId, string storyId)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            await connection.OpenAsync();

            string timeSql = @"
        SELECT MIN(created_at) AS start_time, MAX(completed_at) AS end_time
        FROM ep_task_record
        WHERE ep_id = @ep_id AND story_id = @story_id;
    ";

            DateTime? startTime = null, endTime = null;
            using (var cmd = new MySqlCommand(timeSql, connection))
            {
                cmd.Parameters.AddWithValue("@ep_id", epId);
                cmd.Parameters.AddWithValue("@story_id", storyId);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0)) startTime = reader.GetDateTime(0);
                    if (!reader.IsDBNull(1)) endTime = reader.GetDateTime(1);
                }
            }

            string playTime = "2.5小時";
            if (startTime.HasValue && endTime.HasValue && endTime > startTime)
            {
                var span = endTime.Value - startTime.Value;
                playTime = span.TotalHours >= 1
                    ? $"{span.TotalHours:0.0}小時"
                    : $"{span.TotalMinutes:0}分鐘";
            }

            string spotSql = @"
        SELECT sn.place_name_text, sn.location_codename, tr.completed_at
        FROM ep_task_record tr
        INNER JOIN md_story_node sn ON tr.node_id = sn.node_id
        WHERE tr.ep_id = @ep_id AND tr.story_id = @story_id
        ORDER BY tr.completed_at ASC;
    ";

            var spots = new List<SpotHistoryItem>();
            using (var cmd = new MySqlCommand(spotSql, connection))
            {
                cmd.Parameters.AddWithValue("@ep_id", epId);
                cmd.Parameters.AddWithValue("@story_id", storyId);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    spots.Add(new SpotHistoryItem
                    {
                        spot_name = reader.IsDBNull(0) ? "未知景點" : reader.GetString(0),
                        location_codename = reader.IsDBNull(1) ? null : reader.GetString(1),
                        visit_time = reader.IsDBNull(2) ? null : reader.GetDateTime(2).ToString("yyyy-MM-dd HH:mm")
                    });
                }
            }

            return (playTime, spots);
        }

        /// <summary>
        /// 依 story_id 撈出這個劇本所有任務節點對應的素材網址（md_task_media）。
        /// </summary>
        public async Task<List<string>> GetTaskMediaUrlsAsync(string epId, string storyId)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            await connection.OpenAsync();

            string sql = @"
        SELECT DISTINCT tm.media_url
        FROM ep_task_record tr
        INNER JOIN md_task_media tm ON tr.task_id = tm.task_id
        WHERE tr.ep_id = @ep_id AND tr.story_id = @story_id;
    ";

            var urls = new List<string>();
            using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@ep_id", epId);
            cmd.Parameters.AddWithValue("@story_id", storyId);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0)) urls.Add(reader.GetString(0));
            }
            return urls;
        }

        /// <summary>
        /// 對應 ep_vlog 表：ep_id、vlog_id、story_id、video_url、thumbnail_url、completed_at。
        /// </summary>
        public async Task SaveVlogAsync(string epId, string vlogId, string storyId, string videoUrl)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            await connection.OpenAsync();

            string sql = @"
                INSERT INTO ep_vlog (ep_id, vlog_id, story_id, video_url, thumbnail_url, completed_at)
                VALUES (@ep_id, @vlog_id, @story_id, @video_url, NULL, NOW())
                ON DUPLICATE KEY UPDATE
                    video_url = @video_url,
                    completed_at = NOW();
            ";

            using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@ep_id", epId);
            cmd.Parameters.AddWithValue("@vlog_id", vlogId);
            cmd.Parameters.AddWithValue("@story_id", storyId ?? "");
            cmd.Parameters.AddWithValue("@video_url", videoUrl);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}