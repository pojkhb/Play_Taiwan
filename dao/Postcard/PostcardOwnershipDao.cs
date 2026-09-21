using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MySqlConnector;
using backend.utils;

namespace backend.dao
{
    public class PostcardOwnershipDao
    {
        private readonly string _connectionString;

        public PostcardOwnershipDao(IOptions<AppSettings> appSettings)
        {
            _connectionString = appSettings.Value.mydb;
        }

        public async Task AddOwnershipAsync(string epId, string postcardId, string storyId, bool isNightEdition)
        {
            const string sql = @"
                INSERT INTO ep_postcard (ep_id, postcard_id, story_id, obtained_at, is_night_edition)
                VALUES (@ep_id, @postcard_id, @story_id, UTC_TIMESTAMP(), @is_night_edition);
            ";

            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ep_id", epId);
            cmd.Parameters.AddWithValue("@postcard_id", postcardId);
            cmd.Parameters.AddWithValue("@story_id", storyId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@is_night_edition", isNightEdition ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<bool> IsOwnedByUserAsync(string epId, string postcardId)
        {
            const string sql = @"
                SELECT COUNT(1) FROM ep_postcard
                WHERE ep_id = @ep_id AND postcard_id = @postcard_id;
            ";

            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ep_id", epId);
            cmd.Parameters.AddWithValue("@postcard_id", postcardId);

            var count = (long)await cmd.ExecuteScalarAsync();
            return count > 0;
        }

        public async Task<DateTime?> GetFirstObtainedAtAsync(string epId, string postcardId)
        {
            const string sql = @"
                SELECT MIN(obtained_at) FROM ep_postcard
                WHERE ep_id = @ep_id AND postcard_id = @postcard_id;
            ";

            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ep_id", epId);
            cmd.Parameters.AddWithValue("@postcard_id", postcardId);

            var result = await cmd.ExecuteScalarAsync();
            return result == null || result is DBNull ? (DateTime?)null : Convert.ToDateTime(result);
        }
    }
}