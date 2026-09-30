// 檔案路徑：System\dao\Merchant\MerchantDao.cs
// 對應新資料表 `store`（店家名稱）與 `merchant_media`（商家影音），
// 取代舊的 ep_account.store_name / ep_vlog。
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using backend.Models;
using backend.utils;
using backend.ViewModels;

namespace backend.dao
{
    public class MerchantDao
    {
        private readonly AppSettings _appSettings;

        public MerchantDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        #region 1. 修改店家名稱
        /// <summary>store.au_id 有唯一索引，一個帳號只會有一間店。</summary>
        public int UpdateStoreName(int auId, string storeName)
        {
            string sql = @"
                UPDATE store
                SET store_name = @storeName
                WHERE au_id = @auId;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.Execute(sql, new { auId, storeName = storeName ?? "" });
            }
        }
        #endregion

        #region 2. 已經生成的影音檔案（依最後更新時間新到舊）
        public List<MerchantFileItem> GetMerchantFiles(int auId)
        {
            string sql = @"
                SELECT mm_id, mm_title, mm_video_url, mm_status, updated_at
                FROM merchant_media
                WHERE au_id = @auId
                ORDER BY updated_at DESC;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.Query<MerchantFileItem>(sql, new { auId }).ToList();
            }
        }
        #endregion
    }
}
