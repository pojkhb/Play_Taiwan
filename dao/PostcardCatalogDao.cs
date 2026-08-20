using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MySqlConnector;
using backend.Models;
using backend.utils;

namespace backend.dao
{
    /// <summary>明信片主檔 (md_postcard) 的資料存取物件。</summary>
    public class PostcardCatalogDao
    {
        private readonly string _connectionString;

        // 使用 IOptions<AppSettings> 注入，確保與 AuthDao 的連線字串一致
        public PostcardCatalogDao(IOptions<AppSettings> appSettings)
        {
            _connectionString = appSettings.Value.mydb;
        }

        /// <summary>取得所有明信片主檔，可依系列分類篩選。</summary>
        public async Task<List<PostcardCatalog>> GetAllAsync(string category = null)
        {
            var list = new List<PostcardCatalog>();
            var sql = @"SELECT postcard_id, story_id, postcard_name, summary, image_url,
                               is_night_edition_default, category, sort_order, is_active,
                               created_at, updated_at
                        FROM md_postcard
                        WHERE is_active = 1";

            if (!string.IsNullOrWhiteSpace(category))
            {
                sql += " AND category = @category";
            }
            sql += " ORDER BY sort_order";

            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand(sql, conn);
            
            if (!string.IsNullOrWhiteSpace(category))
            {
                cmd.Parameters.AddWithValue("@category", category);
            }

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(Map(reader));
            }
            return list;
        }

        /// <summary>依明信片識別碼取得單一明信片主檔資料。</summary>
        public async Task<PostcardCatalog> GetByIdAsync(string postcardId)
        {
            const string sql = @"SELECT postcard_id, story_id, postcard_name, summary, image_url,
                                        is_night_edition_default, category, sort_order, is_active,
                                        created_at, updated_at
                                 FROM md_postcard
                                 WHERE postcard_id = @postcard_id";

            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@postcard_id", postcardId);

            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Map(reader) : null;
        }

        /// <summary>取得指定劇本所擁有的所有明信片主檔。</summary>
        public async Task<List<PostcardCatalog>> GetByStoryIdAsync(string storyId)
        {
            var list = new List<PostcardCatalog>();
            const string sql = @"SELECT postcard_id, story_id, postcard_name, summary, image_url,
                                        is_night_edition_default, category, sort_order, is_active,
                                        created_at, updated_at
                                 FROM md_postcard
                                 WHERE story_id = @story_id AND is_active = 1
                                 ORDER BY sort_order";

            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@story_id", storyId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(Map(reader));
            }
            return list;
        }

        /// <summary>新增明信片主檔。</summary>
        public async Task CreateAsync(PostcardCatalog entity)
        {
            const string sql = @"INSERT INTO md_postcard
                                  (postcard_id, story_id, postcard_name, summary, image_url,
                                   is_night_edition_default, category, sort_order, is_active)
                                  VALUES
                                  (@postcard_id, @story_id, @postcard_name, @summary, @image_url,
                                   @is_night_edition_default, @category, @sort_order, @is_active)";

            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand(sql, conn);
            AddEntityParameters(cmd, entity);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>更新明信片主檔，回傳是否有資料被更新。</summary>
        public async Task<bool> UpdateAsync(PostcardCatalog entity)
        {
            const string sql = @"UPDATE md_postcard SET
                                  story_id = @story_id,
                                  postcard_name = @postcard_name,
                                  summary = @summary,
                                  image_url = @image_url,
                                  is_night_edition_default = @is_night_edition_default,
                                  category = @category,
                                  sort_order = @sort_order,
                                  is_active = @is_active
                                  WHERE postcard_id = @postcard_id";

            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand(sql, conn);
            AddEntityParameters(cmd, entity);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        /// <summary>刪除明信片主檔，回傳是否有資料被刪除。</summary>
        public async Task<bool> DeleteAsync(string postcardId)
        {
            const string sql = "DELETE FROM md_postcard WHERE postcard_id = @postcard_id";

            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@postcard_id", postcardId);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        private static void AddEntityParameters(MySqlCommand cmd, PostcardCatalog entity)
        {
            cmd.Parameters.AddWithValue("@postcard_id", entity.PostcardId);
            cmd.Parameters.AddWithValue("@story_id", entity.StoryId);
            cmd.Parameters.AddWithValue("@postcard_name", entity.PostcardName);
            cmd.Parameters.AddWithValue("@summary", entity.Summary);
            cmd.Parameters.AddWithValue("@image_url", entity.ImageUrl);
            cmd.Parameters.AddWithValue("@is_night_edition_default", entity.IsNightEditionDefault);
            cmd.Parameters.AddWithValue("@category", entity.Category);
            cmd.Parameters.AddWithValue("@sort_order", entity.SortOrder);
            cmd.Parameters.AddWithValue("@is_active", entity.IsActive);
        }

        private static PostcardCatalog Map(MySqlDataReader reader)
        {
            return new PostcardCatalog
            {
                PostcardId = reader.GetString("postcard_id"),
                StoryId = reader.IsDBNull(reader.GetOrdinal("story_id")) ? null : reader.GetString("story_id"),
                PostcardName = reader.GetString("postcard_name"),
                Summary = reader.IsDBNull(reader.GetOrdinal("summary")) ? null : reader.GetString("summary"),
                ImageUrl = reader.IsDBNull(reader.GetOrdinal("image_url")) ? null : reader.GetString("image_url"),
                IsNightEditionDefault = reader.GetBoolean("is_night_edition_default"),
                Category = reader.IsDBNull(reader.GetOrdinal("category")) ? null : reader.GetString("category"),
                SortOrder = reader.GetInt32("sort_order"),
                IsActive = reader.GetBoolean("is_active"),
                CreatedAt = reader.GetDateTime("created_at"),
                UpdatedAt = reader.GetDateTime("updated_at")
            };
        }
    }
}