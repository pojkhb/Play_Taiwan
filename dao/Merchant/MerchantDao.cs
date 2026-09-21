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

        #region 3. 建立商家影音專案
        /// <summary>寫入後回傳資料庫自動產生的 mm_id。</summary>
        public int CreateVlogTask(int auId, GenerateVlogRequest req)
        {
            string sql = @"
                INSERT INTO merchant_media
                    (au_id, mm_title, mm_description, mm_text, mm_video_url, mm_status)
                VALUES
                    (@auId, @title, @description, @text, @videoUrl, 3);
                SELECT LAST_INSERT_ID();
            ";

            string promotionText = req.promotion_text ?? "";
            string title = promotionText.Length > 20
                ? promotionText.Substring(0, 20)
                : (string.IsNullOrWhiteSpace(promotionText) ? "商家精選影音" : promotionText);

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.ExecuteScalar<int>(sql, new
                {
                    auId,
                    title,
                    description = promotionText,
                    text = req.tone,
                    videoUrl = req.media_url
                });
            }
        }
        #endregion

        #region 4. 取得最後生成畫面
        public MerchantVlogResult GetVlogResult(int mmId, int auId)
        {
            string sql = @"
                SELECT mm_id, mm_title, mm_text, mm_video_url, mm_hashtage, mm_status
                FROM merchant_media
                WHERE mm_id = @mmId AND au_id = @auId;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                MerchantMedia row = conn.QueryFirstOrDefault<MerchantMedia>(sql, new { mmId, auId });

                if (row == null)
                {
                    throw new System.Exception("找不到此影音檔案資料");
                }

                return new MerchantVlogResult
                {
                    mm_id = row.mm_id,
                    mm_title = row.mm_title,
                    caption = row.mm_text,
                    mm_video_url = row.mm_video_url,
                    hashtags = string.IsNullOrWhiteSpace(row.mm_hashtage)
                        ? new string[0]
                        : row.mm_hashtage.Split(','),
                    mm_status = row.mm_status
                };
            }
        }
        #endregion
    }
}
