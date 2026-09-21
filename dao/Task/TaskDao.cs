using System;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using backend.utils;
using backend.Models;
using backend.Services;
using backend.Sqls.mysql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    /// <summary>
    /// 任務相關資料庫存取操作。
    /// 包含查詢景點座標、獲取提示內容等。
    /// </summary>
    public class TaskDao(IOptions<AppSettings> app_settings_obj, Neo4jService neo4j_service_obj)
    {
        private readonly HttpContext _ipContext;
        private readonly MysqlConnect mysql_connect = new(app_settings_obj.Value.mydb);
        private readonly Neo4jService neo4j_service = neo4j_service_obj;

        #region 查詢任務地點

        /// <summary>
        /// <summary>
        /// 依 task_id 查詢任務地點，關聯 md_task 表。
        /// </summary>
        public async Task<Location> GetPlaceLocation(string node_id)
        {
            // 1. MySQL 查詢 place_id
            Hashtable param = new()
            {
                {"@node_id", new MySQLParameter(node_id, MySqlDbType.VarChar)}
            };

            string sql = @"
                        SELECT place_id, story_id FROM md_story_node
                        WHERE node_id = @node_id";

            List<SearchNeo4jReq> mysqlData = mysql_connect.GetDataList<SearchNeo4jReq>(sql, param);

            // 若查詢結果無 place_id 則回傳 null
            if (mysqlData == null || mysqlData.Count == 0 || string.IsNullOrEmpty(mysqlData[0].place_id))
            {
                return null;
            }

            string targetPlaceId = mysqlData[0].place_id;
            string targetStoryId = mysqlData[0].story_id;

            // 2. 建立 Neo4j Cypher 查詢
            string cypher = @"
                            MATCH (n)
                            WHERE (n:Attraction OR n:Event OR n:Hotel OR n:Restaurant)
                            AND (elementId(n) = $id OR n.id = $id OR n.EventID = $id OR n.uid = $id)
                            RETURN coalesce(n.lat, n.PositionLat) AS Lat,
                                coalesce(n.lon, n.PositionLon) AS Lon
                            LIMIT 1";

            // 3. 執行 Neo4j 查詢
            var locationResult = await neo4j_service.ExecuteCypherAsync<List<Location>>(
                cypher,
                new { id = targetPlaceId }
            );

            // 4. 若有結果，則附加 place_id 及 story_id 回傳
            if (locationResult != null && locationResult.Count > 0)
            {
                locationResult[0].PlaceId = targetPlaceId;
                locationResult[0].StoryId = targetStoryId;
                return locationResult[0];
            }

            // 若查無結果，回傳預設假座標供測試
            return new Location { PlaceId = targetPlaceId, StoryId = targetStoryId, Lat = 25.0456, Lon = 121.5123 };
        }
        #endregion

        #region 景點圖片查詢（供「景點猜猜樂」題型組選項用）

        private class ImageUrlRow
        {
            public string url { get; set; }
        }

        /// <summary>
        /// 依 place_id（可能是 Neo4j elementId 或新版 uid）取出該景點的所有圖片網址。
        /// </summary>
        public async Task<List<string>> GetPlaceImagesAsync(string placeId)
        {
            string cypher = @"
                MATCH (n)
                WHERE (n:Attraction OR n:Event OR n:Hotel OR n:Restaurant)
                  AND (elementId(n) = $id OR n.id = $id OR n.EventID = $id OR n.uid = $id)
                MATCH (n)-[:HAS_IMAGE]->(img:Image)
                RETURN img.url AS url";

            var rows = await neo4j_service.ExecuteCypherAsync<List<ImageUrlRow>>(cypher, new { id = placeId });

            return rows?.Select(r => r.url)
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .ToList()
                   ?? new List<string>();
        }

        /// <summary>
        /// 隨機取出其他景點（排除 excludePlaceId）的圖片網址，供錯誤選項使用。
        /// </summary>
        public async Task<List<string>> GetRandomDecoyImagesAsync(string excludePlaceId, int count)
        {
            if (count <= 0) return new List<string>();

            // 注意：合併後的 Neo4j 節點大多只有 uid、沒有 id 屬性，
            // 若直接寫成 NOT (elementId(p) = $id OR p.id = $id OR ...) 排除，
            // 只要其中一個屬性在該節點不存在（值為 NULL），Cypher 三值邏輯會讓整條
            // WHERE 判定變成 NULL 而被當作 false 濾掉，導致幾乎所有列都被排除。
            // 因此先用同一套 OR 條件（在「包含」語意下 NULL 傳染無害）解析出目標節點
            // 真正的 elementId，再用單一不可能為 NULL 的 elementId(p) <> excludeId 排除。
            string cypher = @"
                MATCH (n)
                WHERE (n:Attraction OR n:Event OR n:Hotel OR n:Restaurant)
                  AND (elementId(n) = $id OR n.id = $id OR n.EventID = $id OR n.uid = $id)
                WITH elementId(n) AS excludeId
                LIMIT 1
                MATCH (p)-[:HAS_IMAGE]->(img:Image)
                WHERE (p:Attraction OR p:Event OR p:Hotel OR p:Restaurant)
                  AND elementId(p) <> excludeId
                RETURN img.url AS url
                ORDER BY rand()
                LIMIT $limit";

            var rows = await neo4j_service.ExecuteCypherAsync<List<ImageUrlRow>>(
                cypher, new { id = excludePlaceId, limit = count });

            return rows?.Select(r => r.url)
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .ToList()
                   ?? new List<string>();
        }

        #endregion


        #region 取得提示

        /// <summary>
        /// 依任務代號與錯誤次數取得對應提示，關聯 md_task_hint。
        /// trigger_wrong_count 與 hint_stage 決定顯示哪一階段提示。
        /// </summary>
        public TaskHintResponse GetHintByWrongCount(string task_id, int wrongCount)
        {
            Hashtable param = new()
            {
                {"@task_id", new MySQLParameter(task_id, MySqlDbType.VarChar)},
                {"@wrongCount", new MySQLParameter(wrongCount, MySqlDbType.Int32)}
            };

            string sql = @"
                SELECT hint_text FROM md_task_hint
                WHERE task_id = @task_id AND trigger_wrong_count <= @wrongCount AND is_active = 1
                ORDER BY hint_stage DESC
                LIMIT 1";

            List<HintTextRow> rows = mysql_connect.GetDataList<HintTextRow>(sql, param);
            string hintText = rows is { Count: > 0 } ? rows[0].hint_text : null;

            return new TaskHintResponse
            {
                task_id = task_id,
                hint_text = hintText ?? "目前沒有更多的提示內容了。",
                is_available = hintText != null
            };
        }

        private class HintTextRow
        {
            public string hint_text { get; set; }
        }

        #endregion

        #region 難度等級

        /// <summary>
        /// 紀錄玩家到訪地區的累積次數，並由 md_difficulty_prompt 的設定動態調整難度等級。
        /// </summary>
        public int RecordVisitAndGetDifficulty(string ep_id, string region_id)
        {
            Hashtable epRegionParam = new()
            {
                {"@ep_id", new MySQLParameter(ep_id, MySqlDbType.VarChar)},
                {"@region_id", new MySQLParameter(region_id, MySqlDbType.VarChar)}
            };

            string upsertSql = @"
                INSERT INTO ep_visit_count (ep_id, region_id, visit_count, current_difficulty_star)
                VALUES (@ep_id, @region_id, 1, 1)
                ON DUPLICATE KEY UPDATE visit_count = visit_count + 1";
            mysql_connect.Execute(upsertSql, epRegionParam);

            string visitSql = @"
                SELECT visit_count FROM ep_visit_count
                WHERE ep_id = @ep_id AND region_id = @region_id";
            List<VisitCountRow> visitRows = mysql_connect.GetDataList<VisitCountRow>(visitSql, epRegionParam);
            int visitCount = visitRows is { Count: > 0 } ? visitRows[0].visit_count : 0;

            Hashtable starParam = new()
            {
                {"@visitCount", new MySQLParameter(visitCount, MySqlDbType.Int32)}
            };
            string starSql = @"
                SELECT MAX(difficulty_star) AS max_star FROM md_difficulty_prompt
                WHERE raise_visit_threshold <= @visitCount AND is_active = 1";
            List<MaxStarRow> starRows = mysql_connect.GetDataList<MaxStarRow>(starSql, starParam);
            int newStar = starRows is { Count: > 0 } && starRows[0].max_star.HasValue
                ? starRows[0].max_star.Value
                : 1;

            Hashtable updateParam = new()
            {
                {"@star", new MySQLParameter(newStar, MySqlDbType.Int32)},
                {"@ep_id", new MySQLParameter(ep_id, MySqlDbType.VarChar)},
                {"@region_id", new MySQLParameter(region_id, MySqlDbType.VarChar)}
            };
            string updateSql = @"
                UPDATE ep_visit_count SET current_difficulty_star = @star
                WHERE ep_id = @ep_id AND region_id = @region_id";
            mysql_connect.Execute(updateSql, updateParam);

            return newStar;
        }

        private class VisitCountRow
        {
            public int visit_count { get; set; }
        }

        private class MaxStarRow
        {
            public int? max_star { get; set; }
        }

        /// <summary>由難度等級取得準備給 LLM 的對話提示範本，關聯 md_difficulty_prompt。</summary>
        public string GetDifficultyPrompt(int difficultyStar)
        {
            Hashtable param = new()
            {
                {"@star", new MySQLParameter(difficultyStar, MySqlDbType.Int32)}
            };

            string sql = @"
                SELECT llm_prompt_template FROM md_difficulty_prompt
                WHERE difficulty_star = @star AND is_active = 1";

            List<PromptTemplateRow> rows = mysql_connect.GetDataList<PromptTemplateRow>(sql, param);
            return rows is { Count: > 0 } ? rows[0].llm_prompt_template ?? "" : "";
        }

        private class PromptTemplateRow
        {
            public string llm_prompt_template { get; set; }
        }

        #endregion

        #region 徽章抽選

        /// <summary>
        /// 完成節點時由 md_badge_pool 的權重隨機抽出徽章，並存入 ep_badge，若已擁有則不重複寫入。
        /// </summary>
        public string DrawBadge(string ep_id, string story_id)
        {
            Hashtable poolParam = new()
            {
                {"@story_id", new MySQLParameter(story_id, MySqlDbType.VarChar)}
            };

            string poolSql = @"
                SELECT badge_id, badge_name, weight FROM md_badge_pool
                WHERE (story_id = @story_id OR story_id IS NULL) AND is_active = 1";

            List<BadgePoolRow> pool = mysql_connect.GetDataList<BadgePoolRow>(poolSql, poolParam);

            if (pool == null || pool.Count == 0) return null;

            int totalWeight = pool.Sum(p => p.weight);
            int roll = new Random().Next(totalWeight);
            int cumulative = 0;
            var picked = pool[0];
            foreach (var item in pool)
            {
                cumulative += item.weight;
                if (roll < cumulative) { picked = item; break; }
            }

            Hashtable insertParam = new()
            {
                {"@ep_id", new MySQLParameter(ep_id, MySqlDbType.VarChar)},
                {"@badge_id", new MySQLParameter(picked.badge_id, MySqlDbType.VarChar)},
                {"@badge_name", new MySQLParameter(picked.badge_name, MySqlDbType.VarChar)}
            };

            string insertSql = @"
                INSERT IGNORE INTO ep_badge (ep_id, badge_id, badge_name)
                VALUES (@ep_id, @badge_id, @badge_name)";

            mysql_connect.Execute(insertSql, insertParam);

            return picked.badge_id;
        }

        private class BadgePoolRow
        {
            public string badge_id { get; set; }
            public string badge_name { get; set; }
            public int weight { get; set; }
        }

        #endregion

        #region 隱藏劇情

        /// <summary>
        /// 依玩家目前 GPS 座標檢查是否觸發隱藏劇情，且未曾觸發過，觸發後存入 ep_hidden_level_unlock。
        /// </summary>
        public HiddenLevelTriggerResult CheckHiddenLevelTrigger(string ep_id, double lat, double lng, string region_id)
        {
            Hashtable param = new()
            {
                {"@ep_id", new MySQLParameter(ep_id, MySqlDbType.VarChar)},
                {"@region_id", new MySQLParameter(region_id, MySqlDbType.VarChar)}
            };

            string sql = @"
                SELECT hl.hidden_level_id, hl.title, hl.cultural_background, hl.content,
                       hl.trigger_lat, hl.trigger_lng, hl.trigger_radius_m,
                       hl.reward_badge_id, hl.reward_postcard_id
                FROM md_hidden_level hl
                LEFT JOIN ep_hidden_level_unlock u
                       ON u.hidden_level_id = hl.hidden_level_id AND u.ep_id = @ep_id
                WHERE hl.region_id = @region_id AND hl.is_active = 1 AND u.ep_id IS NULL";

            List<HiddenLevelRow> candidates = mysql_connect.GetDataList<HiddenLevelRow>(sql, param);

            if (candidates != null)
            {
                foreach (var c in candidates)
                {
                    if (HaversineMeters(c.trigger_lat, c.trigger_lng, lat, lng) <= c.trigger_radius_m)
                    {
                        Hashtable insertParam = new()
                        {
                            {"@ep_id", new MySQLParameter(ep_id, MySqlDbType.VarChar)},
                            {"@id", new MySQLParameter(c.hidden_level_id, MySqlDbType.VarChar)}
                        };

                        string insertSql = @"
                            INSERT INTO ep_hidden_level_unlock (ep_id, hidden_level_id)
                            VALUES (@ep_id, @id)";

                        mysql_connect.Execute(insertSql, insertParam);

                        return new HiddenLevelTriggerResult
                        {
                            triggered = true,
                            hidden_level_id = c.hidden_level_id,
                            title = c.title,
                            cultural_background = c.cultural_background,
                            content = c.content,
                            reward_badge_id = c.reward_badge_id,
                            reward_postcard_id = c.reward_postcard_id
                        };
                    }
                }
            }

            return new HiddenLevelTriggerResult { triggered = false };
        }

        private class HiddenLevelRow
        {
            public string hidden_level_id { get; set; }
            public string title { get; set; }
            public string cultural_background { get; set; }
            public string content { get; set; }
            public double trigger_lat { get; set; }
            public double trigger_lng { get; set; }
            public int trigger_radius_m { get; set; }
            public string reward_badge_id { get; set; }
            public string reward_postcard_id { get; set; }
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
        /// 取得特定玩家在指定任務中已答錯的次數。
        /// 若紀錄中未包含此任務，則回傳 0。
        /// </summary>
        /// <param name="epId">玩家代號</param>
        /// <param name="taskId">任務代號</param>
        /// <returns>累積答錯次數</returns>
        public int GetWrongCount(string epId, string taskId)
        {
            Hashtable param = new()
            {
                {"@ep_id", new MySQLParameter(epId, MySqlDbType.VarChar)},
                {"@task_id", new MySQLParameter(taskId, MySqlDbType.VarChar)}
            };

            string sql = @"
                SELECT COALESCE(wrong_count, 0) AS wrong_count
                FROM ep_task_record
                WHERE ep_id = @ep_id
                  AND task_id = @task_id";

            List<WrongCountRow> rows = mysql_connect.GetDataList<WrongCountRow>(sql, param);
            return rows is { Count: > 0 } ? rows[0].wrong_count : 0;
        }

        private class WrongCountRow
        {
            public int wrong_count { get; set; }
        }

        /// <summary>
        /// 玩家答錯時增加答錯次數。
        /// 若為首次錯誤將建立紀錄，若已存在則將 wrong_count 加一。
        /// </summary>
        /// <param name="epId">玩家代號</param>
        /// <param name="taskId">任務代號</param>
        /// <param name="storyId">故事代號</param>
        /// <param name="nodeId">節點代號</param>
        public void IncreaseWrongCount(
            string epId,
            int    taskId,
            string storyId,
            string nodeId)
        {
            Hashtable param = new()
            {
                {"@ep_id",    new MySQLParameter(epId,    MySqlDbType.VarChar)},
                {"@task_id",  new MySQLParameter(taskId,  MySqlDbType.Int32)},
                {"@story_id", new MySQLParameter(string.IsNullOrWhiteSpace(storyId) ? (object)DBNull.Value : storyId, MySqlDbType.VarChar)},
                {"@node_id",  new MySQLParameter(string.IsNullOrWhiteSpace(nodeId)  ? (object)DBNull.Value : nodeId,  MySqlDbType.VarChar)}
            };

            string sql = @"
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
                    updated_at = NOW()";

            mysql_connect.Execute(sql, param);
        }

        /// <summary>
        /// 玩家答對後建立或更新任務完成紀錄。
        /// 將清除之前的答錯次數歸零，並設定為已完成。
        /// </summary>
        /// <param name="epId">玩家代號</param>
        /// <param name="taskId">任務代號</param>
        /// <param name="storyId">故事代號</param>
        /// <param name="nodeId">節點代號</param>
        public void MarkTaskCompleted(
            string epId,
            string taskId,
            string storyId,
            string nodeId)
        {
            Hashtable param = new()
            {
                {"@ep_id", new MySQLParameter(epId, MySqlDbType.VarChar)},
                {"@task_id", new MySQLParameter(taskId, MySqlDbType.VarChar)},
                {"@story_id", new MySQLParameter(string.IsNullOrWhiteSpace(storyId) ? (object)DBNull.Value : storyId, MySqlDbType.VarChar)},
                {"@node_id", new MySQLParameter(string.IsNullOrWhiteSpace(nodeId) ? (object)DBNull.Value : nodeId, MySqlDbType.VarChar)}
            };

            string sql = @"
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
                    updated_at = NOW()";

            mysql_connect.Execute(sql, param);
        }

        #endregion

        #region 劇本內容查詢（供 AI 生成使用）

        /// <summary>
        /// 組合 AI 任務生成所需的景點劇本內容（story_content）。
        /// 取自三個資料表：
        ///   - md_story.prologue：劇本前導故事
        ///   - md_story_node.fog_hint：節點迷霧提示
        ///   - md_place.introduction：景點詳細介紹
        /// 三欄位皆可為 NULL，僅取有值的部分組合成段落回傳。
        /// </summary>
        /// <param name="node_id">節點代號（md_story_node.node_id）</param>
        /// <returns>組合後的景點劇本說明文字，供 AiTaskRequest.story_content 使用</returns>
        public string GetStoryContent(string node_id)
        {
            Hashtable param = new()
            {
                { "@node_id", new MySQLParameter(node_id, MySqlDbType.VarChar) }
            };

            string sql = @"
                SELECT
                    s.prologue       AS story_prologue,
                    n.fog_hint       AS node_fog_hint,
                    p.introduction   AS place_introduction,
                    p.place_name     AS place_name
                FROM md_story_node n
                INNER JOIN md_story s ON s.story_id = n.story_id
                LEFT  JOIN md_place p ON p.place_id  = n.place_id
                WHERE n.node_id = @node_id
                LIMIT 1";

            var rows = mysql_connect.GetDataList<StoryContentRow>(sql, param);
            if (rows == null || rows.Count == 0)
                return string.Empty;

            var r = rows[0];
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(r.place_name))
                parts.Add($"【景點】{r.place_name}");

            if (!string.IsNullOrWhiteSpace(r.place_introduction))
                parts.Add($"【景點介紹】{r.place_introduction}");

            if (!string.IsNullOrWhiteSpace(r.story_prologue))
                parts.Add($"【劇本背景】{r.story_prologue}");

            if (!string.IsNullOrWhiteSpace(r.node_fog_hint))
                parts.Add($"【節點線索】{r.node_fog_hint}");

            return string.Join("\n\n", parts);
        }

        private class StoryContentRow
        {
            public string story_prologue    { get; set; }
            public string node_fog_hint     { get; set; }
            public string place_introduction { get; set; }
            public string place_name        { get; set; }
        }

        #endregion

        #region 任務類型查詢

        /// <summary>
        /// 查詢特定景點所屬的所有類別屬性，JOIN md_place_type + md_type。
        /// </summary>
        public List<PlaceTypeInfo> GetPlaceTypes(string place_id)
        {
            Hashtable param = new()
            {
                {"@place_id", new MySQLParameter(place_id, MySqlDbType.VarChar)}
            };

            string sql = @"
                SELECT pt.type_id, p.category AS place_category, t.type_name
                FROM md_place_type pt
                INNER JOIN md_type t ON t.type_id = pt.type_id
                LEFT JOIN md_place p ON p.place_id = pt.place_id
                WHERE pt.place_id = @place_id";

            return mysql_connect.GetDataList<PlaceTypeInfo>(sql, param) ?? new List<PlaceTypeInfo>();
        }

        public class PlaceTypeInfo
        {
            public int    type_id        { get; set; }
            public string place_category { get; set; }
            public string type_name      { get; set; }
        }

        #endregion

        #region 任務查詢與寫入

        /// <summary>
        /// 任務生成階段所需的節點基本資料（不含座標，故不打 Neo4j）。
        /// </summary>
        public class StoryNodeRef
        {
            public string node_id  { get; set; }
            public string place_id { get; set; }
            public string story_id { get; set; }
        }

        /// <summary>
        /// 取得一份劇本底下所有啟用中的節點，供劇本生成後批次產生任務使用。
        /// </summary>
        public List<StoryNodeRef> GetNodesByStoryId(string story_id)
        {
            Hashtable param = new()
            {
                {"@story_id", new MySQLParameter(story_id, MySqlDbType.VarChar)}
            };

            string sql = @"
                SELECT node_id, place_id, story_id
                FROM md_story_node
                WHERE story_id = @story_id AND is_active = 1
                ORDER BY node_order";

            return mysql_connect.GetDataList<StoryNodeRef>(sql, param) ?? new List<StoryNodeRef>();
        }

        /// <summary>
        /// 由 node_id 反查其 place_id 與 story_id，供單一節點生成任務使用。
        /// </summary>
        public StoryNodeRef GetNodeRef(string node_id)
        {
            Hashtable param = new()
            {
                {"@node_id", new MySQLParameter(node_id, MySqlDbType.VarChar)}
            };

            string sql = @"
                SELECT node_id, place_id, story_id
                FROM md_story_node
                WHERE node_id = @node_id
                LIMIT 1";

            var rows = mysql_connect.GetDataList<StoryNodeRef>(sql, param);
            return rows is { Count: > 0 } ? rows[0] : null;
        }

        /// <summary>
        /// 查詢特定節點下的所有任務，若已完成則不再顯示。
        /// </summary>
        public List<TaskDetailResponse> GetTasksByNodeId(string node_id)
        {
            Hashtable param = new()
            {
                {"@node_id", new MySQLParameter(node_id, MySqlDbType.VarChar)}
            };

            string sql = @"
                SELECT
                    t.task_id, t.story_id, t.node_id, t.task_place_id,
                    t.task_type AS type_id, ty.type_name AS task_type, t.task_describe,
                    t.task_describe_b, t.correct_answer
                FROM md_task t
                INNER JOIN md_type ty ON ty.type_id = t.task_type
                WHERE t.node_id = @node_id";

            var tasks = mysql_connect.GetDataList<TaskDetailResponse>(sql, param) ?? new List<TaskDetailResponse>();

            foreach (var task in tasks)
            {
                task.options = GetTaskOptions(task.task_id);
                task.media_urls = GetTaskMedia(task.task_id);
            }

            return tasks;
        }

        public TaskDetailResponse GetTaskDetail(int task_id)
        {
            Hashtable param = new()
            {
                {"@task_id", new MySQLParameter(task_id, MySqlDbType.Int32)}
            };

            string sql = @"
                SELECT
                    t.task_id, t.story_id, t.node_id, t.task_place_id,
                    t.task_type AS type_id, ty.type_name AS task_type, t.task_describe,
                    t.task_describe_b, t.correct_answer
                FROM md_task t
                INNER JOIN md_type ty ON ty.type_id = t.task_type
                WHERE t.task_id = @task_id
                LIMIT 1";

            var rows = mysql_connect.GetDataList<TaskDetailResponse>(sql, param);
            if (rows == null || rows.Count == 0)
                throw new KeyNotFoundException($"找不到 task_id={task_id} 的任務");

            var task = rows[0];
            task.options = GetTaskOptions(task.task_id);
            task.media_urls = GetTaskMedia(task.task_id);
            return task;
        }

        private List<TaskOption> GetTaskOptions(int task_id)
        {
            Hashtable param = new()
            {
                {"@task_id", new MySQLParameter(task_id, MySqlDbType.Int32)}
            };

            string sql = @"
                SELECT option_key, option_context AS option_text, option_url, is_correct
                FROM md_option
                WHERE task_id = @task_id
                ORDER BY option_id";

            return mysql_connect.GetDataList<TaskOption>(sql, param) ?? new List<TaskOption>();
        }

        private List<string> GetTaskMedia(int task_id)
        {
            Hashtable param = new()
            {
                {"@task_id", new MySQLParameter(task_id, MySqlDbType.Int32)}
            };

            string sql = "SELECT media_url FROM md_task_media WHERE task_id = @task_id";
            var rows = mysql_connect.GetDataList<TaskMediaRow>(sql, param);
            return rows?.Select(r => r.media_url).ToList() ?? new List<string>();
        }

        private class TaskMediaRow { public string media_url { get; set; } }

        public string GetTypeName(int typeId)
        {
            Hashtable param = new Hashtable { {"@type_id", new MySQLParameter(typeId, MySqlDbType.Int32)} };
            string sql = "SELECT type_name FROM md_type WHERE type_id = @type_id";
            var rows = mysql_connect.GetDataList<TypeNameRow>(sql, param);
            return rows != null && rows.Count > 0 ? rows[0].type_name : typeId.ToString();
        }
        private class TypeNameRow { public string type_name { get; set; } }

        public int InsertTask(TaskDetailResponse task, int typeId)
        {
            Hashtable param = new()
            {
                {"@story_id",     new MySQLParameter(task.story_id    ?? (object)DBNull.Value, MySqlDbType.VarChar)},
                {"@node_id",      new MySQLParameter(task.node_id,     MySqlDbType.VarChar)},
                {"@task_type",    new MySQLParameter(typeId,           MySqlDbType.Int32)},
                {"@task_describe",new MySQLParameter(task.task_describe ?? "", MySqlDbType.Text)},
                {"@task_place_id",new MySQLParameter(task.task_place_id, MySqlDbType.VarChar)},
                // 協作解謎型（type_id=5）專用，其他題型一律為 NULL
                {"@task_describe_b", new MySQLParameter(string.IsNullOrEmpty(task.task_describe_b) ? (object)DBNull.Value : task.task_describe_b, MySqlDbType.Text)},
                {"@correct_answer",  new MySQLParameter(string.IsNullOrEmpty(task.correct_answer)  ? (object)DBNull.Value : task.correct_answer,  MySqlDbType.VarChar)}
            };

            string sql = @"
                INSERT INTO md_task
                (story_id, node_id, task_type, task_describe, task_place_id, task_describe_b, correct_answer)
                VALUES
                (@story_id, @node_id, @task_type, @task_describe, @task_place_id, @task_describe_b, @correct_answer);
                SELECT LAST_INSERT_ID() AS last_id;";

            var idRows = mysql_connect.GetDataList<LastIdRow>(sql, param);
            return idRows is { Count: > 0 } ? idRows[0].last_id : 0;
        }

        private class LastIdRow { public int last_id { get; set; } }

        public void InsertTaskOptions(int task_id, List<TaskOption> options)
        {
            if (options == null || options.Count == 0) return;

            foreach (var opt in options)
            {
                Hashtable param = new()
                {
                    {"@task_id",        new MySQLParameter(task_id,          MySqlDbType.Int32)},
                    {"@option_key",     new MySQLParameter(opt.option_key ?? (object)DBNull.Value, MySqlDbType.VarChar)},
                    {"@option_context", new MySQLParameter(opt.option_text ?? "", MySqlDbType.VarChar)},
                    {"@option_url", new MySQLParameter(opt.option_url ?? (object)DBNull.Value, MySqlDbType.VarChar)},
                    {"@is_correct",     new MySQLParameter(opt.is_correct ? 1 : 0, MySqlDbType.Int32)}
                };

                string sql = @"
                    INSERT INTO md_option (task_id, option_key, option_context, option_url, is_correct)
                    VALUES (@task_id, @option_key, @option_context, @option_url, @is_correct)";

                mysql_connect.Execute(sql, param);
            }
        }

        public void InsertTaskMedia(int task_id, string ep_id, List<string> mediaUrls)
        {
            if (mediaUrls == null || mediaUrls.Count == 0) return;

            foreach (var url in mediaUrls)
            {
                Hashtable param = new()
                {
                    {"@task_id",   new MySQLParameter(task_id, MySqlDbType.Int32)},
                    {"@ep_id",     new MySQLParameter(ep_id ?? (object)DBNull.Value, MySqlDbType.VarChar)},
                    {"@media_url", new MySQLParameter(url, MySqlDbType.VarChar)}
                };

                string sql = @"
                    INSERT INTO md_task_media (task_id, ep_id, media_url)
                    VALUES (@task_id, @ep_id, @media_url)";

                mysql_connect.Execute(sql, param);
            }
        }

        public bool IsLastNodeInStory(string story_id, string node_id)
        {
            Hashtable param = new()
            {
                {"@story_id", new MySQLParameter(story_id, MySqlDbType.VarChar)}
            };

            string sql = @"
                SELECT node_id 
                FROM md_story_node 
                WHERE story_id = @story_id 
                ORDER BY node_order DESC 
                LIMIT 1";

            var rows = mysql_connect.GetDataList<LastNodeRow>(sql, param);
            if (rows != null && rows.Count > 0)
            {
                return rows[0].node_id == node_id;
            }
            return false;
        }

        private class LastNodeRow { public string node_id { get; set; } }

        #endregion
    }
}
