// 檔案路徑：System\dao\StoryDao.cs
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using backend.Models;
using backend.utils;
using backend.ViewModels;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;



namespace backend.dao
{
    public class StoryDao
    {
        private readonly AppSettings _appSettings;
        private readonly backend.Services.GeocodingService _geocodingService;
        private readonly backend.Services.Neo4jService _neo4jService;



        public StoryDao(
            IOptions<AppSettings> appSettings,
            backend.Services.GeocodingService geocodingService,
            backend.Services.Neo4jService neo4jService)
        {
            _appSettings = appSettings.Value;
            _geocodingService = geocodingService;
            _neo4jService = neo4jService;
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
                using var cmd = new MySqlCommand("SELECT tag FROM md_story_tag WHERE story_id = @story_id", connection);
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



        #region 劇本詳情 (包含對應景點名稱輸出)
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


            string nodeSql = @"
                SELECT
                    n.node_id,
                    n.node_order,
                    COALESCE(p.place_name, n.node_title) AS place_name,
                    n.fog_hint,
                    n.location_codename,
                    n.opening_text,
                    n.success_text,
                    npc.npc_name
                FROM md_story_node n
                LEFT JOIN md_place p ON n.place_id = p.place_id
                LEFT JOIN md_npc npc ON n.npc_id = npc.npc_id
                WHERE n.story_id = @story_id
                  AND n.is_active = 1
                ORDER BY n.node_order;
            ";


            using var nodeCommand = new MySqlCommand(nodeSql, connection);
            nodeCommand.Parameters.AddWithValue("@story_id", storyId);
            using var nodeReader = nodeCommand.ExecuteReader();


            while (nodeReader.Read())
            {
                int order = Convert.ToInt32(nodeReader["node_order"]);
                string placeName = nodeReader["place_name"].ToString();
                string taskDesc = nodeReader["fog_hint"] == DBNull.Value ? "" : nodeReader["fog_hint"].ToString();


                result.nodes.Add(new NodeDetail
                {
                    order = order,
                    place_name = placeName,
                    task_description = taskDesc,
                    location_codename = nodeReader["location_codename"]?.ToString() ?? "",
                    opening = nodeReader["opening_text"]?.ToString() ?? "",
                    success = nodeReader["success_text"]?.ToString() ?? "",
                    npc_name = nodeReader["npc_name"]?.ToString() ?? ""
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



        // === 新增區塊：正在遊玩中狀態管理 ===
        #region 劇本進行狀態（is_playing）

        /// <summary>
        /// 玩家按下確定，把指定劇本標記為進行中。假設同一時間只允許一個劇本進行中，
        /// 會先把其他劇本重置為 0，再把指定的劇本設為 1。
        /// </summary>
        public bool SetStoryPlaying(string storyId)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                using (var resetCmd = new MySqlCommand(
                    "UPDATE md_story SET is_playing = 0, updated_at = NOW() WHERE is_playing = 1",
                    connection, transaction))
                {
                    resetCmd.ExecuteNonQuery();
                }

                using var cmd = new MySqlCommand(
                    "UPDATE md_story SET is_playing = 1, updated_at = NOW() WHERE story_id = @story_id AND is_active = 1",
                    connection, transaction);
                cmd.Parameters.AddWithValue("@story_id", storyId);
                int affected = cmd.ExecuteNonQuery();

                transaction.Commit();
                return affected > 0;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// 玩家完成或退出劇本時呼叫，把該劇本的進行狀態改回未進行。
        /// </summary>
        public bool ClearStoryPlaying(string storyId)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();

            using var cmd = new MySqlCommand(
                "UPDATE md_story SET is_playing = 0, updated_at = NOW() WHERE story_id = @story_id",
                connection);
            cmd.Parameters.AddWithValue("@story_id", storyId);
            int affected = cmd.ExecuteNonQuery();
            return affected > 0;
        }

        /// <summary>
        /// 查詢目前正在進行中的劇本。
        /// </summary>
        public object GetCurrentPlayingStory()
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();

            using var cmd = new MySqlCommand(
                "SELECT story_id, title, subtitle, category FROM md_story WHERE is_playing = 1 LIMIT 1",
                connection);
            using var reader = cmd.ExecuteReader();

            if (reader.Read())
            {
                return new
                {
                    story_id = reader["story_id"].ToString(),
                    title = reader["title"].ToString(),
                    subtitle = reader["subtitle"] == DBNull.Value ? "" : reader["subtitle"].ToString(),
                    category = reader["category"] == DBNull.Value ? "" : reader["category"].ToString()
                };
            }
            return null;
        }

        #endregion



        #region GPS 定位生成：完整儲存 AI 劇本藍圖（欄位不遺失版，並補上地圖座標）
        public async Task<string> SaveFullAiGeneratedStory(string epId, string regionId, string cityName, ScriptBlueprintData data)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            using var transaction = connection.BeginTransaction();


            try
            {
                if (data == null) throw new Exception("AI 回傳的劇本資料為空！");


                string newStoryId = "AI_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
                string newNpcId = "NPC_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();


                string insertStorySql = @"
                    INSERT INTO md_story (
                        story_id, title, subtitle, prologue, synopsis,
                        region_id, is_active, category, transport, sort_order,
                        expected_badges_json, expected_postcards, is_night_mode
                    )
                    VALUES (
                        @story_id, @title, @subtitle, @prologue, @synopsis,
                        @region_id, 1, 'AI專屬生成', '客製化交通', 99,
                        '[]', @expected_postcards, @is_night_mode
                    );
                ";


                using (var cmd = new MySqlCommand(insertStorySql, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@story_id", newStoryId);
                    cmd.Parameters.AddWithValue("@title", data.title ?? "專屬客製化旅程");
                    cmd.Parameters.AddWithValue("@subtitle", "AI 智能生成劇本");
                    cmd.Parameters.AddWithValue("@prologue", data.preface ?? "");
                    cmd.Parameters.AddWithValue("@synopsis", data.synopsis ?? "");
                    cmd.Parameters.AddWithValue("@region_id", regionId ?? "");
                    cmd.Parameters.AddWithValue("@expected_postcards", data.nodes?.Count ?? 0);
                    cmd.Parameters.AddWithValue("@is_night_mode", data.is_night_mode);
                    cmd.ExecuteNonQuery();
                }


                if (data.npc != null)
                {
                    string insertNpcSql = @"
                        INSERT INTO md_npc (
                            npc_id, npc_name, npc_title, introduction, default_dialogue, default_emotion, is_active
                        ) VALUES (
                            @npc_id, @name, @title, @intro, @dialogue, 'normal', 1
                        );
                    ";
                    using var cmdNpc = new MySqlCommand(insertNpcSql, connection, transaction);
                    cmdNpc.Parameters.AddWithValue("@npc_id", newNpcId);
                    cmdNpc.Parameters.AddWithValue("@name", data.npc.name ?? "導覽嚮導");
                    cmdNpc.Parameters.AddWithValue("@title", data.npc.role ?? "神秘指引者");
                    cmdNpc.Parameters.AddWithValue("@intro", data.npc.intro ?? "");
                    cmdNpc.Parameters.AddWithValue("@dialogue", $"你好，我是{data.npc.name}。{data.npc.intro}");
                    cmdNpc.ExecuteNonQuery();
                }


                if (data.nodes != null)
                {
                    // 新增（一般）：地點是 Nominatim 地理編碼推算出來的合成地點，place_id 是本地自建的 P_ 編號，每筆都不會重複。
                    string insertPlaceSql = @"
                        INSERT INTO md_place (
                            place_id, place_name, region_id, latitude, longitude, is_active
                        ) VALUES (
                            @place_id, @place_name, @region_id, @latitude, @longitude, 1
                        );
                    ";

                    // Upsert（Neo4j 有比對到景點時使用）：place_id 是該景點在 Neo4j 的 uid，
                    // 同一個真實景點很可能被不同劇本重複選中，用 ON DUPLICATE KEY UPDATE 避免撞 PK。
                    string upsertPlaceSql = @"
                        INSERT INTO md_place (
                            place_id, place_name, region_id, latitude, longitude, is_active
                        ) VALUES (
                            @place_id, @place_name, @region_id, @latitude, @longitude, 1
                        )
                        ON DUPLICATE KEY UPDATE
                            place_name = VALUES(place_name),
                            latitude   = VALUES(latitude),
                            longitude  = VALUES(longitude);
                    ";

                    string insertNodeSql = @"
                        INSERT INTO md_story_node (
                            node_id, story_id, node_order, node_title, fog_hint, is_active, day_index,
                            location_codename, opening_text, success_text, npc_id,
                            place_name_text, task_type, place_id
                        )
                        VALUES (
                            @node_id, @story_id, @node_order, @node_title, @fog_hint, 1, 1,
                            @location_codename, @opening_text, @success_text, @npc_id,
                            @place_name_text, @task_type, @place_id
                        );
                    ";


                    foreach (var node in data.nodes)
                    {
                        // 座標查詢策略：先查 Neo4j 真實景點資料（準確，且會帶回 uid），查無結果才退回 Nominatim
                        var neo4jMatch = await _neo4jService.FindAttractionCoordinatesAsync(node.place_name);
                        double? lat = neo4jMatch.lat;
                        double? lng = neo4jMatch.lon;
                        string neo4jUid = neo4jMatch.uid;


                        if (!lat.HasValue || !lng.HasValue)
                        {
                            var geoResult = await _geocodingService.SearchPlaceCoordinatesAsync(node.place_name, cityName);
                            lat = geoResult.lat;
                            lng = geoResult.lng;
                        }


                        // 有比對到 Neo4j 景點時，place_id 直接用該景點的 uid——任務生成（md_place_type
                        // 查詢、Neo4j 圖片抓取）都是靠這個 uid 關聯，用本地合成的 P_ 編號會讓任務系統
                        // 完全找不到這個景點。查無 Neo4j 資料（只能用 Nominatim 地理編碼）才退回合成編號，
                        // 這種節點之後任務生成會找不到 md_place_type 設定而略過，屬於既有的容錯行為。
                        bool matchedRealPlace = !string.IsNullOrWhiteSpace(neo4jUid);
                        string resolvedPlaceId = matchedRealPlace
                            ? neo4jUid
                            : "P_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();


                        using (var cmdPlace = new MySqlCommand(matchedRealPlace ? upsertPlaceSql : insertPlaceSql, connection, transaction))
                        {
                            cmdPlace.Parameters.AddWithValue("@place_id", resolvedPlaceId);
                            cmdPlace.Parameters.AddWithValue("@place_name", node.place_name ?? "");
                            cmdPlace.Parameters.AddWithValue("@region_id", regionId ?? "");
                            cmdPlace.Parameters.AddWithValue("@latitude", lat.HasValue ? (object)lat.Value : DBNull.Value);
                            cmdPlace.Parameters.AddWithValue("@longitude", lng.HasValue ? (object)lng.Value : DBNull.Value);
                            cmdPlace.ExecuteNonQuery();
                        }


                        using (var cmdNode = new MySqlCommand(insertNodeSql, connection, transaction))
                        {
                            cmdNode.Parameters.AddWithValue("@node_id", "N_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper());
                            cmdNode.Parameters.AddWithValue("@story_id", newStoryId);
                            cmdNode.Parameters.AddWithValue("@node_order", node.node_order);
                            cmdNode.Parameters.AddWithValue("@node_title", node.node_title ?? "");
                            cmdNode.Parameters.AddWithValue("@fog_hint",
                                string.IsNullOrEmpty(node.task_description) ? (object)DBNull.Value
                                : (node.task_description.Length > 255 ? node.task_description.Substring(0, 255) : node.task_description));
                            cmdNode.Parameters.AddWithValue("@location_codename", node.location_codename ?? "");
                            cmdNode.Parameters.AddWithValue("@opening_text", node.dialogues?.opening ?? "");
                            cmdNode.Parameters.AddWithValue("@success_text", node.dialogues?.success ?? "");
                            cmdNode.Parameters.AddWithValue("@npc_id", newNpcId);
                            cmdNode.Parameters.AddWithValue("@place_name_text", node.place_name ?? "");
                            cmdNode.Parameters.AddWithValue("@task_type", node.task_type ?? "");
                            cmdNode.Parameters.AddWithValue("@place_id", resolvedPlaceId);
                            cmdNode.ExecuteNonQuery();
                        }
                    }
                }


                transaction.Commit();
                return newStoryId;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                throw new Exception($"AI 劇本完整寫入資料庫失敗: {ex.Message}");
            }
        }



        /// <summary>
        /// 依 story_id 完整讀出劇本，欄位結構與 AI 原始藍圖 100% 對應，不遺失 task_type / node_title / is_night_mode。
        /// </summary>
        public ScriptBlueprintData GetFullDetail(string storyId)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();


            string storySql = @"
                SELECT title, prologue, synopsis, is_night_mode
                FROM md_story
                WHERE story_id = @story_id AND is_active = 1;
            ";


            var result = new ScriptBlueprintData { nodes = new List<ScriptBlueprintNode>() };
            string npcId = null;


            using (var storyCmd = new MySqlCommand(storySql, connection))
            {
                storyCmd.Parameters.AddWithValue("@story_id", storyId);
                using var reader = storyCmd.ExecuteReader();
                if (!reader.Read()) throw new Exception("找不到此劇本：" + storyId);


                result.title = reader["title"].ToString();
                result.preface = reader["prologue"] == DBNull.Value ? "" : reader["prologue"].ToString();
                result.synopsis = reader["synopsis"] == DBNull.Value ? "" : reader["synopsis"].ToString();
                result.is_night_mode = Convert.ToBoolean(reader["is_night_mode"]);
            }


            string nodeSql = @"
                SELECT node_order, place_name_text, location_codename, node_title, task_type, fog_hint,
                       opening_text, success_text, npc_id
                FROM md_story_node
                WHERE story_id = @story_id AND is_active = 1
                ORDER BY node_order;
            ";


            using (var nodeCmd = new MySqlCommand(nodeSql, connection))
            {
                nodeCmd.Parameters.AddWithValue("@story_id", storyId);
                using var reader = nodeCmd.ExecuteReader();
                while (reader.Read())
                {
                    if (npcId == null && reader["npc_id"] != DBNull.Value) npcId = reader["npc_id"].ToString();


                    result.nodes.Add(new ScriptBlueprintNode
                    {
                        node_order = Convert.ToInt32(reader["node_order"]),
                        place_name = reader["place_name_text"] == DBNull.Value ? "" : reader["place_name_text"].ToString(),
                        location_codename = reader["location_codename"] == DBNull.Value ? "" : reader["location_codename"].ToString(),
                        node_title = reader["node_title"] == DBNull.Value ? "" : reader["node_title"].ToString(),
                        task_type = reader["task_type"] == DBNull.Value ? "" : reader["task_type"].ToString(),
                        task_description = reader["fog_hint"] == DBNull.Value ? "" : reader["fog_hint"].ToString(),
                        dialogues = new ScriptBlueprintDialogues
                        {
                            opening = reader["opening_text"] == DBNull.Value ? "" : reader["opening_text"].ToString(),
                            success = reader["success_text"] == DBNull.Value ? "" : reader["success_text"].ToString()
                        }
                    });
                }
            }


            if (npcId != null)
            {
                string npcSql = "SELECT npc_name, npc_title, introduction FROM md_npc WHERE npc_id = @npc_id";
                using var npcCmd = new MySqlCommand(npcSql, connection);
                npcCmd.Parameters.AddWithValue("@npc_id", npcId);
                using var npcReader = npcCmd.ExecuteReader();
                if (npcReader.Read())
                {
                    result.npc = new ScriptBlueprintNpc
                    {
                        name = npcReader["npc_name"].ToString(),
                        role = npcReader["npc_title"] == DBNull.Value ? "" : npcReader["npc_title"].ToString(),
                        intro = npcReader["introduction"] == DBNull.Value ? "" : npcReader["introduction"].ToString()
                    };
                }
            }


            return result;
        }
        #endregion



        #region GPS 附近地點查詢（依實際距離排序，供劇本節點使用）
        public List<NearbyPlaceDistanceResponse> GetNearbyPlacesByDistance(double lat, double lng, double radiusKm)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();


            string sql = @"
                SELECT
                    place_id,
                    place_name,
                    (
                        6371 * ACOS(
                            COS(RADIANS(@lat)) * COS(RADIANS(latitude)) *
                            COS(RADIANS(longitude) - RADIANS(@lng)) +
                            SIN(RADIANS(@lat)) * SIN(RADIANS(latitude))
                        )
                    ) AS distance_km
                FROM md_place
                WHERE is_active = 1
                HAVING distance_km <= @radius_km
                ORDER BY distance_km ASC;
            ";


            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@lat", lat);
            command.Parameters.AddWithValue("@lng", lng);
            command.Parameters.AddWithValue("@radius_km", radiusKm);


            using var reader = command.ExecuteReader();
            var result = new List<NearbyPlaceDistanceResponse>();


            while (reader.Read())
            {
                result.Add(new NearbyPlaceDistanceResponse
                {
                    place_id = reader["place_id"].ToString(),
                    place_name = reader["place_name"].ToString(),
                    location_codename = "",
                    distance_km = Convert.ToDouble(reader["distance_km"])
                });
            }


            return result;
        }
        #endregion



        #region 依城市/鄉鎮名稱模糊比對，找出對應的 region_id
        public string FindRegionIdByName(string cityName, string townName)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();


            string sql = @"
                SELECT region_id
                FROM md_region
                WHERE is_active = 1
                  AND REPLACE(city_name, '臺', '台') = REPLACE(@city_name, '臺', '台')
                  AND (
                        @town_name = ''
                        OR REPLACE(district_name, '臺', '台') = REPLACE(@town_name, '臺', '台')
                      )
                LIMIT 1;
            ";


            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@city_name", cityName ?? "");
            command.Parameters.AddWithValue("@town_name", townName ?? "");


            var resultObj = command.ExecuteScalar();
            return resultObj?.ToString();
        }
        #endregion



