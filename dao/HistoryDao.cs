using System;
using System.Collections.Generic;
using backend.utils;
using backend.Models;
using backend.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class HistoryDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        private readonly HttpContext _ipContext;

        public HistoryDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 取得所有過往劇本
        public List<HistoryStoryItem> GetHistoryList(string ep_id)
        {
            string sql = @"
                SELECT
                    p.story_id,
                    s.title,
                    s.synopsis,
                    p.completed_at AS completed_date,
                    r.region_name AS region,
                    v.vlog_id
                FROM ep_story_progress p
                INNER JOIN md_story s ON p.story_id = s.story_id
                LEFT JOIN md_region r ON s.region_id = r.region_id
                LEFT JOIN ep_vlog v ON v.story_id = p.story_id AND v.ep_id = p.ep_id
                WHERE p.ep_id = @ep_id
                  AND p.progress_status = 'COMPLETED'
                ORDER BY p.completed_at DESC;
            ";

            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ep_id", ep_id);

            var result = new List<HistoryStoryItem>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new HistoryStoryItem
                {
                    story_id = reader["story_id"].ToString(),
                    title = reader["title"].ToString(),
                    synopsis = reader["synopsis"] == DBNull.Value ? null : reader["synopsis"].ToString(),
                    completed_date = reader["completed_date"] == DBNull.Value ? default : Convert.ToDateTime(reader["completed_date"]),
                    region = reader["region"] == DBNull.Value ? null : reader["region"].ToString(),
                    vlog_id = reader["vlog_id"] == DBNull.Value ? null : reader["vlog_id"].ToString()
                });
            }
            return result;
        }
        #endregion

        #region 取得過往劇本詳情 (包含景點清單)
        public HistoryStoryItem GetHistoryDetail(string story_id, string ep_id)
        {
            string sql = @"
                SELECT
                    p.story_id,
                    s.title,
                    s.synopsis,
                    p.completed_at AS completed_date,
                    r.region_name AS region,
                    v.vlog_id
                FROM ep_story_progress p
                INNER JOIN md_story s ON p.story_id = s.story_id
                LEFT JOIN md_region r ON s.region_id = r.region_id
                LEFT JOIN ep_vlog v ON v.story_id = p.story_id AND v.ep_id = p.ep_id
                WHERE p.story_id = @story_id
                  AND p.ep_id = @ep_id;
            ";

            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@story_id", story_id);
            cmd.Parameters.AddWithValue("@ep_id", ep_id);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new Exception("找不到此過往劇本：" + story_id);
            }

            var resultItem = new HistoryStoryItem
            {
                story_id = reader["story_id"].ToString(),
                title = reader["title"].ToString(),
                synopsis = reader["synopsis"] == DBNull.Value ? null : reader["synopsis"].ToString(),
                completed_date = reader["completed_date"] == DBNull.Value ? default : Convert.ToDateTime(reader["completed_date"]),
                region = reader["region"] == DBNull.Value ? null : reader["region"].ToString(),
                vlog_id = reader["vlog_id"] == DBNull.Value ? null : reader["vlog_id"].ToString(),
                spots = new List<string>() // 👈 實例化景點清單
            };
            
            reader.Close(); // 🌟 關閉目前的 reader，準備查詢景點

            // 🌟 第二段查詢：去節點表把對應劇本的景點名稱都撈出來
            string spotSql = @"
                SELECT COALESCE(p.place_name, n.node_title) AS place_name
                FROM md_story_node n
                LEFT JOIN md_place p ON n.place_id = p.place_id
                WHERE n.story_id = @story_id
                ORDER BY n.node_order;
            ";
            
            using var spotCmd = new MySqlCommand(spotSql, conn);
            spotCmd.Parameters.AddWithValue("@story_id", story_id);
            using var spotReader = spotCmd.ExecuteReader();
            
            while (spotReader.Read())
            {
                resultItem.spots.Add(spotReader["place_name"].ToString());
            }

            return resultItem;
        }
        #endregion
    }
}