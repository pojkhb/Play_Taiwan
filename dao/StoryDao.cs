using System;
using System.Collections.Generic;
using System.Text.Json;
using backend.Models;
using backend.utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using System.Linq;

namespace backend.dao
{
    public class StoryDao
    {
        private readonly AppSettings _appSettings;

        public StoryDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        #region 來點遊意思：轉盤地區

        public StoryWheelSpinResponse WheelSpin()
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();

            string sql = @"
                SELECT
                    region_id,
                    region_name,
                    city_name,
                    district_name
                FROM md_region
                WHERE is_active = 1
                  AND is_wheel_enabled = 1
                ORDER BY RAND()
                LIMIT 1;
            ";

            using var command = new MySqlCommand(sql, connection);
            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                throw new Exception("目前沒有可用的轉盤地區資料。");
            }

            return new StoryWheelSpinResponse
            {
                region_id = reader["region_id"].ToString(),
                region = reader["region_name"].ToString(),
                city_name = reader["city_name"] == DBNull.Value ? null : reader["city_name"].ToString(),
                district_name = reader["district_name"] == DBNull.Value ? null : reader["district_name"].ToString()
            };
        }

        #endregion

        #region 現在揪出發：取得地區清單

        public List<StoryWheelSpinResponse> GetRegions(string mode, string cityName)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();

            string sql = @"
                SELECT
                    region_id,
                    region_name,
                    city_name,
                    district_name
                FROM md_region
                WHERE is_active = 1
                  AND (
                        @mode <> 'NOW'
                        OR is_now_departure = 1
                  )
                  AND (
                        @city_name = ''
                        OR REPLACE(city_name, '臺', '台') = REPLACE(@city_name, '臺', '台')
                  )
                ORDER BY sort_order;
            ";

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@mode", mode ?? "");
            command.Parameters.AddWithValue("@city_name", cityName ?? "");

            using var reader = command.ExecuteReader();
            var result = new List<StoryWheelSpinResponse>();

            while (reader.Read())
            {
                result.Add(new StoryWheelSpinResponse
                {
                    region_id = reader["region_id"].ToString(),
                    region = reader["region_name"].ToString(),
                    city_name = reader["city_name"] == DBNull.Value ? null : reader["city_name"].ToString(),
                    district_name = reader["district_name"] == DBNull.Value ? null : reader["district_name"].ToString()
                });
            }

            return result;
        }

        #endregion

        #region 劇本檔案館：依地區與偏好生成劇本選項清單

        public List<StoryOptionResponse> GenerateStories(StoryGenerateRequest req)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();

            // 💡 修正處 1：拿掉舊的 region_id 判斷，直接用字串模糊比對 region_name
            string sql = @"
                SELECT
                    s.story_id,
                    s.title,
                    s.prologue,
                    s.category,
                    s.transport,
                    s.expected_badges_json,
                    s.expected_postcards,
                    s.region_id,
                    r.region_name
                FROM md_story s
                LEFT JOIN md_region r
                    ON s.region_id = r.region_id
                WHERE s.is_active = 1
                  AND (
                        @region = ''
                        OR r.region_name LIKE CONCAT('%', @region, '%')
                  )
                ORDER BY s.sort_order, s.created_at;
            ";

            using var command = new MySqlCommand(sql, connection);
            
            // 💡 修正處 2：把城市和鄉鎮組合起來搜尋 (例如 "臺南市中西區")
            string searchRegion = $"{req?.city_name}{req?.town_name}".Trim();
            command.Parameters.AddWithValue("@region", searchRegion);

            var stories = new List<StoryOptionResponse>();

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string expectedBadgesJson = reader["expected_badges_json"] == DBNull.Value
                        ? "[]"
                        : reader["expected_badges_json"].ToString();

                    List<string> expectedBadges;
                    try
                    {
                        expectedBadges = JsonSerializer.Deserialize<List<string>>(expectedBadgesJson) ?? new List<string>();
                    }
                    catch
                    {
                        expectedBadges = new List<string>();
                    }

                    stories.Add(new StoryOptionResponse
                    {
                        story_id = reader["story_id"].ToString(),
                        title = reader["title"].ToString(),
                        prologue = reader["prologue"] == DBNull.Value ? null : reader["prologue"].ToString(),
                        category = reader["category"] == DBNull.Value ? null : reader["category"].ToString(),
                        transport = reader["transport"] == DBNull.Value ? null : reader["transport"].ToString(),
                        expected_badges = expectedBadges,
                        expected_postcards = Convert.ToInt32(reader["expected_postcards"]),
                        region_id = reader["region_id"] == DBNull.Value ? null : reader["region_id"].ToString(),
                        region = reader["region_name"] == DBNull.Value ? null : reader["region_name"].ToString()
                    });
                }
            }

            if (stories.Count == 0) return stories;

            foreach (var story in stories)
            {
                story.route_preview = GetRoutePreview(story.story_id);
            }

            if (req.preferences == null || req.preferences.Count == 0)
            {
                return stories;
            }

            var scored = new List<(StoryOptionResponse Story, int Score)>();
            foreach (var story in stories)
            {
                int score = 0;
                using var cmd = new MySqlCommand(
                    "SELECT tag FROM md_story_tag WHERE story_id = @story_id", connection);
                cmd.Parameters.AddWithValue("@story_id", story.story_id);
                using var tagReader = cmd.ExecuteReader();
                while (tagReader.Read())
                {
                    if (req.preferences.Contains(tagReader.GetString("tag")))
                    {
                        score++;
                    }
                }
                scored.Add((story, score));
            }

            return scored.OrderByDescending(s => s.Score).Select(s => s.Story).ToList();
        }

        #endregion

        #region 劇本詳情

        public StoryDetailResponse GetDetail(string storyId)
        {
            if (string.IsNullOrWhiteSpace(storyId))
            {
                throw new Exception("story_id 不可為空白。");
            }

            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();

            string storySql = @"
                SELECT
                    story_id,
                    title,
                    subtitle,
                    prologue,
                    synopsis
                FROM md_story
                WHERE story_id = @story_id
                  AND is_active = 1;
            ";

            using var storyCommand = new MySqlCommand(storySql, connection);
            storyCommand.Parameters.AddWithValue("@story_id", storyId);

            using var storyReader = storyCommand.ExecuteReader();

            if (!storyReader.Read())
            {
                throw new Exception("找不到此劇本：" + storyId);
            }

            string prologue = storyReader["prologue"] == DBNull.Value ? "" : storyReader["prologue"].ToString();
            string synopsis = storyReader["synopsis"] == DBNull.Value ? "" : storyReader["synopsis"].ToString();

            var result = new StoryDetailResponse
            {
                story_id = storyReader["story_id"].ToString(),
                title = storyReader["title"].ToString(),
                subtitle = storyReader["subtitle"] == DBNull.Value ? "" : storyReader["subtitle"].ToString(),
                preface = string.IsNullOrEmpty(prologue) ? synopsis : prologue,
                synopsis = synopsis,
                nodes = new List<NodeDetail>(),
                route_nodes = new List<StoryOptionResponse.RouteNode>()
            };
            storyReader.Close();

            string npcSql = @"
                SELECT npc_name, npc_title 
                FROM md_npc 
                ORDER BY created_at DESC 
                LIMIT 1;
            ";
            using var npcCmd = new MySqlCommand(npcSql, connection);
            using var npcReader = npcCmd.ExecuteReader();
            if (npcReader.Read())
            {
                result.npc = new NpcDetail
                {
                    name = npcReader["npc_name"].ToString(),
                    role = npcReader["npc_title"].ToString()
                };
            }
            npcReader.Close();

            string nodeSql = @"
                SELECT
                    node_id,
                    node_order,
                    node_title,
                    fog_hint
                FROM md_story_node
                WHERE story_id = @story_id
                  AND is_active = 1
                ORDER BY node_order;
            ";

            using var nodeCommand = new MySqlCommand(nodeSql, connection);
            nodeCommand.Parameters.AddWithValue("@story_id", storyId);
            using var nodeReader = nodeCommand.ExecuteReader();

            while (nodeReader.Read())
            {
                int order = Convert.ToInt32(nodeReader["node_order"]);
                string placeName = nodeReader["node_title"].ToString();
                string taskDesc = nodeReader["fog_hint"] == DBNull.Value ? "" : nodeReader["fog_hint"].ToString();

                result.nodes.Add(new NodeDetail
                {
                    order = order,
                    place_name = placeName,
                    task_description = taskDesc,
                    opening_dialogue = ""
                });

                result.route_nodes.Add(
                    new StoryOptionResponse.RouteNode
                    {
                        node_id = nodeReader["node_id"].ToString(),
                        location_name = placeName,
                        node_order = order
                    }
                );
            }

            return result;
        }

        #endregion

        #region 劇本卡片路線預覽

        private List<string> GetRoutePreview(string storyId)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();

            string sql = @"
                SELECT
                    COALESCE(p.place_name, n.node_title) AS location_name
                FROM md_story_node n
                LEFT JOIN md_place p
                    ON n.place_id = p.place_id
                WHERE n.story_id = @story_id
                  AND n.is_active = 1
                ORDER BY n.node_order;
            ";

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@story_id", storyId);
            using var reader = command.ExecuteReader();
            var result = new List<string>();

            while (reader.Read())
            {
                result.Add(reader["location_name"].ToString());
            }

            return result;
        }

        #endregion

        #region AI 動態劇本寫入資料庫 

        public List<Dictionary<string, string>> SaveAiGeneratedStories(string ep_id, string region_id, AiStoryResult aiResult)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                if (aiResult?.Data == null)
                {
                    throw new Exception("AI 回傳的劇本資料為空！");
                }
                
                var savedStories = new List<Dictionary<string, string>>();

                // 💡 修正處 3：因為你的 aiResult.Data 是單一物件，所以先拿掉 foreach 迴圈，直接使用它
                var scriptData = aiResult.Data;

                string newStoryId = "AI_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
                string newNpcId = "NPC_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();

                // --- 1. 寫入主劇本 ---
                string insertStorySql = @"
                    INSERT INTO md_story (
                        story_id, title, subtitle, prologue, synopsis, 
                        region_id, is_active, category, transport, sort_order,
                        expected_badges_json, expected_postcards
                    )
                    VALUES (
                        @story_id, @title, @subtitle, @prologue, @synopsis, 
                        @region_id, 1, 'AI專屬生成', '客製化交通', 99,
                        '[]', @expected_postcards
                    );
                ";

                using (var cmd = new MySqlCommand(insertStorySql, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@story_id", newStoryId);
                    cmd.Parameters.AddWithValue("@title", scriptData.Title ?? "專屬客製化旅程");
                    cmd.Parameters.AddWithValue("@subtitle", "AI 智能生成劇本");
                    cmd.Parameters.AddWithValue("@prologue", scriptData.Preface ?? "");
                    cmd.Parameters.AddWithValue("@synopsis", scriptData.Synopsis ?? "");
                    cmd.Parameters.AddWithValue("@region_id", region_id ?? "");
                    cmd.Parameters.AddWithValue("@expected_postcards", scriptData.Nodes?.Count ?? 0);
                    cmd.ExecuteNonQuery();
                }

                // --- 2. 寫入 NPC ---
                if (scriptData.Npc != null)
                {
                    string insertNpcSql = @"
                        INSERT INTO md_npc (
                            npc_id, npc_name, npc_title, introduction, default_dialogue, default_emotion, is_active
                        ) VALUES (
                            @npc_id, @name, @title, @intro, @dialogue, 'normal', 1
                        );
                    ";
                    using (var cmdNpc = new MySqlCommand(insertNpcSql, connection, transaction))
                    {
                        cmdNpc.Parameters.AddWithValue("@npc_id", newNpcId);
                        cmdNpc.Parameters.AddWithValue("@name", scriptData.Npc.Name ?? "導覽嚮導");
                        cmdNpc.Parameters.AddWithValue("@title", scriptData.Npc.Role ?? "神秘指引者");
                        cmdNpc.Parameters.AddWithValue("@intro", scriptData.Npc.Intro ?? "");
                        cmdNpc.Parameters.AddWithValue("@dialogue", $"你好，我是{scriptData.Npc.Name}。{scriptData.Npc.Intro}");
                        cmdNpc.ExecuteNonQuery();
                    }
                }

                // --- 3. 寫入節點 (Nodes) ---
                if (scriptData.Nodes != null && scriptData.Nodes.Count > 0)
                {
                    string insertNodeSql = @"
                        INSERT INTO md_story_node (
                            node_id, story_id, node_order, node_title, fog_hint, is_active, day_index
                        )
                        VALUES (
                            @node_id, @story_id, @node_order, @node_title, @fog_hint, 1, 1
                        );
                    ";
                    
                    foreach (var node in scriptData.Nodes)
                    {
                        string placeName = "探索節點";
                        int nodeOrder = 1;
                        string taskDesc = "";

                        try
                        {
                            var type = node.GetType();
                            var placeProp = type.GetProperty("PlaceName") ?? type.GetProperty("place_name");
                            if (placeProp != null) placeName = placeProp.GetValue(node)?.ToString() ?? "探索節點";

                            var orderProp = type.GetProperty("NodeOrder") ?? type.GetProperty("node_order");
                            if (orderProp != null) nodeOrder = Convert.ToInt32(orderProp.GetValue(node) ?? 1);

                            var descProp = type.GetProperty("TaskDescription") ?? type.GetProperty("task_description");
                            if (descProp != null) taskDesc = descProp.GetValue(node)?.ToString() ?? "";
                        }
                        catch { /* 容錯防呆 */ }

                        using (var cmdNode = new MySqlCommand(insertNodeSql, connection, transaction))
                        {
                            cmdNode.Parameters.AddWithValue("@node_id", "N_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper());
                            cmdNode.Parameters.AddWithValue("@story_id", newStoryId);
                            cmdNode.Parameters.AddWithValue("@node_order", nodeOrder);
                            cmdNode.Parameters.AddWithValue("@node_title", placeName);
                            cmdNode.Parameters.AddWithValue("@fog_hint", string.IsNullOrEmpty(taskDesc) ? (object)DBNull.Value : (taskDesc.Length > 255 ? taskDesc.Substring(0, 255) : taskDesc));
                            cmdNode.ExecuteNonQuery();
                        }
                    }
                }

                // 將這筆成功儲存的劇本存入 List，一併回傳給前端
                savedStories.Add(new Dictionary<string, string>
                {
                    { "story_id", newStoryId },
                    { "title", scriptData.Title ?? "專屬客製化旅程" }
                });

                transaction.Commit();
                return savedStories; // 回傳清單，裡面只有這一筆資料
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                throw new Exception($"AI 劇本寫入資料庫失敗: {ex.Message}");
            }
        }
        #endregion
    }
}