        #region Agent 即時推薦（/spin 用，存進 md_agent_recommendation）
        public string SaveAgentRecommendation(string epId, string cityName, string townName, double lat, double lng, AgentOrchestrateResponse result)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();


            string newId = "REC_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();


            string sql = @"
                INSERT INTO md_agent_recommendation (
                    recommendation_id, ep_id, city_name, town_name, user_lat, user_lon,
                    voice_input, emotion_detected, agent_thought_process, extracted_tags_json,
                    spot_name, spot_address, spot_description, spot_tags_json, spot_distance_m,
                    theme_title, npc_dialogue, task_mission, preparation_tips_json,
                    calendar_sync_url, social_share_url, next_step
                ) VALUES (
                    @recommendation_id, @ep_id, @city_name, @town_name, @user_lat, @user_lon,
                    @voice_input, @emotion_detected, @agent_thought_process, @extracted_tags_json,
                    @spot_name, @spot_address, @spot_description, @spot_tags_json, @spot_distance_m,
                    @theme_title, @npc_dialogue, @task_mission, @preparation_tips_json,
                    @calendar_sync_url, @social_share_url, @next_step
                );
            ";


            var spot = result.phase_3_graph_rag?.recommended_spot;
            var blueprint = result.phase_4_action_and_tools?.script_blueprint;


