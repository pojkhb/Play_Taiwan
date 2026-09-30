// 檔案路徑：System\dao\Silhouette\SilhouetteDao.cs
// 對應新資料表 `silhouette`，取代舊的 md_silhouette。
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using backend.Models;
using backend.utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class SilhouetteDao
    {
        private readonly AppSettings _appSettings;

        public SilhouetteDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        // 新表沒有 is_active / sort_order 欄位，改以名稱排序。
        private const string SelectColumns = @"
            si_id,
            si_name,
            si_type,
            si_silhouette_image,
            si_image_url,
            si_hint
        ";

        #region 取得全部剪影
        public List<Silhouette> GetSilhouettes()
        {
            string sql = $@"
                SELECT {SelectColumns}
                FROM silhouette
                ORDER BY si_type, si_name;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.Query<Silhouette>(sql).ToList();
            }
        }
        #endregion

        #region 依代號取得單一剪影
        public Silhouette GetSilhouetteById(int siId)
        {
            string sql = $@"
                SELECT {SelectColumns}
                FROM silhouette
                WHERE si_id = @siId
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.QueryFirstOrDefault<Silhouette>(sql, new { siId });
            }
        }
        #endregion

        #region 劇本節點的剪影（依景點照片產生，同一張照片的剪影跨劇本重用）

        private MySqlConnection Open() => new MySqlConnection(_appSettings.mydb);

        public class NodePhotoRow
        {
            public int sn_id { get; set; }
            public int sn_order { get; set; }

            /// <summary>景點 Neo4j uid（story_node.place_id）</summary>
            public string place_id { get; set; }

            public string place_name { get; set; }
            public string location_codename { get; set; }

            /// <summary>景點照片（place.p_image），就是地圖解鎖後顯示的那張</summary>
            public string photo_url { get; set; }

            /// <summary>已經連結的剪影；還沒有時為 null</summary>
            public int? si_id { get; set; }
            public string silhouette_image { get; set; }
        }

        /// <summary>
        /// 劇本每個節點的景點照片與目前連結的剪影。
        /// 景點名稱與照片的橋接方式跟 MapDao 相同（story_node → place_type → place），確保剪影跟解鎖後的照片是同一張。
        /// </summary>
        public async Task<List<NodePhotoRow>> GetStoryNodePhotosAsync(int storyId)
        {
            using var conn = Open();
            return (await conn.QueryAsync<NodePhotoRow>(@"
                SELECT sn.sn_id, sn.sn_order, sn.place_id, pt.place_name, sn.location_codename,
                       NULLIF(p.p_image, '') AS photo_url,
                       si.si_id, si.si_silhouette_image AS silhouette_image
                FROM story_node sn
                LEFT JOIN (SELECT place_id, MIN(place_name) AS place_name FROM place_type GROUP BY place_id) pt
                       ON pt.place_id = sn.place_id
                LEFT JOIN place p
                       ON p.p_id = (SELECT MIN(p2.p_id) FROM place p2 WHERE p2.p_name = pt.place_name)
                LEFT JOIN silhouette si
                       ON si.si_id = (SELECT sns.si_id FROM story_node_silhouette sns
                                      WHERE sns.sn_id = sn.sn_id ORDER BY sns.sns_order, sns.sns_id LIMIT 1)
                WHERE sn.s_id = @storyId
                ORDER BY sn.sn_order, sn.sn_id;", new { storyId })).ToList();
        }

        /// <summary>用來源照片網址找已經做過的剪影</summary>
        public async Task<Silhouette> FindBySourcePhotoAsync(string photoUrl)
        {
            using var conn = Open();
            return await conn.QueryFirstOrDefaultAsync<Silhouette>($@"
                SELECT {SelectColumns} FROM silhouette
                WHERE si_image_url = @photoUrl
                ORDER BY si_id LIMIT 1;", new { photoUrl });
        }

        /// <summary>新增剪影，回傳 si_id</summary>
        public async Task<int> InsertAsync(string name, string silhouetteImage, string photoUrl, string hint)
        {
            using var conn = Open();
            return await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO silhouette (si_name, si_type, si_silhouette_image, si_image_url, si_hint)
                VALUES (@name, '景點', @silhouetteImage, @photoUrl, @hint);
                SELECT LAST_INSERT_ID();", new { name, silhouetteImage, photoUrl, hint });
        }

        public async Task UpdateImageAsync(int siId, string silhouetteImage)
        {
            using var conn = Open();
            await conn.ExecuteAsync(
                "UPDATE silhouette SET si_silhouette_image = @silhouetteImage WHERE si_id = @siId;", new { siId, silhouetteImage });
        }

        /// <summary>把剪影連到節點；點擊時抓該景點資料。已經連過就略過（uk_sns_node_silhouette）</summary>
        public async Task LinkAsync(int snId, int siId, string placeId, string hint)
        {
            using var conn = Open();
            await conn.ExecuteAsync(@"
                INSERT IGNORE INTO story_node_silhouette (sn_id, si_id, sns_order, target_type, target_id, is_clickable, hint_override)
                VALUES (@snId, @siId, 1, 'place', @placeId, 1, @hint);", new { snId, siId, placeId, hint });
        }

        #endregion
    }
}
