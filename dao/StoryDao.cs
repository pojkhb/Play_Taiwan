// 檔案路徑：System\dao\StoryDao.cs
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
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
            // 新資料庫沒有地區主表，改用 story 自己的 city_name / district_name 比對。
            string sql = @"
                SELECT
                    s.s_id            AS story_id,
                    s.story_title     AS title,
                    s.story_prologue  AS prologue,
                    s.sd_transport    AS transport,
                    s.story_badge     AS badge_raw,
                    COALESCE(s.story_postcards, 0) AS expected_postcards,
                    CONCAT(COALESCE(s.city_name, ''), COALESCE(s.district_name, '')) AS region
                FROM story s
                WHERE s.is_active = 1
                  AND (@region = ''
                       OR CONCAT(COALESCE(s.city_name, ''), COALESCE(s.district_name, '')) LIKE CONCAT('%', @region, '%'))
                ORDER BY s.created_at;
            ";

            string searchRegion = $"{req?.city_name}{req?.town_name}".Trim();

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();

                List<StoryOptionResponse> stories = conn.Query(sql, new { region = searchRegion })
                    .Select(r => new StoryOptionResponse
                    {
                        story_id = (int)r.story_id,
                        title = r.title as string,
                        prologue = r.prologue as string,
                        category = null,
                        transport = r.transport as string,
                        expected_badges = ParseBadgeList(r.badge_raw as string),
                        expected_postcards = (int)r.expected_postcards,
                        region_id = null,
                        region = r.region as string
                    })
                    .ToList();

                if (stories.Count == 0) return stories;

                foreach (StoryOptionResponse story in stories)
                {
                    story.route_preview = GetRoutePreview(conn, story.story_id);
                }

                if (req?.preferences == null || req.preferences.Count == 0)
                {
                    return stories;
                }

                // 依 story_tag 與使用者偏好的重疊數量排序，重疊越多排越前面。
                string tagSql = "SELECT s_tag FROM story_tag WHERE s_id = @storyId;";

                return stories
                    .Select(story => new
                    {
                        Story = story,
                        Score = conn.Query<string>(tagSql, new { storyId = story.story_id })
                                    .Count(tag => req.preferences.Contains(tag))
                    })
                    .OrderByDescending(x => x.Score)
                    .Select(x => x.Story)
                    .ToList();
            }
        }

        /// <summary>story.story_badge 是純文字欄位，同時容許 JSON 陣列與逗號分隔兩種格式。</summary>
        private static List<string> ParseBadgeList(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();

            if (raw.TrimStart().StartsWith("["))
            {
                try
                {
                    return JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
                }
                catch
                {
                    // 不是合法 JSON 就退回逗號分隔處理
                }
            }

            return raw.Split(',')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
        }
        #endregion



        #region 劇本詳情 (包含對應節點輸出)
        public StoryDetailResponse GetDetail(int storyId)
        {
            string storySql = @"
                SELECT s_id, story_title, story_prologue, story_synopsis
                FROM story
                WHERE s_id = @storyId AND is_active = 1;
            ";

            // 新資料庫沒有 NPC 主表，story_node.npc_id 只是一個裸的整數，因此不再 JOIN NPC 名稱。
            string nodeSql = @"
                SELECT
                    sn_id, sn_order, sn_title, sn_hint,
                    location_codename, sn_opening_text, sn_success_text
                FROM story_node
                WHERE s_id = @storyId
                ORDER BY sn_order;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();

                var story = conn.QueryFirstOrDefault(storySql, new { storyId });

                if (story == null)
                {
                    throw new Exception("找不到此劇本：" + storyId);
                }

                string prologue = (story.story_prologue as string) ?? "";
                string synopsis = (story.story_synopsis as string) ?? "";

                var result = new StoryDetailResponse
                {
                    story_id = (int)story.s_id,
                    title = story.story_title as string,
                    subtitle = "",
                    preface = string.IsNullOrEmpty(prologue) ? synopsis : prologue,
                    synopsis = synopsis,
                    nodes = new List<NodeDetail>(),
                    route_nodes = new List<StoryOptionResponse.RouteNode>()
                };

                foreach (var node in conn.Query(nodeSql, new { storyId }))
                {
                    int order = (int)node.sn_order;
                    string title = (node.sn_title as string) ?? "";

                    result.nodes.Add(new NodeDetail
                    {
                        order = order,
                        place_name = title,
                        task_description = (node.sn_hint as string) ?? "",
                        location_codename = (node.location_codename as string) ?? "",
                        opening = (node.sn_opening_text as string) ?? "",
                        success = (node.sn_success_text as string) ?? "",
                        npc_name = ""
                    });

                    result.route_nodes.Add(new StoryOptionResponse.RouteNode
                    {
                        node_id = (int)node.sn_id,
                        location_name = title,
                        node_order = order
                    });
                }

                return result;
            }
        }
        #endregion



        #region 劇本卡片路線預覽
        private static List<string> GetRoutePreview(MySqlConnection conn, int storyId)
        {
            string sql = @"
                SELECT sn_title
                FROM story_node
                WHERE s_id = @storyId
                ORDER BY sn_order;
            ";

            return conn.Query<string>(sql, new { storyId }).ToList();
        }
        #endregion



        #region 劇本進行狀態
        // 新資料表 story 沒有 is_playing 欄位，進行中狀態改記在 story_session.ss_status。

        /// <summary>
        /// 玩家按下確定，把指定劇本標記為進行中。同一時間只允許一個劇本進行中，
        /// 會先把該使用者其他進行中的場次改為 paused，再建立/更新這一場。
        /// </summary>
        public bool SetStoryPlaying(int auId, int storyId)
        {
            string pauseOthersSql = @"
                UPDATE story_session
                SET ss_status = 'paused'
                WHERE au_id = @auId AND s_id <> @storyId AND ss_status = 'in_progress';
            ";

            string updateSql = @"
                UPDATE story_session
                SET ss_status = 'in_progress', last_played_at = NOW()
                WHERE au_id = @auId AND s_id = @storyId;
            ";

            string insertSql = @"
                INSERT INTO story_session (au_id, s_id, ss_current, ss_status, started_at, last_played_at)
                SELECT @auId, s.s_id, 0, 'in_progress', NOW(), NOW()
                FROM story s
                WHERE s.s_id = @storyId AND s.is_active = 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        conn.Execute(pauseOthersSql, new { auId, storyId }, transaction);

                        int affected = conn.Execute(updateSql, new { auId, storyId }, transaction);
                        if (affected == 0)
                        {
                            affected = conn.Execute(insertSql, new { auId, storyId }, transaction);
                        }

                        transaction.Commit();
                        return affected > 0;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// 玩家結束劇本時呼叫，把該場次標記為完成（首頁與過往紀錄都以 completed 統計）。
        /// </summary>
        public bool ClearStoryPlaying(int auId, int storyId)
        {
            string sql = @"
                UPDATE story_session
                SET ss_status = 'completed', completed_at = NOW()
                WHERE au_id = @auId AND s_id = @storyId;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.Execute(sql, new { auId, storyId }) > 0;
            }
        }

        /// <summary>查詢該使用者目前正在進行中的劇本。</summary>
        public object GetCurrentPlayingStory(int auId)
        {
            string sql = @"
                SELECT s.s_id AS story_id, s.story_title AS title, ss.ss_current AS current_order
                FROM story_session ss
                INNER JOIN story s ON s.s_id = ss.s_id
                WHERE ss.au_id = @auId AND ss.ss_status = 'in_progress'
                ORDER BY ss.last_played_at DESC
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();

                var row = conn.QueryFirstOrDefault(sql, new { auId });

                if (row == null) return null;

                return new
                {
                    story_id = (int)row.story_id,
                    title = (row.title as string) ?? "",
                    current_order = (int)row.current_order
                };
            }
        }
        #endregion



        #region GPS 定位生成：完整儲存 AI 劇本藍圖
        /// <summary>
        /// 把 AI 產生的劇本藍圖寫進 story + story_node。
        /// 與舊版的兩點差異：
        /// 1. 新資料庫沒有 NPC 主表，NPC 名稱／介紹無處可存，只能放棄（story.npc_id 留 null）。
        /// 2. place 的主鍵是 int 自增，沒辦法再用 Neo4j uid 當主鍵。
        ///    節點仍然把 Neo4j uid 寫進 story_node.place_id（任務生成靠這個關聯），
        ///    另外把景點名稱與座標補進 place 表，避免地理資訊整個遺失。
        /// </summary>
        public async Task<int> SaveFullAiGeneratedStory(
            int auId, string cityName, string districtName, ScriptBlueprintData data)
        {
            if (data == null) throw new Exception("AI 回傳的劇本資料為空！");

            string insertStorySql = @"
                INSERT INTO story (
                    au_id, city_name, district_name, sd_transport,
                    story_title, story_prologue, story_synopsis,
                    story_badge, story_postcards, is_active, is_night_mode
                ) VALUES (
                    @auId, @cityName, @districtName, '客製化交通',
                    @title, @prologue, @synopsis,
                    '', @expectedPostcards, 1, @isNightMode
                );
                SELECT LAST_INSERT_ID();
            ";

            string insertNodeSql = @"
                INSERT INTO story_node (
                    s_id, place_id, sn_order, sn_title, sn_hint,
                    is_hidden, is_night_only, is_active,
                    location_codename, sn_opening_text, sn_success_text, sn_task_type
                ) VALUES (
                    @storyId, @placeId, @order, @title, @hint,
                    2, 2, 2,
                    @codename, @opening, @success, @taskType
                );
            ";

            // place 沒有 UNIQUE 欄位可做 upsert，先用名稱查，查不到才新增。
            string findPlaceSql = "SELECT p_id FROM place WHERE p_name = @name LIMIT 1;";
            string insertPlaceSql = @"
                INSERT INTO place (region_id, p_name, p_latitude, p_longitude)
                VALUES (@regionId, @name, @lat, @lng);
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                await conn.OpenAsync();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        int newStoryId = await conn.ExecuteScalarAsync<int>(insertStorySql, new
                        {
                            auId,
                            cityName = cityName ?? "",
                            districtName = districtName ?? "",
                            title = data.title ?? "專屬客製化旅程",
                            prologue = data.preface ?? "",
                            synopsis = data.synopsis ?? "",
                            expectedPostcards = data.nodes?.Count ?? 0,
                            isNightMode = data.is_night_mode ? 1 : 2
                        }, transaction);

                        foreach (var node in data.nodes ?? new List<ScriptBlueprintNode>())
                        {
                            // 座標先查 Neo4j 真實景點（準確且會回傳 uid），查不到才退回 Nominatim 地理編碼。
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

                            if (!string.IsNullOrWhiteSpace(node.place_name))
                            {
                                int? existingPlaceId = await conn.ExecuteScalarAsync<int?>(
                                    findPlaceSql, new { name = node.place_name }, transaction);

                                if (existingPlaceId == null)
                                {
                                    await conn.ExecuteAsync(insertPlaceSql, new
                                    {
                                        regionId = neo4jUid,
                                        name = node.place_name,
                                        lat,
                                        lng
                                    }, transaction);
                                }
                            }

                            string hint = node.task_description;
                            if (!string.IsNullOrEmpty(hint) && hint.Length > 255)
                            {
                                hint = hint.Substring(0, 255);
                            }

                            await conn.ExecuteAsync(insertNodeSql, new
                            {
                                storyId = newStoryId,
                                placeId = neo4jUid,
                                order = node.node_order,
                                title = node.node_title ?? node.place_name ?? "",
                                hint,
                                codename = node.location_codename ?? "",
                                opening = node.dialogues?.opening ?? "",
                                success = node.dialogues?.success ?? "",
                                taskType = node.task_type ?? ""
                            }, transaction);
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
            }
        }

        /// <summary>
        /// 依 s_id 完整讀出劇本，結構與 AI 原始藍圖對應。
        /// NPC 區塊因為新資料庫沒有 NPC 主表，固定為 null。
        /// </summary>
        public ScriptBlueprintData GetFullDetail(int storyId)
        {
            string storySql = @"
                SELECT story_title, story_prologue, story_synopsis, is_night_mode
                FROM story
                WHERE s_id = @storyId AND is_active = 1;
            ";

            string nodeSql = @"
                SELECT sn_order, sn_title, location_codename, sn_task_type,
                       sn_hint, sn_opening_text, sn_success_text
                FROM story_node
                WHERE s_id = @storyId
                ORDER BY sn_order;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();

                var story = conn.QueryFirstOrDefault(storySql, new { storyId });

                if (story == null)
                {
                    throw new Exception("找不到此劇本：" + storyId);
                }

                var result = new ScriptBlueprintData
                {
                    title = story.story_title as string,
                    preface = (story.story_prologue as string) ?? "",
                    synopsis = (story.story_synopsis as string) ?? "",
                    is_night_mode = Convert.ToInt32(story.is_night_mode) == 1,
                    npc = null,
                    nodes = new List<ScriptBlueprintNode>()
                };

                foreach (var node in conn.Query(nodeSql, new { storyId }))
                {
                    string title = (node.sn_title as string) ?? "";

                    result.nodes.Add(new ScriptBlueprintNode
                    {
                        node_order = (int)node.sn_order,
                        place_name = title,
                        location_codename = (node.location_codename as string) ?? "",
                        node_title = title,
                        task_type = (node.sn_task_type as string) ?? "",
                        task_description = (node.sn_hint as string) ?? "",
                        dialogues = new ScriptBlueprintDialogues
                        {
                            opening = (node.sn_opening_text as string) ?? "",
                            success = (node.sn_success_text as string) ?? ""
                        }
                    });
                }

                return result;
            }
        }
        #endregion



        #region GPS 附近地點查詢（依實際距離排序，供劇本節點使用）
        public List<NearbyPlaceDistanceResponse> GetNearbyPlacesByDistance(double lat, double lng, double radiusKm)
        {
            // place 沒有 is_active 欄位，改為只要有座標就納入計算。
            string sql = @"
                SELECT
                    p_id   AS place_id,
                    p_name AS place_name,
                    (
                        6371 * ACOS(
                            COS(RADIANS(@lat)) * COS(RADIANS(p_latitude)) *
                            COS(RADIANS(p_longitude) - RADIANS(@lng)) +
                            SIN(RADIANS(@lat)) * SIN(RADIANS(p_latitude))
                        )
                    ) AS distance_km
                FROM place
                WHERE p_latitude IS NOT NULL AND p_longitude IS NOT NULL
                HAVING distance_km <= @radiusKm
                ORDER BY distance_km;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();

                return conn.Query<NearbyPlaceDistanceResponse>(
                        sql, new { lat, lng, radiusKm })
                    .Select(x =>
                    {
                        x.location_codename = "";
                        return x;
                    })
                    .ToList();
            }
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