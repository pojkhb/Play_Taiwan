using System;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dapper;
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
        private readonly string mydb = app_settings_obj.Value.mydb;
        private readonly Neo4jService neo4j_service = neo4j_service_obj;

        #region 查詢任務地點

        /// <summary>
        /// 節點對應的景點 uid（story_node.place_id），座標由 PlaceLookupService 向 Neo4j 查（與地圖抵達同一份座標）。
        /// </summary>
        public string GetNodePlaceId(int snId)
        {
            using var conn = new MySqlConnection(mydb);
            return conn.ExecuteScalar<string>("SELECT place_id FROM story_node WHERE sn_id = @snId;", new { snId });
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

        /// <summary>取得任務提示（task.task_hint），沒有提示時回傳 null。</summary>
        public string GetTaskHint(int taskId)
        {
            using var conn = new MySqlConnection(mydb);
            string hint = conn.ExecuteScalar<string>("SELECT task_hint FROM task WHERE task_id = @taskId;", new { taskId });
            return string.IsNullOrWhiteSpace(hint) ? null : hint;
        }

        /// <summary>任務的題型（task.task_type），任務不存在時回傳 0</summary>
        public int GetTaskTypeId(int taskId)
        {
            using var conn = new MySqlConnection(mydb);
            return conn.ExecuteScalar<int?>("SELECT task_type FROM task WHERE task_id = @taskId;", new { taskId }) ?? 0;
        }

        /// <summary>給提示的 NPC 圖片（站內路徑）：任務所在節點的 NPC，沒有指定時用預設 NPC</summary>
        public string GetTaskNpcAvatar(int taskId)
        {
            using var conn = new MySqlConnection(mydb);
            return conn.ExecuteScalar<string>($@"
                SELECT n.npc_avatar
                FROM task t
                JOIN story_node sn ON sn.sn_id = t.node_id
                {MapDao.NpcJoin}
                WHERE t.task_id = @taskId;", new { taskId });
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
        /// 取得玩家在指定任務中已答錯的次數（user_task_record.is_correct = 0 的筆數）。
        /// </summary>
        public int GetWrongCount(int auId, int taskId)
        {
            using var conn = new MySqlConnection(mydb);
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM user_task_record WHERE au_id = @auId AND task_id = @taskId AND is_correct = 0;",
                new { auId, taskId });
        }

        /// <summary>登入者與某個節點的關係（節點不存在時查詢回傳 null）</summary>
        public class PlayerNodeState
        {
            public int story_id { get; set; }
            public int owner_id { get; set; }
            public int node_order { get; set; }

            /// <summary>登入者在協作隊伍（未取消）中的座位，不在隊伍中為 null</summary>
            public int? seat_no { get; set; }

            /// <summary>登入者（有隊伍時為全隊共用）最遠抵達的節點順序，見 TeamProgress</summary>
            public int current_node_order { get; set; }
        }

        /// <summary>
        /// 查詢登入者在某個節點所屬劇本的身分、座位與隊伍進度。節點不存在時回傳 null。
        /// </summary>
        public PlayerNodeState GetPlayerNodeState(int auId, int snId)
        {
            using var conn = new MySqlConnection(mydb);
            conn.Open();

            PlayerNodeState state = conn.QueryFirstOrDefault<PlayerNodeState>(@"
                SELECT sn.s_id AS story_id,
                       s.au_id AS owner_id,
                       sn.sn_order AS node_order,
                       (SELECT pm.seat_no
                        FROM story_pair_member pm
                        INNER JOIN story_pair_session ps ON ps.pair_id = pm.pair_id
                        WHERE pm.au_id = @auId AND ps.s_id = sn.s_id AND ps.pair_status <> 'cancelled'
                        ORDER BY pm.pair_id DESC
                        LIMIT 1) AS seat_no
                FROM story_node sn
                INNER JOIN story s ON s.s_id = sn.s_id
                WHERE sn.sn_id = @snId;", new { auId, snId });

            if (state != null)
            {
                state.current_node_order = TeamProgress.GetCurrentNodeOrder(conn, auId, state.story_id);
            }

            return state;
        }

        /// <summary>節點遊玩畫面用的節點資料</summary>
        public class NodePlayRow
        {
            public int sn_id { get; set; }
            public int s_id { get; set; }
            public int sn_order { get; set; }
            public string sn_title { get; set; }
            public string location_codename { get; set; }
            public string sn_opening_text { get; set; }
            public string sn_success_text { get; set; }
        }

        public NodePlayRow GetNodePlayRow(int snId)
        {
            using var conn = new MySqlConnection(mydb);
            return conn.QueryFirstOrDefault<NodePlayRow>(@"
                SELECT sn_id, s_id, sn_order, sn_title, location_codename, sn_opening_text, sn_success_text
                FROM story_node
                WHERE sn_id = @snId;", new { snId });
        }

        /// <summary>多個任務各自的「登入者答錯次數」與「是否有提示」</summary>
        public Dictionary<int, (int wrongCount, bool hasHint)> GetTaskPlayerStats(int auId, List<int> taskIds)
        {
            if (taskIds == null || taskIds.Count == 0) return new Dictionary<int, (int, bool)>();

            using var conn = new MySqlConnection(mydb);
            return conn.Query<(int task_id, int wrong_count, int has_hint)>(@"
                SELECT t.task_id,
                       (SELECT COUNT(*) FROM user_task_record r
                        WHERE r.au_id = @auId AND r.task_id = t.task_id AND r.is_correct = 0) AS wrong_count,
                       (t.task_hint IS NOT NULL AND t.task_hint <> '') AS has_hint
                FROM task t
                WHERE t.task_id IN @taskIds;", new { auId, taskIds })
                .ToDictionary(r => r.task_id, r => (r.wrong_count, r.has_hint == 1));
        }

        /// <summary>
        /// 確認玩家能否作答此劇本的任務：劇本擁有者（story.au_id），或該劇本協作隊伍的成員（story_pair_member）。
        /// 回傳 (是否為擁有者, 所屬協作隊伍 pair_id)；兩者皆無代表玩家沒有參與這個劇本。
        /// </summary>
        public (bool isOwner, int? pairId) GetStoryAccess(int auId, int storyId)
        {
            using var conn = new MySqlConnection(mydb);
            conn.Open();

            bool isOwner = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM story WHERE s_id = @storyId AND au_id = @auId;",
                new { auId, storyId }) > 0;

            int? pairId = conn.ExecuteScalar<int?>(@"
                SELECT pm.pair_id
                FROM story_pair_member pm
                INNER JOIN story_pair_session ps ON ps.pair_id = pm.pair_id
                WHERE pm.au_id = @auId AND ps.s_id = @storyId AND ps.pair_status <> 'cancelled'
                ORDER BY pm.pair_id DESC
                LIMIT 1;", new { auId, storyId });

            return (isOwner, pairId);
        }

        /// <summary>作答寫入後的結果與進度</summary>
        public class AnswerSaveResult
        {
            public int attempt_no { get; set; }
            public NodeProgress node_progress { get; set; }
            public bool story_completed { get; set; }
        }

        /// <summary>一站的任務進度（task.pass，協作隊伍共用）</summary>
        public NodeProgress GetNodeProgress(int snId)
        {
            using var conn = new MySqlConnection(mydb);
            return GetNodeProgress(conn, null, snId);
        }

        private static NodeProgress GetNodeProgress(MySqlConnection conn, MySqlTransaction transaction, int snId)
        {
            var (total, passed) = conn.QueryFirst<(int total, int passed)>(@"
                SELECT COUNT(*) AS total, CAST(COALESCE(SUM(pass = 1), 0) AS SIGNED) AS passed
                FROM task
                WHERE node_id = @snId;", new { snId }, transaction);

            return new NodeProgress { total = total, passed = passed, all_passed = total > 0 && passed == total };
        }

        /// <summary>
        /// 寫入一次作答紀錄（user_task_record），答對時同一個交易：
        /// 1. 把 task.pass 設為 1（已通過）
        /// 2. 這一站任務全部通過 → 寫入 story_node.last_time（完成時間）
        /// 3. 整份劇本任務全部通過 → 劇本擁有者與協作隊員的遊玩紀錄標成完成、協作隊伍標成完成
        /// 第一個上傳素材存 answer_media_url，其餘存 record_media。
        /// user_task_record.record_id、record_media.media_id 沒有自動遞增，交易內鎖住後自行編號
        /// （同 StoryDao 寫 task_option.option_id 的做法）。
        /// </summary>
        /// <param name="isCorrect">1=正確、0=錯誤；null=人工審核中，尚無對錯判定</param>
        public AnswerSaveResult SaveAnswerRecord(int auId, int taskId, int storyId, int nodeId, int? pairId,
            string answerContent, List<string> mediaUrls, bool? isCorrect)
        {
            mediaUrls ??= new List<string>();

            using var conn = new MySqlConnection(mydb);
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                int recordId = conn.ExecuteScalar<int>(
                    "SELECT COALESCE(MAX(record_id), 0) + 1 FROM user_task_record FOR UPDATE;",
                    transaction: transaction);

                int attemptNo = conn.ExecuteScalar<int>(
                    "SELECT COUNT(*) + 1 FROM user_task_record WHERE au_id = @auId AND task_id = @taskId;",
                    new { auId, taskId }, transaction);

                conn.Execute(@"
                    INSERT INTO user_task_record
                        (record_id, au_id, task_id, pair_id, answer_content, answer_media_url, is_correct, attempt_no)
                    VALUES
                        (@recordId, @auId, @taskId, @pairId, @answerContent, @answerMediaUrl, @isCorrect, @attemptNo);",
                    new
                    {
                        recordId,
                        auId,
                        taskId,
                        pairId,
                        answerContent,
                        answerMediaUrl = mediaUrls.FirstOrDefault(),
                        isCorrect = isCorrect.HasValue ? (isCorrect.Value ? 1 : 0) : (int?)null,
                        attemptNo
                    }, transaction);

                if (mediaUrls.Count > 1)
                {
                    int mediaId = conn.ExecuteScalar<int>(
                        "SELECT COALESCE(MAX(media_id), 0) FROM record_media FOR UPDATE;",
                        transaction: transaction);

                    foreach (string url in mediaUrls.Skip(1))
                    {
                        conn.Execute(
                            "INSERT INTO record_media (media_id, record_id, media_url) VALUES (@mediaId, @recordId, @url);",
                            new { mediaId = ++mediaId, recordId, url }, transaction);
                    }
                }

                bool storyCompleted = false;

                if (isCorrect == true)
                {
                    conn.Execute("UPDATE task SET pass = 1 WHERE task_id = @taskId;", new { taskId }, transaction);

                    if (GetNodeProgress(conn, transaction, nodeId).all_passed)
                    {
                        conn.Execute(
                            "UPDATE story_node SET last_time = COALESCE(last_time, NOW()) WHERE sn_id = @nodeId;",
                            new { nodeId }, transaction);
                    }

                    int remaining = conn.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM task WHERE story_id = @storyId AND pass = 0;", new { storyId }, transaction);

                    if (remaining == 0)
                    {
                        CompleteStory(conn, transaction, storyId);
                        storyCompleted = true;
                    }
                }

                NodeProgress progress = GetNodeProgress(conn, transaction, nodeId);

                transaction.Commit();
                return new AnswerSaveResult { attempt_no = attemptNo, node_progress = progress, story_completed = storyCompleted };
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// 劇本全部任務通過：劇本擁有者與協作隊員（未取消隊伍）的遊玩紀錄標成完成，協作隊伍標成完成。
        /// 沒有遊玩紀錄（沒按過確認開始、也沒抵達過）的人不補建。
        /// </summary>
        private static void CompleteStory(MySqlConnection conn, MySqlTransaction transaction, int storyId)
        {
            conn.Execute(@"
                UPDATE story_session
                SET ss_status = 'completed', completed_at = COALESCE(completed_at, NOW())
                WHERE s_id = @storyId
                  AND ss_status <> 'completed'
                  AND au_id IN (
                      SELECT au_id FROM story WHERE s_id = @storyId
                      UNION
                      SELECT pm.au_id
                      FROM story_pair_member pm
                      INNER JOIN story_pair_session ps ON ps.pair_id = pm.pair_id
                      WHERE ps.s_id = @storyId AND ps.pair_status <> 'cancelled');", new { storyId }, transaction);

            conn.Execute(@"
                UPDATE story_pair_session
                SET pair_status = 'completed', completed_at = NOW()
                WHERE s_id = @storyId AND pair_status IN ('waiting', 'locked', 'expired');", new { storyId }, transaction);
        }

        #endregion

        #region 劇本內容查詢（供 AI 生成使用）

        /// <summary>
        /// 組合 AI 任務生成所需的景點劇本內容（story_content）。
        /// 取自：
        ///   - story.story_prologue：劇本前傳
        ///   - story_node.location_codename：節點地點代號（原 fog_hint，v5 已移除 sn_hint）
        ///   - placeIntroduction：景點介紹（呼叫端向 Neo4j 查，見 PlaceLookupService）
        /// 欄位皆可為 NULL，僅取有值的部分組合成段落回傳。
        /// </summary>
        /// <param name="node_id">節點代號（story_node.sn_id）</param>
        /// <param name="placeIntroduction">景點介紹，沒有時傳 null</param>
        /// <returns>組合後的景點劇本說明文字，供 AiTaskRequest.story_content 使用</returns>
        public string GetStoryContent(string node_id, string placeIntroduction)
        {
            if (!int.TryParse(node_id, out int snId))
                return string.Empty;

            string sql = @"
                SELECT
                    s.story_prologue     AS story_prologue,
                    sn.location_codename AS node_fog_hint,
                    pt.place_name        AS place_name
                FROM story_node sn
                INNER JOIN story s ON s.s_id = sn.s_id
                LEFT JOIN (SELECT place_id, MIN(place_name) AS place_name FROM place_type GROUP BY place_id) pt
                       ON pt.place_id = sn.place_id
                WHERE sn.sn_id = @snId
                LIMIT 1;";

            StoryContentRow r;
            using (var conn = new MySqlConnection(mydb))
            {
                r = conn.QueryFirstOrDefault<StoryContentRow>(sql, new { snId });
            }

            if (r == null)
                return string.Empty;

            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(r.place_name))
                parts.Add($"【景點】{r.place_name}");

            if (!string.IsNullOrWhiteSpace(placeIntroduction))
                parts.Add($"【景點介紹】{placeIntroduction}");

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
            public string place_name        { get; set; }
        }

        #endregion

        #region 任務類型查詢

        /// <summary>
        /// 查詢特定景點可出的所有任務類型，來源 place_type JOIN type。
        /// place_category 為 Attraction / Event / Hotel / Restaurant。
        /// </summary>
        public List<PlaceTypeInfo> GetPlaceTypes(string place_id)
        {
            string sql = @"
                SELECT pt.type_id, pt.place_category, t.type_name
                FROM place_type pt
                INNER JOIN `type` t ON t.type_id = pt.type_id
                WHERE pt.place_id = @place_id;";

            using var conn = new MySqlConnection(mydb);
            return conn.Query<PlaceTypeInfo>(sql, new { place_id }).ToList();
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
        /// node_id = story_node.sn_id、story_id = story_node.s_id，沿用字串型別給 TaskGenerationService。
        /// </summary>
        public class StoryNodeRef
        {
            public string node_id  { get; set; }
            public string place_id { get; set; }
            public string story_id { get; set; }
        }

        /// <summary>
        /// 取得一份劇本底下的所有節點，供劇本生成後批次產生任務使用。
        /// story_node.is_active 在 v5 代表「是否解鎖」，不能拿來過濾。
        /// </summary>
        public List<StoryNodeRef> GetNodesByStoryId(string story_id)
        {
            if (!int.TryParse(story_id, out int sId))
                return new List<StoryNodeRef>();

            string sql = @"
                SELECT CAST(sn_id AS CHAR) AS node_id, place_id, CAST(s_id AS CHAR) AS story_id
                FROM story_node
                WHERE s_id = @sId
                ORDER BY sn_order;";

            using var conn = new MySqlConnection(mydb);
            return conn.Query<StoryNodeRef>(sql, new { sId }).ToList();
        }

        /// <summary>
        /// 由 node_id 反查其 place_id 與 story_id，供單一節點生成任務使用。
        /// </summary>
        public StoryNodeRef GetNodeRef(string node_id)
        {
            if (!int.TryParse(node_id, out int snId))
                return null;

            string sql = @"
                SELECT CAST(sn_id AS CHAR) AS node_id, place_id, CAST(s_id AS CHAR) AS story_id
                FROM story_node
                WHERE sn_id = @snId
                LIMIT 1;";

            using var conn = new MySqlConnection(mydb);
            return conn.QueryFirstOrDefault<StoryNodeRef>(sql, new { snId });
        }

        /// <summary>
        /// 查詢特定節點下的所有任務（含通關狀態 task.pass）。
        /// 協作解謎型另外帶出該座位（seat_no）在 task_clue 的線索，放在 clue_text。
        /// </summary>
        public List<TaskDetailResponse> GetTasksByNodeId(string node_id, int seat_no)
        {
            if (!int.TryParse(node_id, out int snId))
                return new List<TaskDetailResponse>();

            string sql = @"
                SELECT
                    t.task_id,
                    CAST(t.story_id AS CHAR) AS story_id,
                    CAST(t.node_id AS CHAR)  AS node_id,
                    sn.place_id              AS task_place_id,
                    t.task_type              AS type_id,
                    ty.type_name             AS task_type,
                    COALESCE(NULLIF(t.task_describe, ''), sq.question_describe) AS task_describe,
                    t.correct_answer,
                    t.pass,
                    tc.clue_text
                FROM task t
                LEFT JOIN `type` ty     ON ty.type_id = t.task_type
                LEFT JOIN store_question sq ON sq.question_id = t.question_id
                LEFT JOIN story_node sn ON sn.sn_id = t.node_id
                LEFT JOIN task_clue tc  ON tc.task_id = t.task_id AND tc.seat_no = @seat_no
                WHERE t.node_id = @snId
                ORDER BY t.task_id;";

            using var conn = new MySqlConnection(mydb);
            conn.Open();

            List<TaskDetailResponse> tasks = conn.Query<TaskDetailResponse>(sql, new { snId, seat_no }).ToList();
            Dictionary<int, List<TaskOption>> options = GetTaskOptions(conn, tasks.Select(t => t.task_id).ToList());

            foreach (var task in tasks)
            {
                task.options = options.TryGetValue(task.task_id, out var list) ? list : new List<TaskOption>();
                task.media_urls = new List<string>();
            }

            return tasks;
        }

        /// <summary>
        /// 取得作答用的任務內容：task + type + task_option，含目前的通關狀態（task.pass）。
        /// </summary>
        public TaskDetailResponse GetTaskDetail(int task_id)
        {
            string sql = @"
                SELECT
                    t.task_id,
                    CAST(t.story_id AS CHAR) AS story_id,
                    CAST(t.node_id AS CHAR)  AS node_id,
                    t.task_type              AS type_id,
                    ty.type_name             AS task_type,
                    COALESCE(NULLIF(t.task_describe, ''), sq.question_describe) AS task_describe,
                    t.correct_answer,
                    t.pass
                FROM task t
                LEFT JOIN `type` ty ON ty.type_id = t.task_type
                LEFT JOIN store_question sq ON sq.question_id = t.question_id
                WHERE t.task_id = @task_id
                LIMIT 1;";

            using var conn = new MySqlConnection(mydb);
            conn.Open();

            TaskDetailResponse task = conn.QueryFirstOrDefault<TaskDetailResponse>(sql, new { task_id });
            if (task == null)
                throw new KeyNotFoundException($"找不到 task_id={task_id} 的任務");

            task.options = GetTaskOptions(conn, new List<int> { task_id })
                .TryGetValue(task_id, out var list) ? list : new List<TaskOption>();
            task.media_urls = new List<string>();
            return task;
        }

        private class TaskOptionRow
        {
            public int task_id { get; set; }
            public string option_key { get; set; }
            public string option_text { get; set; }
            public string option_url { get; set; }
            public int is_correct { get; set; }
        }

        /// <summary>
        /// 一次取出多個任務的選項，依 task_id 分組：
        /// AI 生成的任務取 task_option；商家知識問答（task.question_id 有值）取商家題庫的 question_option。
        /// </summary>
        private static Dictionary<int, List<TaskOption>> GetTaskOptions(MySqlConnection conn, List<int> taskIds)
        {
            if (taskIds == null || taskIds.Count == 0)
                return new Dictionary<int, List<TaskOption>>();

            string sql = @"
                SELECT t.task_id, o.option_id AS sort_id, o.option_key, o.option_context AS option_text, o.option_url, o.is_correct
                FROM task t
                INNER JOIN task_option o ON o.task_id = t.task_id
                WHERE t.task_id IN @taskIds AND t.question_id IS NULL
                UNION ALL
                SELECT t.task_id, qo.option_id AS sort_id, qo.option_key, qo.option_context AS option_text, qo.option_url, qo.is_correct
                FROM task t
                INNER JOIN question_option qo ON qo.question_id = t.question_id
                WHERE t.task_id IN @taskIds
                ORDER BY task_id, sort_id;";

            return conn.Query<TaskOptionRow>(sql, new { taskIds })
                .GroupBy(o => o.task_id)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(o => new TaskOption
                    {
                        option_key = o.option_key,
                        option_text = o.option_text,
                        option_url = o.option_url,
                        is_correct = o.is_correct == 1
                    }).ToList());
        }

        public string GetTypeName(int typeId)
        {
            using var conn = new MySqlConnection(mydb);
            string name = conn.ExecuteScalar<string>("SELECT type_name FROM `type` WHERE type_id = @typeId;", new { typeId });
            return string.IsNullOrWhiteSpace(name) ? typeId.ToString() : name;
        }

        /// <summary>
        /// 寫入一筆任務（task）。協作解謎型的兩段線索寫入 task_clue：
        /// clue_text → 座位 1、task_describe_b → 座位 2（對應 story_pair_member.seat_no）。
        /// </summary>
        public int InsertTask(TaskDetailResponse task, int typeId)
        {
            int? storyId = int.TryParse(task.story_id, out int sId) ? sId : null;
            int? nodeId = int.TryParse(task.node_id, out int snId) ? snId : null;

            using var conn = new MySqlConnection(mydb);
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                int taskId = conn.ExecuteScalar<int>(@"
                    INSERT INTO task (story_id, node_id, task_type, task_describe, correct_answer)
                    VALUES (@storyId, @nodeId, @typeId, @describe, @answer);
                    SELECT LAST_INSERT_ID();",
                    new
                    {
                        storyId,
                        nodeId,
                        typeId,
                        describe = task.task_describe ?? "",
                        answer = string.IsNullOrEmpty(task.correct_answer) ? null : task.correct_answer
                    }, transaction);

                var clues = new[] { (seat: 1, text: task.clue_text), (seat: 2, text: task.task_describe_b) }
                    .Where(c => !string.IsNullOrEmpty(c.text));

                foreach (var (seat, text) in clues)
                {
                    conn.Execute(
                        "INSERT INTO task_clue (task_id, seat_no, clue_text) VALUES (@taskId, @seat, @text);",
                        new { taskId, seat, text }, transaction);
                }

                transaction.Commit();
                return taskId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// 寫入任務選項（task_option）。option_id 沒有自動遞增，交易內鎖住後自行編號（同 StoryDao）。
        /// </summary>
        public void InsertTaskOptions(int task_id, List<TaskOption> options)
        {
            if (options == null || options.Count == 0) return;

            using var conn = new MySqlConnection(mydb);
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                int optionId = conn.ExecuteScalar<int>(
                    "SELECT COALESCE(MAX(option_id), 0) FROM task_option FOR UPDATE;", transaction: transaction);

                foreach (var opt in options)
                {
                    conn.Execute(@"
                        INSERT INTO task_option (option_id, task_id, option_key, option_context, option_url, is_correct)
                        VALUES (@optionId, @task_id, @option_key, @option_context, @option_url, @is_correct);",
                        new
                        {
                            optionId = ++optionId,
                            task_id,
                            opt.option_key,
                            option_context = opt.option_text ?? "",
                            opt.option_url,
                            is_correct = opt.is_correct ? 1 : 0
                        }, transaction);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public bool IsLastNodeInStory(string story_id, string node_id)
        {
            if (!int.TryParse(story_id, out int sId) || !int.TryParse(node_id, out int snId))
                return false;

            using var conn = new MySqlConnection(mydb);
            int? lastSnId = conn.ExecuteScalar<int?>(
                "SELECT sn_id FROM story_node WHERE s_id = @sId ORDER BY sn_order DESC LIMIT 1;", new { sId });
            return lastSnId == snId;
        }

        #endregion
    }
}
