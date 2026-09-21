// 檔案路徑：System\dao\PostcardCatalogDao.cs
// 對應新資料表 `postcard`，取代舊的 md_postcard(主檔) + ep_postcard(擁有者)。
// 兩張表已合併，au_id 直接在 postcard 上，因此不再需要額外的「綁定給使用者」動作。
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using backend.Models;
using backend.utils;

namespace backend.dao
{
    public class PostcardCatalogDao
    {
        private readonly AppSettings _appSettings;

        public PostcardCatalogDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        private const string SelectColumns = @"
            p_id, au_id, s_id, sn_id, p_name, p_summary, p_imag_url,
            is_night, created_at, updated_at
        ";

        #region 取得使用者的所有明信片
        public async Task<List<PostcardCatalog>> GetAllAsync(int auId)
        {
            string sql = $@"
                SELECT {SelectColumns}
                FROM postcard
                WHERE au_id = @auId
                ORDER BY created_at DESC;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                await conn.OpenAsync();
                return (await conn.QueryAsync<PostcardCatalog>(sql, new { auId })).ToList();
            }
        }
        #endregion

        #region 依明信片代號取得單張
        public async Task<PostcardCatalog> GetByIdAsync(int postcardId)
        {
            string sql = $@"
                SELECT {SelectColumns}
                FROM postcard
                WHERE p_id = @postcardId
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                await conn.OpenAsync();
                return await conn.QueryFirstOrDefaultAsync<PostcardCatalog>(sql, new { postcardId });
            }
        }
        #endregion

        #region 取得某劇本底下的明信片
        public async Task<List<PostcardCatalog>> GetByStoryIdAsync(int storyId, int auId)
        {
            string sql = $@"
                SELECT {SelectColumns}
                FROM postcard
                WHERE s_id = @storyId AND au_id = @auId
                ORDER BY created_at;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                await conn.OpenAsync();
                return (await conn.QueryAsync<PostcardCatalog>(sql, new { storyId, auId })).ToList();
            }
        }
        #endregion

        #region 新增明信片（AI 生成後寫入）
        /// <summary>寫入後回傳資料庫自動產生的 p_id。</summary>
        public async Task<int> CreateAsync(PostcardCatalog entity)
        {
            string sql = @"
                INSERT INTO postcard
                    (au_id, s_id, sn_id, p_name, p_summary, p_imag_url, is_night)
                VALUES
                    (@au_id, @s_id, @sn_id, @p_name, @p_summary, @p_imag_url, @is_night);
                SELECT LAST_INSERT_ID();
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                await conn.OpenAsync();
                return await conn.ExecuteScalarAsync<int>(sql, entity);
            }
        }
        #endregion

        #region 刪除明信片
        public async Task<bool> DeleteAsync(int postcardId, int auId)
        {
            string sql = @"
                DELETE FROM postcard
                WHERE p_id = @postcardId AND au_id = @auId;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                await conn.OpenAsync();
                return await conn.ExecuteAsync(sql, new { postcardId, auId }) > 0;
            }
        }
        #endregion
    }
}
