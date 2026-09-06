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
    /// 隞餃?蝑??賊?鞈?摮?撅扎?
    /// ?芾?鞎祆閰?撖怠鞈?摨恬?銝?隞颱?璆剖??斗嚗撠?獢??摩??Services/TaskVerificationService嚗?
    /// </summary>
    public class TaskDao(IOptions<AppSettings> app_settings_obj, Neo4jService neo4j_service_obj)
    {
        private readonly HttpContext _ipContext;
        private readonly MysqlConnect mysql_connect = new(app_settings_obj.Value.mydb);
        private readonly Neo4jService neo4j_service = neo4j_service_obj;


        #region ??隞餃?閰單?

        /// <summary>
        /// 靘?task_id ?亥岷隞餃?摰?批捆嚗???皞 md_task??
        /// </summary>

        public async Task<Location> GetPlaceLocation(string node_id)
        {
            // 1. MySQL ?亥岷 place_id
            Hashtable param = new()
            {
                {"@node_id", new MySQLParameter(node_id, MySqlDbType.VarChar)}
            };

            string sql = @"
                        SELECT place_id, story_id FROM md_story_node
                        WHERE node_id = @node_id";

            List<SearchNeo4jReq> mysqlData = mysql_connect.GetDataList<SearchNeo4jReq>(sql, param);

            // ?脣?嚗???MySQL ?曆??啗???? place_id ?箇征嚗??湔? null
            if (mysqlData == null || mysqlData.Count == 0 || string.IsNullOrEmpty(mysqlData[0].place_id))
            {
                return null;
            }

            string targetPlaceId = mysqlData[0].place_id;
            string targetStoryId = mysqlData[0].story_id;

            // 2. 瑽遣 Neo4j ??Cypher ?亥岷嚗????舫?摨扳?嚗?
            string cypher = @"
                            MATCH (n)
                            WHERE (n:Attraction OR n:Event OR n:Hotel OR n:Restaurant)
                            AND (elementId(n) = $id OR n.id = $id OR n.EventID = $id)
                            RETURN coalesce(n.lat, n.PositionLat) AS Lat,
                                coalesce(n.lon, n.PositionLon) AS Lon
                            LIMIT 1";

            // 3. ?澆 Neo4jService ?瑁??亥岷
            var locationResult = await neo4j_service.ExecuteCypherAsync<List<Location>>(
                cypher,
                new { id = targetPlaceId }
            );

            // 4. 閫???蝯?嚗蒂鋆? place_id ??story_id 靘?蝥蝙??
            if (locationResult != null && locationResult.Count > 0)
            {
                locationResult[0].PlaceId = targetPlaceId;
                locationResult[0].StoryId = targetStoryId;
                return locationResult[0];
            }

            // ??銝蝯?嚗?憒???API 404 ??嚗??喳??怠?摨扳???Location 霈葫閰西???
            return new Location { PlaceId = targetPlaceId, StoryId = targetStoryId, Lat = 25.0456, Lon = 121.5123 };
        }
        #endregion


        #region ???內

        /// <summary>
        /// 靘??舀活?詨?敺???畾萇??內嚗???皞 md_task_hint??
        /// trigger_wrong_count 頞??int_stage 頞之隞?”?內頞?蝣綽??泵??隞嗡葉??Ⅱ??蝑?
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
                hint_text = hintText ?? "?????唳?蝷箄圾??蝑甈⊥",
                is_available = hintText != null
            };
        }

        private class HintTextRow
        {
            public string hint_text { get; set; }
        }

        #endregion

        #region ????漲

        /// <summary>
        /// 閮??拙振?刻府?啣??赤甈⊥嚗蒂靘?md_difficulty_prompt ??瑼餉?矽?湧摨行?蝑?
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

        /// <summary>靘摨行?蝑?敺?銝策 LLM ?摰?蝷箏?璅⊥嚗???皞 md_difficulty_prompt??/summary>
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

        #region ???賢?

        /// <summary>
        /// 摰?蝭暺?靘?md_badge_pool ??????噬蝡?銝血神??ep_badge嚗?銴????嚗?
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

        #region ?梯??

        /// <summary>
        /// 靘摰嗥??GPS 摨扳?瑼Ｘ?臬閫貊閰脣?撠閫??????∴?閫貊敺神??ep_hidden_level_unlock??
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
        #region ?拙振隞餃?蝑?蝝??

        /// <summary>
        /// ?????Ｗ?冽?摰遙??歇蝑?活?詻?
        /// ?交?∪??芷?憪迨隞餃?嚗?? 0??
        /// </summary>
        /// <param name="epId">?Ｗ隞????/param>
        /// <param name="taskId">隞餃?隞????/param>
        /// <returns>蝝舐?蝑甈⊥??/returns>
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
        /// ?拙振蝑?敞蝛隤斗活?詻?
        /// ?仿?甈∠?憿??啣?蝝???亙歇???? wrong_count ????
        /// </summary>
        /// <param name="epId">?Ｗ隞????/param>
        /// <param name="taskId">隞餃?隞????/param>
        /// <param name="storyId">?撅砍??砌誨??/param>
        /// <param name="nodeId">?撅祉?暺誨??/param>
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
        /// ?拙振蝑??遣蝡??湔隞餃?摰?蝝??
        /// 撌脩敞蝛??航炊甈⊥????靘?蝥摰嗉??箏??蝙?具?
        /// </summary>
        /// <param name="epId">?Ｗ隞????/param>
        /// <param name="taskId">隞餃?隞????/param>
        /// <param name="storyId">?撅砍??砌誨??/param>
        /// <param name="nodeId">?撅祉?暺誨??/param>
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

        #region 隞餃?憿??亥岷

        /// <summary>
        /// ?亥岷???舫??舀???遙????JOIN md_place_type + md_type嚗?
        /// </summary>
        public List<PlaceTypeInfo> GetPlaceTypes(string place_id)
        {
            Hashtable param = new()
            {
                {"@place_id", new MySQLParameter(place_id, MySqlDbType.VarChar)}
            };

            string sql = @"
                SELECT pt.type_id, pt.place_category, t.type_name
                FROM md_place_type pt
                INNER JOIN md_type t ON t.type_id = pt.type_id
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

        #region 隞餃??亥岷?神??

        /// <summary>
        /// ?亥岷??蝭暺?血歇摮隞餃?嚗撌脩?????亙??喉?銝?銴???
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
                    t.task_type AS type_id, ty.type_name AS task_type, t.task_describe
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
                    t.task_type AS type_id, ty.type_name AS task_type, t.task_describe
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
                SELECT option_id AS option_key, option_context AS option_text, option_url, is_correct
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
                {"@task_place_id",new MySQLParameter(task.task_place_id, MySqlDbType.VarChar)}
            };

            string sql = @"
                INSERT INTO md_task
                (story_id, node_id, task_type, task_describe, task_place_id)
                VALUES
                (@story_id, @node_id, @task_type, @task_describe, @task_place_id);
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
                    {"@option_context", new MySQLParameter(opt.option_text ?? "", MySqlDbType.VarChar)},
                    {"@option_url", new MySQLParameter(opt.option_url ?? (object)DBNull.Value, MySqlDbType.VarChar)},
                    {"@is_correct",     new MySQLParameter(opt.is_correct ? 1 : 0, MySqlDbType.Int32)}
                };

                string sql = @"
                    INSERT INTO md_option (task_id, option_context, option_url, is_correct)
                    VALUES (@task_id, @option_context, @option_url, @is_correct)";

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