            using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@recommendation_id", newId);
            cmd.Parameters.AddWithValue("@ep_id", epId);
            cmd.Parameters.AddWithValue("@city_name", cityName ?? "");
            cmd.Parameters.AddWithValue("@town_name", townName ?? "");
            cmd.Parameters.AddWithValue("@user_lat", lat);
            cmd.Parameters.AddWithValue("@user_lon", lng);
            cmd.Parameters.AddWithValue("@voice_input", result.phase_1_perception?.voice_input ?? "");
            cmd.Parameters.AddWithValue("@emotion_detected", result.phase_1_perception?.emotion_detected ?? "");
            cmd.Parameters.AddWithValue("@agent_thought_process", result.phase_2_cognition?.agent_thought_process ?? "");
            cmd.Parameters.AddWithValue("@extracted_tags_json", JsonSerializer.Serialize(result.phase_2_cognition?.extracted_tags ?? new List<string>()));
            cmd.Parameters.AddWithValue("@spot_name", spot?.name ?? "");
            cmd.Parameters.AddWithValue("@spot_address", spot?.address ?? "");
            cmd.Parameters.AddWithValue("@spot_description", spot?.description ?? "");
            cmd.Parameters.AddWithValue("@spot_tags_json", JsonSerializer.Serialize(spot?.tags ?? new List<string>()));
            cmd.Parameters.AddWithValue("@spot_distance_m", spot?.distance_m ?? 0);
            cmd.Parameters.AddWithValue("@theme_title", blueprint?.theme_title ?? "");
            cmd.Parameters.AddWithValue("@npc_dialogue", blueprint?.npc_dialogue ?? "");
            cmd.Parameters.AddWithValue("@task_mission", blueprint?.task_mission ?? "");
            cmd.Parameters.AddWithValue("@preparation_tips_json", JsonSerializer.Serialize(blueprint?.preparation_tips ?? new List<string>()));
            cmd.Parameters.AddWithValue("@calendar_sync_url", result.phase_4_action_and_tools?.tool_1_calendar_sync ?? "");
            cmd.Parameters.AddWithValue("@social_share_url", result.phase_4_action_and_tools?.tool_2_social_share ?? "");
            cmd.Parameters.AddWithValue("@next_step", result.phase_5_next_step ?? "");
            cmd.ExecuteNonQuery();


            return newId;
        }
        #endregion
    }
}