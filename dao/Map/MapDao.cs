// 檔案路徑：System\dao\Map\MapDao.cs
// 對應新資料表 `story_node`、`place`、`story_session`、`postcard`、`story`、`task`，
// 取代舊的 md_story_node / md_place / ep_story_progress / ep_postcard / md_postcard / md_task。
//
// ⚠️ 兩個必須注意的新舊差異：
// 1. story_node.is_active 的語意變成「是否解鎖(1=是、2=否)」，不是舊的「資料是否啟用」。
//    預設值是 2，所以絕對不能再沿用舊的 `WHERE is_active = 1` 過濾，否則會一筆節點都查不到。
//    解鎖與否一律由 story_session.ss_current 計算。
// 2. story_node.place_id 存的是 Neo4j UUID。節點座標一律由 MapService 透過 PlaceLookupService 向 Neo4j 查
//    （抵達、作答、導航共用同一份座標），這裡只帶出 place_id。
//    圖片、介紹、營業時間仍用 place_type（UUID + 景點名稱）→ place 的名稱橋接（見 待修改問題.md）。
using System.Collections.Generic;
using System.Linq;
using Dapper;
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

        // story_node(Neo4j UUID) -> place_type(UUID + 名稱) -> place(名稱) 的暫時橋接。
        // place_type 每個景點會有多筆（一種任務類型一筆），place 也可能有同名多筆，
        // 兩邊都先收斂成一筆再 JOIN，避免節點被重複列出（同 VisitorVlogDao）。
        internal const string PlaceBridgeJoin = @"
            LEFT JOIN (SELECT place_id, MIN(place_name) AS place_name FROM place_type GROUP BY place_id) pt
                   ON pt.place_id = sn.place_id
            LEFT JOIN place p
                   ON p.p_id = (SELECT MIN(p2.p_id) FROM place p2 WHERE p2.p_name = pt.place_name)
        ";

        /// <summary>預設 NPC（薯光）的代號：劇本沒有指定 NPC 時用這個</summary>
        internal const string DefaultNpcIdSql = "(SELECT npc_id FROM npc WHERE is_default = 1 ORDER BY npc_id LIMIT 1)";

        // 節點的 NPC：節點自己有設定就用節點的，沒有就用劇本的 NPC，都沒有（舊劇本、AI 沒挑）就用預設 NPC
        internal const string NpcJoin = $@"
            LEFT JOIN story s ON s.s_id = sn.s_id
            LEFT JOIN npc n ON n.npc_id = COALESCE(sn.npc_id, s.npc_id, {DefaultNpcIdSql})
        ";

        /// <summary>npc 表沒有任何 NPC（還沒匯入名單）時，畫面上顯示的 NPC 名稱</summary>
        public const string DefaultNpcName = "旅遊引導員";

        #region 取得地圖節點
        /// <summary>
        /// 取得指定劇本的全部地圖節點。解鎖狀態由 MapService 依目前這一關（GetStageOrder）統一計算，
        /// 座標、景點照片與迷霧圖都由 MapService 依 place_id 補上（座標與照片向 Neo4j 查，見 PlaceLookupService）。
        /// </summary>
        public List<MapNode> GetStoryNodes(int storyId)
        {
            string sql = $@"
                SELECT
                    sn.sn_id                     AS node_id,
                    sn.sn_order                  AS node_order,
                    1                            AS day_index,
                    sn.location_codename         AS fog_hint,
                    (sn.is_night_only = 1)       AS is_night_only,
                    sn.sn_title                  AS location_name,
                    COALESCE(p.p_name, pt.place_name) AS place_name,
                    sn.place_id                  AS place_id,
                    (EXISTS (SELECT 1 FROM task t WHERE t.node_id = sn.sn_id)
                     AND NOT EXISTS (SELECT 1 FROM task t WHERE t.node_id = sn.sn_id AND t.pass = 0)) AS is_completed
                FROM story_node sn
                {PlaceBridgeJoin}
                WHERE sn.s_id = @storyId
                ORDER BY sn.sn_order;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();

                List<MapNode> nodes = conn.Query<MapNode>(sql, new { storyId }).ToList();

                foreach (MapNode node in nodes)
                {
                    node.child_node_ids = new List<int>();
                }

                return nodes;
            }
        }

        /// <summary>
        /// 這些景點照片各自的迷霧圖：照片網址 → 迷霧圖路徑（還沒做好的不會出現）。
        /// fog 表用照片網址的 SHA-1 當 key（FogService.SourceKey，跟做迷霧圖時用的是同一個算法）。
        /// SHA-1 在程式裡算，不用 SQL 的 SHA1()：有些 MySQL 相容的資料庫沒有這個函式，整支地圖 API 會回 400。
        /// </summary>
        public Dictionary<string, string> GetFogImages(IEnumerable<string> photoUrls)
        {
            Dictionary<string, string> urlByKey = (photoUrls ?? Enumerable.Empty<string>())
                .Where(url => !string.IsNullOrEmpty(url))
                .Distinct()
                .ToDictionary(Services.FogService.SourceKey, url => url);

            if (urlByKey.Count == 0) return new Dictionary<string, string>();

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();

                return conn.Query<(string fog_source_key, string fog_image)>(
                        "SELECT fog_source_key, fog_image FROM fog WHERE fog_source_key IN @keys;", new { keys = urlByKey.Keys.ToList() })
                    .Where(row => row.fog_source_key != null && urlByKey.ContainsKey(row.fog_source_key))
                    .GroupBy(row => row.fog_source_key)
                    .ToDictionary(g => urlByKey[g.Key], g => g.First().fog_image);
            }
        }
        #endregion

        #region 取得玩家目前節點進度
        /// <summary>登入者（有協作隊伍時為全隊共用）最遠抵達的節點順序，見 TeamProgress。</summary>
        public int GetArrivedOrder(int auId, int storyId)
        {
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return TeamProgress.GetCurrentNodeOrder(conn, auId, storyId);
            }
        }

        /// <summary>目前這一關的站序（破完一站才開下一站），這一關和之前的站都是解鎖的，見 TeamProgress.GetStageOrder。</summary>
        public int GetStageOrder(int auId, int storyId)
        {
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return TeamProgress.GetStageOrder(conn, auId, storyId);
            }
        }

        /// <summary>登入者能不能玩這份劇本（劇本擁有者或協作隊伍成員），見 TeamProgress.CanPlay。</summary>
        public bool CanPlay(int auId, int storyId)
        {
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return TeamProgress.CanPlay(conn, auId, storyId);
            }
        }

        /// <summary>節點所屬劇本（story_node.s_id），找不到節點時回傳 null。</summary>
        public int? GetNodeStoryId(int nodeId)
        {
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.ExecuteScalar<int?>("SELECT s_id FROM story_node WHERE sn_id = @nodeId;", new { nodeId });
            }
        }

        /// <summary>
        /// 節點的站序與玩家在該劇本的進度，判斷這一站是否還在迷霧中用；找不到節點回傳 null。
        /// 進度用 TeamProgress（有協作隊伍時全隊共用，跟地圖解鎖、抵達、任務門檻一致）。
        /// stage_order 是目前這一關的站序，站序不超過它的站才是解鎖的。
        /// </summary>
        public NodeProgress GetNodeProgress(int auId, int nodeId)
        {
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();

                NodeProgress progress = conn.QueryFirstOrDefault<NodeProgress>(@"
                    SELECT sn.s_id AS story_id, sn.sn_order AS node_order
                    FROM story_node sn
                    WHERE sn.sn_id = @nodeId;", new { nodeId });

                if (progress != null)
                {
                    progress.stage_order = TeamProgress.GetStageOrder(conn, auId, progress.story_id);
                    progress.arrived_order = TeamProgress.GetCurrentNodeOrder(conn, auId, progress.story_id);
                }

                return progress;
            }
        }

        public class NodeProgress
        {
            public int story_id { get; set; }
            public int node_order { get; set; }
            public int stage_order { get; set; }

            /// <summary>隊伍最遠抵達第幾站；站序不超過它的站才算抵達過</summary>
            public int arrived_order { get; set; }
        }
        #endregion

        #region 迷霧圖
        /// <summary>通用迷霧圖（fog 表 is_default = 1 的那張）：景點沒有照片、或迷霧圖還沒做好時用；沒有資料時回傳 null</summary>
        public string GetDefaultFogImage()
        {
            string sql = @"
                SELECT fog_image
                FROM fog
                WHERE is_default = 1
                ORDER BY fog_id
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.ExecuteScalar<string>(sql);
            }
        }
        #endregion

        #region 取得節點（抵達、導航用）
        /// <summary>節點基本資料與所屬劇本、place_id；座標由 MapService 依 place_id 向 Neo4j 查。</summary>
        public MapNode GetNodeLocation(int nodeId)
        {
            string sql = @"
                SELECT
                    sn.sn_id                     AS node_id,
                    sn.sn_order                  AS node_order,
                    1                            AS day_index,
                    (sn.is_night_only = 1)       AS is_night_only,
                    sn.sn_title                  AS location_name,
                    sn.place_id                  AS place_id
                FROM story_node sn
                WHERE sn.sn_id = @nodeId
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();

                MapNode node = conn.QueryFirstOrDefault<MapNode>(sql, new { nodeId });

                if (node != null)
                {
                    node.child_node_ids = new List<int>();
                }

                return node;
            }
        }
        #endregion

        #region 更新玩家節點進度
        /// <summary>
        /// 抵達節點後推進 story_session.ss_current，同時把節點本身標記為已解鎖。
        /// story_session 沒有 (au_id, s_id) 唯一鍵，因此用先更新、沒更新到才新增的方式處理。
        /// story_node.last_time 是「完成時間」，在這一站任務全部通過時才寫入（TaskDao.SaveAnswerRecord），抵達時不寫。
        /// </summary>
        public void UnlockNode(int auId, int nodeId)
        {
            string updateSessionSql = @"
                UPDATE story_session ss
                INNER JOIN story_node sn ON sn.sn_id = @nodeId
                SET ss.ss_current    = GREATEST(ss.ss_current, sn.sn_order),
                    ss.sn_id         = sn.sn_id,
                    ss.last_played_at = NOW()
                WHERE ss.au_id = @auId AND ss.s_id = sn.s_id;
            ";

            string insertSessionSql = @"
                INSERT INTO story_session (au_id, s_id, sn_id, ss_current, ss_status, started_at, last_played_at)
                SELECT @auId, sn.s_id, sn.sn_id, sn.sn_order, 'in_progress', NOW(), NOW()
                FROM story_node sn
                WHERE sn.sn_id = @nodeId;
            ";

            string unlockNodeSql = @"
                UPDATE story_node
                SET is_active = 1
                WHERE sn_id = @nodeId;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();

                int rows = conn.Execute(updateSessionSql, new { auId, nodeId });
                if (rows == 0)
                {
                    conn.Execute(insertSessionSql, new { auId, nodeId });
                }

                conn.Execute(unlockNodeSql, new { nodeId });
            }
        }
        #endregion

        #region 示範模式（設定 Demo:AllowStageSkip 打開才會被呼叫）
        /// <summary>
        /// 示範用破關：把這一站的任務全部標成通過並寫入完成時間；整份劇本的任務都通過時，遊玩紀錄標成完成。
        /// 不寫作答紀錄（user_task_record），所以不會有照片、對錯統計。回傳劇本是否全部完成。
        /// </summary>
        public bool DemoClearNode(int storyId, int nodeId)
        {
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                using (MySqlTransaction transaction = conn.BeginTransaction())
                {
                    conn.Execute("UPDATE task SET pass = 1 WHERE node_id = @nodeId;", new { nodeId }, transaction);
                    conn.Execute("UPDATE story_node SET last_time = COALESCE(last_time, NOW()) WHERE sn_id = @nodeId;", new { nodeId }, transaction);

                    // 每一站都抵達過、任務也都通過，才算整份劇本完成（跟正式作答的完成條件一致，多檢查站有沒有走完）
                    bool completed = conn.ExecuteScalar<int>(@"
                        SELECT (SELECT COUNT(*) FROM task WHERE story_id = @storyId AND pass = 0)
                             + (SELECT COUNT(*) FROM story_node sn
                                WHERE sn.s_id = @storyId
                                  AND sn.sn_order > (SELECT COALESCE(MAX(ss_current), 0) FROM story_session WHERE s_id = @storyId));",
                        new { storyId }, transaction) == 0;

                    if (completed)
                    {
                        conn.Execute(@"
                            UPDATE story_session
                            SET ss_status = 'completed', completed_at = COALESCE(completed_at, NOW())
                            WHERE s_id = @storyId AND ss_status <> 'completed';", new { storyId }, transaction);
                    }

                    transaction.Commit();
                    return completed;
                }
            }
        }

        /// <summary>
        /// 示範用重來：這份劇本回到第一關。任務改回未通過、每一站清掉完成時間、遊玩紀錄回到還沒抵達任何一站。
        /// 作答紀錄、明信片、勳章不動。
        /// </summary>
        public void DemoResetStory(int storyId)
        {
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                using (MySqlTransaction transaction = conn.BeginTransaction())
                {
                    conn.Execute("UPDATE task SET pass = 0 WHERE story_id = @storyId;", new { storyId }, transaction);
                    conn.Execute("UPDATE story_node SET last_time = NULL WHERE s_id = @storyId;", new { storyId }, transaction);
                    conn.Execute(@"
                        UPDATE story_session
                        SET ss_current = 0, sn_id = NULL, ss_status = 'in_progress', completed_at = NULL, last_played_at = NOW()
                        WHERE s_id = @storyId;", new { storyId }, transaction);
                    transaction.Commit();
                }
            }
        }
        #endregion

        #region 取得節點詳情
        /// <summary>
        /// 取得指定節點的景點、劇情與對應任務資訊。task 表沒有 is_active / difficulty_star，
        /// 同一節點有多題時取 task_id 最小的一筆。
        /// </summary>
        public NodeDetailResponse GetNodeDetail(int nodeId)
        {
            string sql = $@"
                SELECT
                    sn.sn_id           AS node_id,
                    sn.sn_title        AS location_name,
                    sn.sn_opening_text AS opening_text,
                    p.p_summary        AS summary,
                    p.p_introduction   AS introduction,
                    p.p_open_time      AS opening_hours,
                    n.npc_name         AS npc_name,
                    n.npc_avatar       AS npc_avatar,
                    MIN(t.task_id)     AS task_id
                FROM story_node sn
                {PlaceBridgeJoin}
                {NpcJoin}
                LEFT JOIN task t ON t.node_id = sn.sn_id
                WHERE sn.sn_id = @nodeId
                GROUP BY sn.sn_id, sn.sn_title, sn.sn_opening_text,
                         p.p_summary, p.p_introduction, p.p_open_time, n.npc_name, n.npc_avatar
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();

                var row = conn.QueryFirstOrDefault(sql, new { nodeId });

                if (row == null)
                {
                    return null;
                }

                string introStory = row.introduction as string;
                if (string.IsNullOrWhiteSpace(introStory)) introStory = row.opening_text as string;
                if (string.IsNullOrWhiteSpace(introStory)) introStory = row.summary as string;
                if (string.IsNullOrWhiteSpace(introStory)) introStory = "這個地點正等待你來探索。";

                return new NodeDetailResponse
                {
                    node_id = (int)row.node_id,
                    location_name = (row.location_name as string) ?? "",
                    npc_name = (row.npc_name as string) ?? DefaultNpcName,
                    npc_avatar_url = row.npc_avatar as string,
                    intro_story = introStory,
                    opening_hours = row.opening_hours as string,
                    nearby_food = new List<string>(),
                    task_id = (int?)row.task_id
                };
            }
        }
        #endregion

        #region 取得 NPC 互動
        /// <summary>
        /// NPC 畫面：NPC 名稱、身分、圖片、聲線來自 npc 表（劇本用了哪一位 NPC），台詞是這一站的開場白，
        /// 景點介紹與照片來自景點資料。劇本沒有指定 NPC 時用預設的薯光。
        /// 圖片回傳站內路徑，由 Controller 組成完整網址。
        /// </summary>
        /// <param name="arrived">
        /// 隊伍抵達這一站了沒。還沒抵達時不給景點副標題。
        /// 背景圖（景點照片或迷霧圖）由 MapService 補上：照片要向 Neo4j 查
        /// </param>
        public NpcInteractionResponse GetRandomNpcInteraction(int nodeId, bool arrived)
        {
            string sql = $@"
                SELECT
                    sn.sn_id           AS node_id,
                    n.npc_id           AS npc_id,
                    n.npc_name         AS npc_name,
                    n.npc_role         AS npc_role,
                    n.npc_avatar       AS npc_avatar,
                    n.npc_voice        AS npc_voice,
                    sn.sn_title        AS location_name,
                    sn.sn_opening_text AS opening_text,
                    p.p_summary        AS summary
                FROM story_node sn
                {PlaceBridgeJoin}
                {NpcJoin}
                WHERE sn.sn_id = @nodeId
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();

                var row = conn.QueryFirstOrDefault(sql, new { nodeId });

                if (row == null)
                {
                    return null;
                }

                string dialogue = row.opening_text as string;
                if (string.IsNullOrWhiteSpace(dialogue))
                {
                    dialogue = "這裡似乎藏著一段等待你發現的故事。";
                }

                return new NpcInteractionResponse
                {
                    node_id = (int)row.node_id,
                    location_name = (row.location_name as string) ?? "",
                    location_subtitle = arrived ? row.summary as string : null,
                    npc_id = row.npc_id == null ? "NPC-DEFAULT" : row.npc_id.ToString(),
                    npc_name = (row.npc_name as string) ?? DefaultNpcName,
                    npc_role = row.npc_role as string,
                    npc_avatar_url = row.npc_avatar as string,
                    npc_voice = row.npc_voice as string,
                    npc_dialogue = dialogue,
                    emotion = "normal",
                    skip_button_text = "稍後再說",
                    next_task_id = null
                };
            }
        }
        #endregion

        #region 取得明信片統計
        public int GetUnlockedPostcardCount(int auId, int storyId)
        {
            string sql = @"
                SELECT COUNT(*)
                FROM postcard
                WHERE au_id = @auId AND s_id = @storyId;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.ExecuteScalar<int>(sql, new { auId, storyId });
            }
        }

        /// <summary>
        /// 劇本預期可獲得的明信片數量。新資料庫沒有明信片主檔，
        /// 改讀 story.story_postcards 這個預期數量欄位。
        /// </summary>
        public int GetTotalPostcardCount(int storyId)
        {
            string sql = @"
                SELECT COALESCE(story_postcards, 0)
                FROM story
                WHERE s_id = @storyId;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.ExecuteScalar<int>(sql, new { storyId });
            }
        }
        #endregion
    }
}
