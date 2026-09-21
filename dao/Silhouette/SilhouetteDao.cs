// 檔案路徑：System\dao\Silhouette\SilhouetteDao.cs
// 對應新資料表 `silhouette`，取代舊的 md_silhouette。
using System.Collections.Generic;
using System.Linq;
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
    }
}
