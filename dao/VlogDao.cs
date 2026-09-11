// 檔案路徑：System\dao\VlogDao.cs
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MySqlConnector;
using backend.ViewModels;
using backend.utils;

namespace backend.dao
{
    /// <summary>
    /// 探員 Vlog 完成紀錄 (ep_vlog) 資料存取層。
    /// 對應實際表結構：ep_vlog_id(PK,自增) / ep_id / vlog_id / story_id / video_url / thumbnail_url / completed_at。
    /// 因為 completed_at 為 not null，此表只在影片「真正合成完成」時才寫入一筆，不記錄處理中狀態。
    /// </summary>
    public class VlogDao
    {
        private readonly string _connectionString;

        public VlogDao(IOptions<AppSettings> appSettings)
        {
            _connectionString = appSettings.Value.mydb;
        }

        /// <summary>依 vlog_id（即外部 API 的 task_id）查詢是否已經寫入過完成紀錄，避免輪詢重複寫入。</summary>
        public async Task<EpVlog> GetByVlogIdAsync(string vlogId)
        {
            const string sql = @"SELECT ep_id, vlog_id, story_id, video_url, thumbnail_url, completed_at
                                  FROM ep_vlog
                                  WHERE vlog_id = @vlog_id";

            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@vlog_id", vlogId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new EpVlog
            {
                EpId = reader.GetString("ep_id"),
                VlogId = reader.GetString("vlog_id"),
                StoryId = reader.IsDBNull(reader.GetOrdinal("story_id")) ? null : reader.GetString("story_id"),
                VideoUrl = reader.IsDBNull(reader.GetOrdinal("video_url")) ? null : reader.GetString("video_url"),
                ThumbnailUrl = reader.IsDBNull(reader.GetOrdinal("thumbnail_url")) ? null : reader.GetString("thumbnail_url"),
                CompletedAt = reader.GetDateTime("completed_at")
            };
        }

        /// <summary>
        /// 影片合成完成後，寫入一筆完成紀錄，綁定給該探員與（選填的）劇本。
        /// </summary>
        public async Task CreateCompletedRecordAsync(string epId, string vlogId, string storyId, string videoUrl, string thumbnailUrl)
        {
            const string sql = @"INSERT INTO ep_vlog
                                  (ep_id, vlog_id, story_id, video_url, thumbnail_url, completed_at)
                                  VALUES
                                  (@ep_id, @vlog_id, @story_id, @video_url, @thumbnail_url, NOW())";

            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ep_id", epId);
            cmd.Parameters.AddWithValue("@vlog_id", vlogId);
            cmd.Parameters.AddWithValue("@story_id", (object)storyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@video_url", (object)videoUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@thumbnail_url", (object)thumbnailUrl ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}