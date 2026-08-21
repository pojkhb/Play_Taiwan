using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using backend.utils;
using backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    /// <summary>
    /// 任務答題相關資料存取層。
    /// 只負責查詢/寫入資料庫，不做任何業務判斷（核對答案的邏輯在 Services/TaskVerificationService）。
    /// </summary>
    public class TaskDao
    {
        private readonly AppSettings _appSettings;
        private readonly HttpContext _ipContext;

        public TaskDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 取得任務詳情

        /// <summary>
        /// 依 task_id 查詢任務完整內容，資料來源為 md_task。
        /// </summary>
        public TaskDetailResponse GetTaskDetail(string task_id)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();

            using var cmd = new MySqlCommand(@"
                SELECT task_id, node_id, task_category, task_title, task_description,
                       options_json, correct_option_key,
                       geofence_lat, geofence_lng, geofence_radius_m, geofence_dwell_seconds,
                       vision_target_labels_json, pose_reference_json, interview_script_json,
                       count_target_answer, count_tolerance, qr_code_token, hidden_unlock_condition_json,
                       requires_photo, requires_gps, requires_group,
                       recommended_players_min, recommended_players_max,
                       difficulty_star, wrong_attempt_tolerance,
                       reward_postcard_id, reward_badge_id
                FROM md_task
                WHERE task_id = @task_id AND is_active = 1", conn);

            cmd.Parameters.AddWithValue("@task_id", task_id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new Exception("找不到指定任務");
            }

            var optionsJson = reader["options_json"] as string;
            var options = string.IsNullOrEmpty(optionsJson)
                ? new List<TaskOption>()
                : JsonSerializer.Deserialize<List<TaskOption>>(optionsJson);

            return new TaskDetailResponse
            {
                task_id = reader.GetString("task_id"),
                node_id = reader.GetString("node_id"),
                task_type = reader.GetString("task_category"), // DB 的 task_category 映射到 Model 的 task_type
                task_description = reader.GetString("task_description"),
                options = options,
                correct_option_key = reader["correct_option_key"] as string,
                geofence_lat = reader["geofence_lat"] as double?,
                geofence_lng = reader["geofence_lng"] as double?,
                geofence_radius_m = reader["geofence_radius_m"] as int?,
                geofence_dwell_seconds = reader["geofence_dwell_seconds"] as int?,
                vision_target_labels_json = reader["vision_target_labels_json"] as string,
                pose_reference_json = reader["pose_reference_json"] as string,
                interview_script_json = reader["interview_script_json"] as string,
                count_target_answer = reader["count_target_answer"] as int?,
                count_tolerance = reader["count_tolerance"] as int?,
                qr_code_token = reader["qr_code_token"] as string,
                hidden_unlock_condition_json = reader["hidden_unlock_condition_json"] as string,
                requires_photo = reader.GetBoolean("requires_photo"),
                requires_gps = reader.GetBoolean("requires_gps"),
                requires_group = reader.GetBoolean("requires_group"),
                recommended_players_min = reader.GetInt32("recommended_players_min"),
                recommended_players_max = reader.GetInt32("recommended_players_max"),
                difficulty_star = reader.GetInt32("difficulty_star"),
                wrong_attempt_tolerance = reader.GetInt32("wrong_attempt_tolerance"),
                reward_postcard_id = reader["reward_postcard_id"] as string,
                reward_badge_id = reader["reward_badge_id"] as string
            };
        }

        #endregion

        #region 取得提示

        /// <summary>
        /// 依答錯次數取得對應階段的提示，資料來源為 md_task_hint。
        /// trigger_wrong_count 越高、hint_stage 越大代表提示越明確，取符合條件中最明確的一筆。
        /// </summary>
        public TaskHintResponse GetHintByWrongCount(string task_id, int wrongCount)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();

            using var cmd = new MySqlCommand(@"
                SELECT hint_text FROM md_task_hint
                WHERE task_id = @task_id AND trigger_wrong_count <= @wrongCount AND is_active = 1
                ORDER BY hint_stage DESC
                LIMIT 1", conn);

            cmd.Parameters.AddWithValue("@task_id", task_id);
            cmd.Parameters.AddWithValue("@wrongCount", wrongCount);

            var result = cmd.ExecuteScalar();

            return new TaskHintResponse
            {
                task_id = task_id,
                hint_text = result?.ToString() ?? "還沒有達到提示解鎖的答錯次數",
                is_available = result != null
            };
        }

        #endregion

        #region 動態難度

        /// <summary>
        /// 記錄玩家在該地區的造訪次數，並依 md_difficulty_prompt 的門檻自動調整難度星等。
        /// </summary>
        public int RecordVisitAndGetDifficulty(string ep_id, string region_id)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();

            using (var cmd = new MySqlCommand(@"
                INSERT INTO ep_visit_count (ep_id, region_id, visit_count, current_difficulty_star)
                VALUES (@ep_id, @region_id, 1, 1)
                ON DUPLICATE KEY UPDATE visit_count = visit_count + 1", conn))
            {
                cmd.Parameters.AddWithValue("@ep_id", ep_id);
                cmd.Parameters.AddWithValue("@region_id", region_id);
                cmd.ExecuteNonQuery();
            }

            int visitCount;
            using (var cmd = new MySqlCommand(
                "SELECT visit_count FROM ep_visit_count WHERE ep_id=@ep_id AND region_id=@region_id", conn))
            {
                cmd.Parameters.AddWithValue("@ep_id", ep_id);
                cmd.Parameters.AddWithValue("@region_id", region_id);
                visitCount = Convert.ToInt32(cmd.ExecuteScalar());
            }

            int newStar;
            using (var cmd = new MySqlCommand(@"
                SELECT MAX(difficulty_star) FROM md_difficulty_prompt
                WHERE raise_visit_threshold <= @visitCount AND is_active = 1", conn))
            {
                cmd.Parameters.AddWithValue("@visitCount", visitCount);
                var result = cmd.ExecuteScalar();
                newStar = (result == DBNull.Value || result == null) ? 1 : Convert.ToInt32(result);
            }

            using (var cmd = new MySqlCommand(
                "UPDATE ep_visit_count SET current_difficulty_star=@star WHERE ep_id=@ep_id AND region_id=@region_id", conn))
            {
                cmd.Parameters.AddWithValue("@star", newStar);
                cmd.Parameters.AddWithValue("@ep_id", ep_id);
                cmd.Parameters.AddWithValue("@region_id", region_id);
                cmd.ExecuteNonQuery();
            }

            return newStar;
        }

        /// <summary>依難度星等取得要丟給 LLM 的固定提示字模板，資料來源為 md_difficulty_prompt。</summary>
        public string GetDifficultyPrompt(int difficultyStar)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();

            using var cmd = new MySqlCommand(
                "SELECT llm_prompt_template FROM md_difficulty_prompt WHERE difficulty_star=@star AND is_active=1", conn);
            cmd.Parameters.AddWithValue("@star", difficultyStar);

            return cmd.ExecuteScalar()?.ToString() ?? "";
        }

        #endregion

        #region 獎章抽取

        /// <summary>
        /// 完成節點時依 md_badge_pool 的權重抽取一枚徽章，並寫入 ep_badge（重複不會再插入）。
        /// </summary>
        public string DrawBadge(string ep_id, string story_id)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();

            var pool = new List<(string BadgeId, string BadgeName, int Weight)>();
            using (var cmd = new MySqlCommand(@"
                SELECT badge_id, badge_name, weight FROM md_badge_pool
                WHERE (story_id = @story_id OR story_id IS NULL) AND is_active = 1", conn))
            {
                cmd.Parameters.AddWithValue("@story_id", story_id);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    pool.Add((reader.GetString("badge_id"), reader.GetString("badge_name"), reader.GetInt32("weight")));
                }
            }

            if (pool.Count == 0) return null;

            int totalWeight = pool.Sum(p => p.Weight);
            int roll = new Random().Next(totalWeight);
            int cumulative = 0;
            var picked = pool[0];
            foreach (var item in pool)
            {
                cumulative += item.Weight;
                if (roll < cumulative) { picked = item; break; }
            }

            using (var cmd = new MySqlCommand(@"
                INSERT IGNORE INTO ep_badge (ep_id, badge_id, badge_name)
                VALUES (@ep_id, @badge_id, @badge_name)", conn))
            {
                cmd.Parameters.AddWithValue("@ep_id", ep_id);
                cmd.Parameters.AddWithValue("@badge_id", picked.BadgeId);
                cmd.Parameters.AddWithValue("@badge_name", picked.BadgeName);
                cmd.ExecuteNonQuery();
            }

            return picked.BadgeId;
        }

        #endregion

        #region 隱藏關卡

        /// <summary>
        /// 依玩家目前 GPS 座標檢查是否觸發該地區尚未解鎖的隱藏關卡，觸發後寫入 ep_hidden_level_unlock。
        /// </summary>
        public HiddenLevelTriggerResult CheckHiddenLevelTrigger(string ep_id, double lat, double lng, string region_id)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();

            using var cmd = new MySqlCommand(@"
                SELECT hl.hidden_level_id, hl.title, hl.cultural_background, hl.content,
                       hl.trigger_lat, hl.trigger_lng, hl.trigger_radius_m,
                       hl.reward_badge_id, hl.reward_postcard_id
                FROM md_hidden_level hl
                LEFT JOIN ep_hidden_level_unlock u
                       ON u.hidden_level_id = hl.hidden_level_id AND u.ep_id = @ep_id
                WHERE hl.region_id = @region_id AND hl.is_active = 1 AND u.ep_id IS NULL", conn);

            cmd.Parameters.AddWithValue("@ep_id", ep_id);
            cmd.Parameters.AddWithValue("@region_id", region_id);

            var candidates = new List<(string Id, string Title, string Culture, string Content, double Lat, double Lng, int Radius, string BadgeId, string PostcardId)>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    candidates.Add((
                        reader.GetString("hidden_level_id"),
                        reader["title"] as string,
                        reader["cultural_background"] as string,
                        reader["content"] as string,
                        Convert.ToDouble(reader["trigger_lat"]),
                        Convert.ToDouble(reader["trigger_lng"]),
                        Convert.ToInt32(reader["trigger_radius_m"]),
                        reader["reward_badge_id"] as string,
                        reader["reward_postcard_id"] as string));
                }
            }

            foreach (var c in candidates)
            {
                if (HaversineMeters(c.Lat, c.Lng, lat, lng) <= c.Radius)
                {
                    using var insertCmd = new MySqlCommand(
                        "INSERT INTO ep_hidden_level_unlock (ep_id, hidden_level_id) VALUES (@ep_id, @id)", conn);
                    insertCmd.Parameters.AddWithValue("@ep_id", ep_id);
                    insertCmd.Parameters.AddWithValue("@id", c.Id);
                    insertCmd.ExecuteNonQuery();

                    return new HiddenLevelTriggerResult
                    {
                        triggered = true,
                        hidden_level_id = c.Id,
                        title = c.Title,
                        cultural_background = c.Culture,
                        content = c.Content,
                        reward_badge_id = c.BadgeId,
                        reward_postcard_id = c.PostcardId
                    };
                }
            }

            return new HiddenLevelTriggerResult { triggered = false };
        }

        private static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
        {
            const double R = 6371000;
            double dLat = (lat2 - lat1) * Math.PI / 180;
            double dLng = (lng2 - lng1) * Math.PI / 180;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                       Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        #endregion
                #region 玩家任務答題紀錄

        /// <summary>
        /// 取得指定探員在指定任務目前已答錯的次數。
        /// 若探員尚未開始此任務，則回傳 0。
        /// </summary>
        /// <param name="epId">探員代號。</param>
        /// <param name="taskId">任務代號。</param>
        /// <returns>累積答錯次數。</returns>
        public int GetWrongCount(string epId, string taskId)
        {
            const string sql = @"
                SELECT COALESCE(wrong_count, 0)
                FROM ep_task_record
                WHERE ep_id = @ep_id
                  AND task_id = @task_id;
            ";

            using var connection = new MySqlConnection(_appSettings.mydb);
            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@ep_id", epId);
            command.Parameters.AddWithValue("@task_id", taskId);

            connection.Open();

            object result = command.ExecuteScalar();

            return result == null || result == DBNull.Value
                ? 0
                : Convert.ToInt32(result);
        }

        /// <summary>
        /// 玩家答錯時累積錯誤次數。
        /// 若首次答題則新增紀錄；若已有紀錄則 wrong_count 加一。
        /// </summary>
        /// <param name="epId">探員代號。</param>
        /// <param name="taskId">任務代號。</param>
        /// <param name="storyId">所屬劇本代號。</param>
        /// <param name="nodeId">所屬節點代號。</param>
        public void IncreaseWrongCount(
            string epId,
            string taskId,
            string storyId,
            string nodeId)
        {
            const string sql = @"
                INSERT INTO ep_task_record
                (
                    ep_id,
                    task_id,
                    story_id,
                    node_id,
                    wrong_count,
                    is_completed,
                    created_at,
                    updated_at
                )
                VALUES
                (
                    @ep_id,
                    @task_id,
                    @story_id,
                    @node_id,
                    1,
                    0,
                    NOW(),
                    NOW()
                )
                ON DUPLICATE KEY UPDATE
                    wrong_count = wrong_count + 1,
                    updated_at = NOW();
            ";

            using var connection = new MySqlConnection(_appSettings.mydb);
            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@ep_id", epId);
            command.Parameters.AddWithValue("@task_id", taskId);
            command.Parameters.AddWithValue("@story_id", string.IsNullOrWhiteSpace(storyId) ? (object)DBNull.Value : storyId);
            command.Parameters.AddWithValue("@node_id", string.IsNullOrWhiteSpace(nodeId) ? (object)DBNull.Value : nodeId);

            connection.Open();

            command.ExecuteNonQuery();
        }

        /// <summary>
        /// 玩家答對時建立或更新任務完成紀錄。
        /// 已累積的錯誤次數會保留，供後續玩家行為分析使用。
        /// </summary>
        /// <param name="epId">探員代號。</param>
        /// <param name="taskId">任務代號。</param>
        /// <param name="storyId">所屬劇本代號。</param>
        /// <param name="nodeId">所屬節點代號。</param>
        public void MarkTaskCompleted(
            string epId,
            string taskId,
            string storyId,
            string nodeId)
        {
            const string sql = @"
                INSERT INTO ep_task_record
                (
                    ep_id,
                    task_id,
                    story_id,
                    node_id,
                    wrong_count,
                    is_completed,
                    completed_at,
                    created_at,
                    updated_at
                )
                VALUES
                (
                    @ep_id,
                    @task_id,
                    @story_id,
                    @node_id,
                    0,
                    1,
                    NOW(),
                    NOW(),
                    NOW()
                )
                ON DUPLICATE KEY UPDATE
                    is_completed = 1,
                    completed_at = COALESCE(completed_at, NOW()),
                    updated_at = NOW();
            ";

            using var connection = new MySqlConnection(_appSettings.mydb);
            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@ep_id", epId);
            command.Parameters.AddWithValue("@task_id", taskId);
            command.Parameters.AddWithValue("@story_id", string.IsNullOrWhiteSpace(storyId) ? (object)DBNull.Value : storyId);
            command.Parameters.AddWithValue("@node_id", string.IsNullOrWhiteSpace(nodeId) ? (object)DBNull.Value : nodeId);

            connection.Open();

            command.ExecuteNonQuery();
        }

        #endregion
    }
}