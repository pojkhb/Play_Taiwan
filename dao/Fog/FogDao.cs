// 檔案路徑：System\dao\Fog\FogDao.cs
// 對應資料表 `fog`：每張景點照片做一張迷霧圖（fog_source_key = 照片網址的 SHA-1），另外有一張通用迷霧圖（is_default = 1）
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using backend.utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class FogDao
    {
        private readonly AppSettings _appSettings;

        public FogDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        public class PlacePhoto
        {
            public string place_name { get; set; }
            public string photo_url { get; set; }
        }

        /// <summary>劇本每一站的景點（景點 ID 與名稱），沒有對應景點的站不列。照片由 FogService 依景點 ID 向 Neo4j 拿</summary>
        public async Task<List<(string place_id, string place_name)>> GetStoryPlacesAsync(int storyId)
        {
            string sql = $@"
                SELECT sn.place_id, COALESCE(MIN(p.p_name), MIN(pt.place_name), MIN(sn.sn_title)) AS place_name
                FROM story_node sn
                {MapDao.PlaceBridgeJoin}
                WHERE sn.s_id = @storyId AND sn.place_id IS NOT NULL AND sn.place_id <> ''
                GROUP BY sn.place_id;
            ";

            using var conn = new MySqlConnection(_appSettings.mydb);
            return (await conn.QueryAsync<(string place_id, string place_name)>(sql, new { storyId })).ToList();
        }

        /// <summary>這張照片做好的迷霧圖網址；還沒做過回傳 null</summary>
        public async Task<string> FindFogImageAsync(string sourceKey)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            return await conn.ExecuteScalarAsync<string>(
                "SELECT fog_image FROM fog WHERE fog_source_key = @sourceKey LIMIT 1;", new { sourceKey });
        }

        /// <summary>寫入一張照片的迷霧圖；同一張照片已經有資料時更新網址</summary>
        public async Task UpsertAsync(string name, string fogImage, string sourceImage, string sourceKey)
        {
            const string sql = @"
                INSERT INTO fog (fog_name, fog_image, fog_source_image, fog_source_key, is_default)
                VALUES (@name, @fogImage, @sourceImage, @sourceKey, 0)
                ON DUPLICATE KEY UPDATE fog_name = VALUES(fog_name), fog_image = VALUES(fog_image), fog_source_image = VALUES(fog_source_image);
            ";

            using var conn = new MySqlConnection(_appSettings.mydb);
            await conn.ExecuteAsync(sql, new { name, fogImage, sourceImage, sourceKey });
        }
    }
}
