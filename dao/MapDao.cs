using System;
using System.Collections.Generic;
using backend.Models;
using backend.utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class MapDao
    {
        private readonly AppSettings _appSettings;

        public MapDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        #region 取得地圖節點

        /// <summary>
        /// 取得指定劇本的全部地圖節點。
        /// 節點解鎖狀態由 Service 依玩家 current_node_order 統一計算。
        /// day_index 由資料庫回傳，前端負責切換第一日／第二日。
        /// </summary>
        /// <param name="storyId">劇本代號。</param>
        /// <returns>劇本地圖節點清單。</returns>
        public List<MapNode> GetStoryNodes(string storyId)
        {
            const string sql = @"
        SELECT
            n.node_id,
            n.node_order,
            n.day_index,
            n.fog_hint,
            n.is_night_only,
            COALESCE(p.place_name, n.node_title) AS location_name,
            p.latitude,
            p.longitude,
            p.image_url
        FROM md_story_node n
        LEFT JOIN md_place p
            ON n.place_id = p.place_id
        WHERE n.story_id = @story_id
          AND n.is_active = 1
          AND (p.is_active = 1 OR p.place_id IS NULL)
        ORDER BY n.day_index, n.node_order;
    ";

            var result = new List<MapNode>();

            using var connection = new MySqlConnection(_appSettings.mydb);
            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@story_id", storyId);

            connection.Open();

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                result.Add(new MapNode
                {
                    node_id = reader["node_id"].ToString(),
                    node_order = Convert.ToInt32(reader["node_order"]),
                    day_index = Convert.ToInt32(reader["day_index"]), // 節點所屬天數

                    location_name = reader["location_name"] == DBNull.Value
                        ? ""
                        : reader["location_name"].ToString(),

                    lat = reader["latitude"] == DBNull.Value
                        ? 0
                        : Convert.ToDouble(reader["latitude"]),

                    lng = reader["longitude"] == DBNull.Value
                        ? 0
                        : Convert.ToDouble(reader["longitude"]),

                    is_unlocked = false, // 由 MapService.GetMap 依玩家進度覆寫
                    is_night_only = Convert.ToBoolean(reader["is_night_only"]),

                    fog_hint = reader["fog_hint"] == DBNull.Value
                        ? "前方仍被迷霧籠罩，完成前一站任務後即可探索。"
                        : reader["fog_hint"].ToString(),

                    image_url = reader["image_url"] == DBNull.Value
                        ? null
                        : reader["image_url"].ToString(),

                    silhouette_image_url = null, // 後續可串 md_silhouette
                    child_node_ids = new List<string>()
                });
            }

            return result;
        }

        #endregion

        #region 取得玩家目前節點進度

        public int GetCurrentNodeOrder(
            string epId,
            string storyId)
        {
            /*
             * 假設 ep_story_progress 內有：
             * ep_id, story_id, current_node_order
             *
             * 若欄位不同，之後只改這個 SQL。
             */
            const string sql = @"
                SELECT COALESCE(MAX(current_node_order), 0)
                FROM ep_story_progress
                WHERE ep_id = @ep_id
                  AND story_id = @story_id;
            ";

            using var connection = new MySqlConnection(_appSettings.mydb);
            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@ep_id", epId);
            command.Parameters.AddWithValue("@story_id", storyId);

            connection.Open();

            object value = command.ExecuteScalar();

            return value == null || value == DBNull.Value
                ? 0
                : Convert.ToInt32(value);
        }

        #endregion

        #region 取得節點座標

        public MapNode GetNodeLocation(string nodeId)
        {
            const string sql = @"
    SELECT
        n.node_id,
        n.node_order,
        n.day_index,
        n.is_night_only,
        COALESCE(p.place_name, n.node_title) AS location_name,
        p.latitude,
        p.longitude
    FROM md_story_node n
    LEFT JOIN md_place p
        ON n.place_id = p.place_id
    WHERE n.node_id = @node_id
      AND n.is_active = 1
      AND (p.is_active = 1 OR p.place_id IS NULL)
    LIMIT 1;
";

            using var connection = new MySqlConnection(_appSettings.mydb);
            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@node_id", nodeId);

            connection.Open();

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return null;
            }

            return new MapNode
            {
                node_id = reader["node_id"].ToString(),
                node_order = Convert.ToInt32(reader["node_order"]),
                day_index = Convert.ToInt32(reader["day_index"]),
                is_night_only = Convert.ToBoolean(reader["is_night_only"]),

                location_name = reader["location_name"] == DBNull.Value
                    ? ""
                    : reader["location_name"].ToString(),

                lat = reader["latitude"] == DBNull.Value
                    ? 0
                    : Convert.ToDouble(reader["latitude"]),

                lng = reader["longitude"] == DBNull.Value
                    ? 0
                    : Convert.ToDouble(reader["longitude"]),

                child_node_ids = new List<string>()
            };
        }

        #endregion

        #region 更新玩家節點進度

        public void UnlockNode(string epId, string nodeId)
        {
            /*
             * 假設 ep_story_progress 有：
             * ep_id, story_id, current_node_order, updated_at
             *
             * 注意：ep_id + story_id 需要是 UNIQUE KEY，
             * ON DUPLICATE KEY UPDATE 才會生效。
             */
            const string sql = @"
                INSERT INTO ep_story_progress
                (
                    ep_id,
                    story_id,
                    current_node_order,
                    updated_at
                )
                SELECT
                    @ep_id,
                    n.story_id,
                    n.node_order,
                    NOW()
                FROM md_story_node n
                WHERE n.node_id = @node_id
                  AND n.is_active = 1
                ON DUPLICATE KEY UPDATE
                    current_node_order = GREATEST(
                        current_node_order,
                        VALUES(current_node_order)
                    ),
                    updated_at = NOW();
            ";

            using var connection = new MySqlConnection(_appSettings.mydb);
            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@ep_id", epId);
            command.Parameters.AddWithValue("@node_id", nodeId);

            connection.Open();
            command.ExecuteNonQuery();
        }

        #endregion

        #region 取得節點詳情

        /// <summary>
        /// 取得指定節點的景點、NPC 劇情與對應任務資訊。
        /// </summary>
        /// <param name="nodeId">節點代號。</param>
        /// <returns>節點詳細資料，包含 task_id，前端可接著導向答題頁。</returns>
        public NodeDetailResponse GetNodeDetail(string nodeId)
        {
            const string sql = @"
        SELECT
            n.node_id,
            COALESCE(p.place_name, n.node_title) AS location_name,
            p.summary,
            p.introduction,
            p.open_time,
            t.task_id
        FROM md_story_node n
        LEFT JOIN md_place p
            ON n.place_id = p.place_id
        LEFT JOIN md_task t
            ON t.node_id = n.node_id
            AND t.is_active = 1
        WHERE n.node_id = @node_id
          AND n.is_active = 1
          AND (p.is_active = 1 OR p.place_id IS NULL)
        ORDER BY t.difficulty_star, t.created_at
        LIMIT 1;
    ";

            using var connection = new MySqlConnection(_appSettings.mydb);
            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@node_id", nodeId);

            connection.Open();

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return null;
            }

            string introStory = reader["introduction"] == DBNull.Value
                ? null
                : reader["introduction"].ToString();

            if (string.IsNullOrWhiteSpace(introStory))
            {
                introStory = reader["summary"] == DBNull.Value
                    ? "這個地點正等待你來探索。"
                    : reader["summary"].ToString();
            }

            return new NodeDetailResponse
            {
                node_id = reader["node_id"].ToString(),

                location_name = reader["location_name"] == DBNull.Value
                    ? ""
                    : reader["location_name"].ToString(),

                npc_name = "旅遊引導員",

                intro_story = introStory,

                opening_hours = reader["open_time"] == DBNull.Value
                    ? null
                    : reader["open_time"].ToString(),

                nearby_food = new List<string>(),

                task_id = reader["task_id"] == DBNull.Value
                    ? null
                    : reader["task_id"].ToString(),

                review_story_url = null
            };
        }

        #endregion

        #region 取得 NPC 隨機互動

        public NpcInteractionResponse GetRandomNpcInteraction(
            string nodeId)
        {
            /*
             * 第一版先由景點資料產出 NPC 畫面，
             * 尚未使用 md_npc。
             * 等 md_npc 欄位提供後，再改成 JOIN md_npc。
             */
            const string sql = @"
                SELECT
                    n.node_id,
                    COALESCE(p.place_name, n.node_title) AS location_name,
                    p.summary,
                    p.image_url
                FROM md_story_node n
                LEFT JOIN md_place p
                    ON n.place_id = p.place_id
                WHERE n.node_id = @node_id
                  AND n.is_active = 1
                  AND (p.is_active = 1 OR p.place_id IS NULL)
                LIMIT 1;
            ";

            using var connection = new MySqlConnection(_appSettings.mydb);
            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@node_id", nodeId);

            connection.Open();

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return null;
            }

            return new NpcInteractionResponse
            {
                node_id = reader["node_id"].ToString(),

                location_name = reader["location_name"] == DBNull.Value
                    ? ""
                    : reader["location_name"].ToString(),

                location_subtitle = reader["summary"] == DBNull.Value
                    ? null
                    : reader["summary"].ToString(),

                scene_image_url = reader["image_url"] == DBNull.Value
                    ? null
                    : reader["image_url"].ToString(),

                npc_id = "NPC-DEFAULT",
                npc_name = "旅遊引導員",
                npc_avatar_url = null,

                npc_dialogue = "這裡似乎藏著一段等待你發現的故事。",

                emotion = "normal",
                skip_button_text = "稍後再說",
                next_task_id = null
            };
        }

        #endregion

        #region 取得明信片統計

        /// <summary>
        /// 取得玩家在指定劇本已收集的明信片數量。
        /// </summary>
        public int GetUnlockedPostcardCount(string epId, string storyId)
        {
            const string sql = @"
        SELECT COUNT(*)
        FROM ep_postcard
        WHERE ep_id = @ep_id
          AND story_id = @story_id;
    ";

            using var connection = new MySqlConnection(_appSettings.mydb);
            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@ep_id", epId);
            command.Parameters.AddWithValue("@story_id", storyId);

            connection.Open();

            return Convert.ToInt32(command.ExecuteScalar());
        }

        /// <summary>
        /// 取得指定劇本可收集的明信片總數。
        /// </summary>
        public int GetTotalPostcardCount(string storyId)
        {
            const string sql = @"
        SELECT COUNT(*)
        FROM md_postcard
        WHERE story_id = @story_id
          AND is_active = 1;
    ";

            using var connection = new MySqlConnection(_appSettings.mydb);
            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@story_id", storyId);

            connection.Open();

            return Convert.ToInt32(command.ExecuteScalar());
        }

        #endregion

        #region 取得周邊好去

        public List<NearbyPlaceResponse> GetNearbyPlaces(
            string storyId,
            string category)
        {
            const string sql = @"
                SELECT
                    p.place_id,
                    p.category,
                    p.place_name,
                    p.address,
                    p.open_time,
                    p.image_url,
                    p.latitude,
                    p.longitude
                FROM md_place p
                INNER JOIN md_story s
                    ON s.region_id = p.region_id
                WHERE s.story_id = @story_id
                  AND s.is_active = 1
                  AND p.is_active = 1
                  AND (
                      @category = ''
                      OR p.category = @category
                  )
                ORDER BY p.place_name;
            ";

            var result = new List<NearbyPlaceResponse>();

            using var connection = new MySqlConnection(_appSettings.mydb);
            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@story_id", storyId);
            command.Parameters.AddWithValue("@category", category ?? "");

            connection.Open();

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                double latitude = reader["latitude"] == DBNull.Value
                    ? 0
                    : Convert.ToDouble(reader["latitude"]);

                double longitude = reader["longitude"] == DBNull.Value
                    ? 0
                    : Convert.ToDouble(reader["longitude"]);

                string imageUrl = reader["image_url"] == DBNull.Value
                    ? null
                    : reader["image_url"].ToString();

                result.Add(new NearbyPlaceResponse
                {
                    place_id = reader["place_id"].ToString(),

                    category = reader["category"] == DBNull.Value
                        ? null
                        : reader["category"].ToString(),

                    name = reader["place_name"].ToString(),

                    address = reader["address"] == DBNull.Value
                        ? null
                        : reader["address"].ToString(),

                    open_time = reader["open_time"] == DBNull.Value
                        ? null
                        : reader["open_time"].ToString(),

                    photo_urls = string.IsNullOrWhiteSpace(imageUrl)
                        ? new List<string>()
                        : new List<string> { imageUrl },

                    maps_deeplink_url = latitude == 0 || longitude == 0
                        ? null
                        : $"https://www.google.com/maps/search/?api=1&query={latitude},{longitude}"
                });
            }

            return result;
        }

        #endregion
    }
}