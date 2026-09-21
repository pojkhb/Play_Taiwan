// 檔案路徑：System\dao\BadgeDao.cs
// 對應新資料表 `badge` + `au_badge`，取代舊的 md_badge / ep_badge。
using System.Collections.Generic;
using System.Linq;
using Dapper;
using backend.utils;
using backend.Models;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class BadgeDao
    {
        private readonly AppSettings _appSettings;

        public BadgeDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        #region 取得徽章圖鑑狀態（全部勳章 + 該使用者是否已解鎖）
        public List<BadgeResponse> GetAllBadgeStatus(int auId)
        {
            string sql = @"
                SELECT
                    b.b_id,
                    b.b_name,
                    b.b_fication,
                    b.b_thing,
                    b.b_image,
                    IF(ab.ab_id IS NOT NULL, TRUE, FALSE) AS is_owned,
                    ab.created_at AS obtained_at
                FROM badge b
                LEFT JOIN au_badge ab ON ab.b_id = b.b_id AND ab.au_id = @auId
                ORDER BY b.b_fication, b.b_id;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.Query<BadgeResponse>(sql, new { auId }).ToList();
            }
        }
        #endregion
    }
}
