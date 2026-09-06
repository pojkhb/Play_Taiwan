using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
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

        // 1. 修改店家名稱 (更新 ep_account 中的 store_name 欄位)
        public void UpdateStoreName(string epId, string storeName)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();
            string sql = "UPDATE ep_account SET store_name = @store_name, updated_at = NOW() WHERE ep_id = @ep_id;";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@store_name", storeName ?? "");
            cmd.Parameters.AddWithValue("@ep_id", epId);
            cmd.ExecuteNonQuery();
        }

        // 2. 已經生成檔案 (依照編輯時間 updated_at 新到舊排序)
        public List<Dictionary<string, object>> GetMerchantFiles(string epId)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();
            string sql = @"
                SELECT 
                    vlog_id, 
                    title, 
                    video_url, 
                    updated_at 
                FROM ep_vlog 
                WHERE ep_id = @ep_id 
                ORDER BY updated_at DESC;
            ";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ep_id", epId);

            var list = new List<Dictionary<string, object>>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Dictionary<string, object>
                {
                    { "vlog_id", reader["vlog_id"].ToString() },
                    { "title", reader["title"] == DBNull.Value ? "" : reader["title"].ToString() },
                    { "video_url", reader["video_url"] == DBNull.Value ? "" : reader["video_url"].ToString() },
                    { "updated_at", reader["updated_at"] == DBNull.Value ? "" : Convert.ToDateTime(reader["updated_at"]).ToString("yyyy-MM-dd HH:mm") }
                });
            }
            return list;
        }

        // 3. 商家生成影音 (建立生成任務)
        public string CreateVlogTask(string epId, GenerateVlogRequest req)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();
            string vlogId = "VLOG_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();

            string sql = @"
                INSERT INTO ep_vlog (vlog_id, ep_id, title, description, video_url, status, created_at, updated_at)
                VALUES (@vlog_id, @ep_id, @title, @description, @media_url, 'COMPLETED', NOW(), NOW());
            ";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@vlog_id", vlogId);
            cmd.Parameters.AddWithValue("@ep_id", epId);
            cmd.Parameters.AddWithValue("@title", req.promotion_text?.Length > 20 ? req.promotion_text.Substring(0, 20) : (req.promotion_text ?? "商家精選影音"));
            cmd.Parameters.AddWithValue("@description", req.promotion_text ?? "");
            cmd.Parameters.AddWithValue("@media_url", req.media_url ?? "https://example.com/default_reels.mp4");
            cmd.ExecuteNonQuery();

            return vlogId;
        }

        // 4. 取得最後生成畫面 (Reels 影音、推薦配文、標籤)
        public Dictionary<string, object> GetVlogResult(string vlogId)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();
            string sql = @"
                SELECT 
                    vlog_id, 
                    title, 
                    description, 
                    video_url, 
                    hashtags, 
                    status 
                FROM ep_vlog 
                WHERE vlog_id = @vlog_id;
            ";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@vlog_id", vlogId);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new Exception("找不到此影音檔案資料");
            }

            return new Dictionary<string, object>
            {
                { "vlog_id", reader["vlog_id"].ToString() },
                { "title", reader["title"].ToString() },
                { "caption", reader["description"] == DBNull.Value ? "" : reader["description"].ToString() },
                { "video_url", reader["video_url"] == DBNull.Value ? "" : reader["video_url"].ToString() },
                { "hashtags", reader["hashtags"] == DBNull.Value ? new string[] { "#居酒屋推薦", "#巷弄美食" } : reader["hashtags"].ToString().Split(',') },
                { "status", reader["status"].ToString() }
            };
        }
    }
}