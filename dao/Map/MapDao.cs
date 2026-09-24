// 檔案路徑：System\dao\Map\MapDao.cs
// 對應新資料表 `story_node`、`place`、`story_session`、`postcard`、`story`、`task`，
// 取代舊的 md_story_node / md_place / ep_story_progress / ep_postcard / md_postcard / md_task。
//
// ⚠️ 兩個必須注意的新舊差異：
// 1. story_node.is_active 的語意變成「是否解鎖(1=是、2=否)」，不是舊的「資料是否啟用」。
//    預設值是 2，所以絕對不能再沿用舊的 `WHERE is_active = 1` 過濾，否則會一筆節點都查不到。
//    解鎖與否一律由 story_session.ss_current 計算。
// 2. story_node.place_id 存的是 Neo4j UUID，而 place 主表的主鍵是 int p_id，兩者無法直接 JOIN。
//    目前用 place_type（同時有 UUID 與景點名稱）當橋接取座標，找不到時經緯度為 0。
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
        private const string PlaceBridgeJoin = @"
            LEFT JOIN place_type pt ON pt.place_id = sn.place_id
            LEFT JOIN place p       ON p.p_name    = pt.place_name
        ";

        #region 取得地圖節點
        /// <summary>
        /// 取得指定劇本的全部地圖節點。解鎖狀態由 MapService 依 ss_current 統一計算。
        /// </summary>
        public List<MapNode> GetStoryNodes(int storyId)
        {
            string sql = $@"
                SELECT
                    sn.sn_id                     AS node_id,
                    sn.sn_order                  AS node_order,
                    1                            AS day_index,
                    sn.sn_hint                   AS fog_hint,
                    (sn.is_night_only = 1)       AS is_night_only,
                    sn.sn_title                  AS location_name,
                    COALESCE(p.p_latitude, 0)    AS lat,
                    COALESCE(p.p_longitude, 0)   AS lng,
                    p.p_image                    AS image_url
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
        #endregion

        #region 取得玩家目前節點進度
        public int GetCurrentNodeOrder(int auId, int storyId)
        {
            string sql = @"
                SELECT COALESCE(MAX(ss_current), 0)
                FROM story_session
                WHERE au_id = @auId AND s_id = @storyId;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.ExecuteScalar<int>(sql, new { auId, storyId });
            }
        }
        #endregion

        #region 取得節點座標
        public MapNode GetNodeLocation(int nodeId)
        {
            string sql = $@"
                SELECT
                    sn.sn_id                     AS node_id,
                    sn.sn_order                  AS node_order,
                    1                            AS day_index,
                    (sn.is_night_only = 1)       AS is_night_only,
                    sn.sn_title                  AS location_name,
                    COALESCE(p.p_latitude, 0)    AS lat,
                    COALESCE(p.p_longitude, 0)   AS lng
                FROM story_node sn
                {PlaceBridgeJoin}
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
                SET is_active = 1, last_time = NOW()
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
                    MIN(t.task_id)     AS task_id
                FROM story_node sn
                {PlaceBridgeJoin}
                LEFT JOIN task t ON t.node_id = sn.sn_id
                WHERE sn.sn_id = @nodeId
                GROUP BY sn.sn_id, sn.sn_title, sn.sn_opening_text,
                         p.p_summary, p.p_introduction, p.p_open_time
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
                    npc_name = "旅遊引導員",
                    intro_story = introStory,
                    opening_hours = row.opening_hours as string,
                    nearby_food = new List<string>(),
                    task_id = (int?)row.task_id,
                    review_story_url = null
                };
            }
        }
        #endregion

        #region 取得 NPC 互動
        /// <summary>
        /// 新資料庫沒有 NPC 主表（story_node.npc_id 只是一個裸的整數），
        /// 因此 NPC 畫面仍由節點與景點資料組出來。
        /// </summary>
        public NpcInteractionResponse GetRandomNpcInteraction(int nodeId)
        {
            string sql = $@"
                SELECT
                    sn.sn_id           AS node_id,
                    sn.npc_id          AS npc_id,
                    sn.sn_title        AS location_name,
                    sn.sn_opening_text AS opening_text,
                    p.p_summary        AS summary,
                    p.p_image          AS image_url
                FROM story_node sn
                {PlaceBridgeJoin}
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
                    location_subtitle = row.summary as string,
                    scene_image_url = row.image_url as string,
                    npc_id = row.npc_id == null ? "NPC-DEFAULT" : row.npc_id.ToString(),
                    npc_name = "旅遊引導員",
                    npc_avatar_url = null,
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

        #region 取得周邊好去
        /// <summary>
        /// 新資料庫的 story 沒有 region_id，place 的 region_id 又是 Neo4j UUID，
        /// 兩邊無法直接關聯，因此改用劇本的縣市／鄉鎮名稱比對景點地址。
        /// </summary>
        public List<NearbyPlaceResponse> GetNearbyPlaces(int storyId, string category)
        {
            string sql = @"
                SELECT
                    p.p_id        AS place_id,
                    p.p_category  AS category,
                    p.p_type      AS type,
                    p.p_name      AS name,
                    p.p_address   AS address,
                    p.p_open_time AS open_time,
                    p.p_image     AS image_url,
                    p.p_latitude  AS lat,
                    p.p_longitude AS lng
                FROM place p
                INNER JOIN story s ON s.s_id = @storyId
                WHERE (s.city_name IS NULL OR p.p_address LIKE CONCAT('%', s.city_name, '%'))
                  AND (@category = '' OR p.p_category = @category)
                ORDER BY p.p_name;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();

                return conn.Query(sql, new { storyId, category = category ?? "" })
                    .Select(r => new NearbyPlaceResponse
                    {
                        place_id = (int)r.place_id,
                        category = r.category as string,
                        type = r.type as string,
                        name = (r.name as string) ?? "",
                        address = r.address as string,
                        open_time = r.open_time as string,
                        photo_urls = string.IsNullOrWhiteSpace(r.image_url as string)
                            ? new List<string>()
                            : new List<string> { (string)r.image_url },
                        maps_deeplink_url = BuildMapsUrl(r.lat, r.lng),
                        lat = r.lat == null ? (double?)null : System.Convert.ToDouble(r.lat),
                        lng = r.lng == null ? (double?)null : System.Convert.ToDouble(r.lng)
                    })
                    .ToList();
            }
        }

        private static string BuildMapsUrl(object latValue, object lngValue)
        {
            if (latValue == null || lngValue == null) return null;

            double lat = System.Convert.ToDouble(latValue);
            double lng = System.Convert.ToDouble(lngValue);

            if (lat == 0 || lng == 0) return null;

            return $"https://www.google.com/maps/search/?api=1&query={lat},{lng}";
        }
        #endregion
    }
}
