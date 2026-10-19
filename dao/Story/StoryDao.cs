// 檔案路徑：System\dao\Story\StoryDao.cs
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



        /// <summary>
        /// story_node(Neo4j UUID) → place_type(名稱) → place(照片)，跟 MapDao 的橋接方式相同，
        /// 劇本檔案館、劇本詳情、地圖顯示的都是同一張照片。
        /// </summary>
        private const string PlaceImageJoin = @"
            LEFT JOIN (SELECT place_id, MIN(place_name) AS place_name FROM place_type GROUP BY place_id) pt
                   ON pt.place_id = sn.place_id
            LEFT JOIN place p
                   ON p.p_id = (SELECT MIN(p2.p_id) FROM place p2 WHERE p2.p_name = pt.place_name)
        ";

        #region 劇本詳情 (包含對應節點輸出)
        public StoryDetailResponse GetDetail(int storyId)
        {
            // 劇本沒有指定 NPC（舊劇本、AI 沒挑）時用預設 NPC（薯光）。NPC 圖片回傳站內路徑，由 Controller 組成完整網址
            string storySql = $@"
                SELECT s.s_id, s.story_title, s.story_prologue, s.story_synopsis, s.is_night_mode, s.is_favorite,
                       n.npc_name, n.npc_role, n.npc_intro, n.npc_avatar
                FROM story s
                LEFT JOIN npc n ON n.npc_id = COALESCE(s.npc_id, {MapDao.DefaultNpcIdSql})
                WHERE s.s_id = @storyId AND s.is_active = 1;
            ";

            // 每一站的 NPC：節點自己有設定就用節點的，沒有就用劇本的 NPC，再沒有就用預設 NPC。
            // story_node 已移除 sn_hint，任務說明改取該節點第一筆任務的 task_describe。
            string nodeSql = $@"
                SELECT
                    sn.sn_id, sn.sn_order, sn.sn_title, sn.sn_task_type,
                    sn.location_codename, sn.sn_opening_text, sn.sn_success_text, sn.place_id,
                    (SELECT t.task_describe FROM task t WHERE t.node_id = sn.sn_id ORDER BY t.task_id LIMIT 1) AS task_describe,
                    n.npc_name
                FROM story_node sn
                {MapDao.NpcJoin}
                WHERE sn.s_id = @storyId
                ORDER BY sn.sn_order;
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
                    is_night_mode = Convert.ToInt32(story.is_night_mode) == 1,
                    is_favorite = Convert.ToInt32(story.is_favorite) == 1,
                    npc = story.npc_name == null ? null : new NpcDetail
                    {
                        name = story.npc_name as string,
                        role = story.npc_role as string,
                        intro = story.npc_intro as string,
                        avatar_url = story.npc_avatar as string
                    },
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
                        task_description = (node.task_describe as string) ?? "",
                        location_codename = (node.location_codename as string) ?? "",
                        opening = (node.sn_opening_text as string) ?? "",
                        success = (node.sn_success_text as string) ?? "",
                        npc_name = (node.npc_name as string) ?? "",
                        task_type = (node.sn_task_type as string) ?? "",
                        place_id = node.place_id as string   // 景點照片由 StoryService 依這個 ID 向 Neo4j 拿
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



        #region 加入行事曆
        public class StoryCalendarInfo
        {
            public string story_title { get; set; }
            public string story_synopsis { get; set; }
            public string story_prologue { get; set; }
            public string city_name { get; set; }
            public string district_name { get; set; }
            public int is_night_mode { get; set; }
            public int node_count { get; set; }
            public string first_place_name { get; set; }
            public string first_place_address { get; set; }
        }

        /// <summary>
        /// 行事曆要用的劇本資料。地點只帶第一站（集合點，本來就是開放的），其他站還在迷霧中不帶出。
        /// 找不到劇本回傳 null。
        /// </summary>
        public async Task<StoryCalendarInfo> GetCalendarInfoAsync(int storyId)
        {
            string sql = $@"
                SELECT s.story_title, s.story_synopsis, s.story_prologue, s.city_name, s.district_name, s.is_night_mode,
                       (SELECT COUNT(*) FROM story_node n WHERE n.s_id = s.s_id) AS node_count,
                       first_stop.p_name AS first_place_name,
                       first_stop.p_address AS first_place_address
                FROM story s
                LEFT JOIN (
                    SELECT p.p_name, p.p_address
                    FROM story_node sn
                    {PlaceImageJoin}
                    WHERE sn.s_id = @storyId
                    ORDER BY sn.sn_order
                    LIMIT 1
                ) first_stop ON TRUE
                WHERE s.s_id = @storyId AND s.is_active = 1;
            ";

            using var conn = new MySqlConnection(_appSettings.mydb);
            return await conn.QueryFirstOrDefaultAsync<StoryCalendarInfo>(sql, new { storyId });
        }
        #endregion



        #region 喜愛的劇本
        /// <summary>設定或取消喜愛；只能改自己的劇本。找不到劇本（或不是自己的）回傳 false</summary>
        public async Task<bool> SetFavoriteAsync(int auId, int storyId, bool isFavorite)
        {
            const string sql = @"
                UPDATE story SET is_favorite = @favorite
                WHERE s_id = @storyId AND au_id = @auId AND is_active = 1;
                SELECT COUNT(*) FROM story WHERE s_id = @storyId AND au_id = @auId AND is_active = 1;
            ";

            using var conn = new MySqlConnection(_appSettings.mydb);
            return await conn.ExecuteScalarAsync<int>(sql, new { auId, storyId, favorite = isFavorite ? 1 : 0 }) > 0;
        }

        /// <summary>使用者喜愛的劇本，最新建立的在前面</summary>
        public async Task<List<FavoriteStoryItem>> GetFavoriteStoriesAsync(int auId)
        {
            string sql = $@"
                SELECT s.s_id AS story_id, s.story_title AS title, s.story_synopsis AS synopsis,
                       s.city_name, s.district_name, (s.is_night_mode = 1) AS is_night_mode, s.created_at,
                       (SELECT COUNT(*) FROM story_node n WHERE n.s_id = s.s_id) AS node_count,
                       EXISTS (SELECT 1 FROM story_session ss
                                WHERE ss.au_id = s.au_id AND ss.s_id = s.s_id AND ss.ss_status = 'completed') AS is_completed,
                       (SELECT sn.place_id
                          FROM story_node sn
                         WHERE sn.s_id = s.s_id
                         ORDER BY sn.sn_order
                         LIMIT 1) AS cover_place_id
                FROM story s
                WHERE s.au_id = @auId AND s.is_favorite = 1 AND s.is_active = 1
                ORDER BY s.created_at DESC, s.s_id DESC;
            ";

            using var conn = new MySqlConnection(_appSettings.mydb);
            return (await conn.QueryAsync<FavoriteStoryItem>(sql, new { auId })).ToList();
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

        /// <summary>登入者與劇本的關係：劇本不存在時回傳 null</summary>
        public class StoryParticipation
        {
            public int owner_id { get; set; }

            /// <summary>這份劇本協作隊伍（未取消）的成員。只有遊玩紀錄不算（避免沒參與的人自己建紀錄）</summary>
            public bool is_participant { get; set; }
        }

        public StoryParticipation GetStoryParticipation(int auId, int storyId)
        {
            string sql = @"
                SELECT
                    s.au_id AS owner_id,
                    EXISTS (SELECT 1
                            FROM story_pair_member pm
                            INNER JOIN story_pair_session ps ON ps.pair_id = pm.pair_id
                            WHERE pm.au_id = @auId AND ps.s_id = s.s_id AND ps.pair_status <> 'cancelled') AS is_participant
                FROM story s
                WHERE s.s_id = @storyId;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.QueryFirstOrDefault<StoryParticipation>(sql, new { auId, storyId });
            }
        }

        /// <summary>
        /// 玩家結束劇本時呼叫，把該場次標記為完成（首頁與過往紀錄都以 completed 統計）。
        /// 沒有遊玩紀錄（沒按過確認開始）時不補建，回傳 false。
        /// </summary>
        public bool ClearStoryPlaying(int auId, int storyId)
        {
            string sql = @"
                UPDATE story_session
                SET ss_status = 'completed', completed_at = NOW()
                WHERE au_id = @auId AND s_id = @storyId;
            ";

            // 劇本擁有者結束劇本時，進行中的協作隊伍一併標成完成（配對碼釋出）
            string completePairSql = @"
                UPDATE story_pair_session ps
                INNER JOIN story s ON s.s_id = ps.s_id
                SET ps.pair_status = 'completed', ps.completed_at = NOW()
                WHERE ps.s_id = @storyId AND s.au_id = @auId
                  AND ps.pair_status IN ('waiting', 'locked', 'expired');
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                bool cleared = conn.Execute(sql, new { auId, storyId }) > 0;
                conn.Execute(completePairSql, new { auId, storyId });
                return cleared;
            }
        }

        /// <summary>查詢該使用者目前正在進行中的劇本。</summary>
        public CurrentPlayingStory GetCurrentPlayingStory(int auId)
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

                return new CurrentPlayingStory
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
        /// 1. AI 回傳的 NPC 名稱對應到 NPC 名單（npc 表），劇本與每一站都掛上這位 NPC；名單上沒有這個名字時用預設的薯光。
        /// 2. place 的主鍵是 int 自增，沒辦法再用 Neo4j uid 當主鍵。
        ///    節點仍然把 Neo4j uid 寫進 story_node.place_id（任務生成靠這個關聯），
        ///    另外把景點寫進 place 表（region_id 存 Neo4j uid）：名稱、座標，以及 Neo4j 查得到的地址、分類、介紹、照片、營業時間等。
        ///    已經有這個景點（同 uid，或同名且還沒記 uid）時不重複新增，只補上空著的欄位。
        /// 3. 遊你說算的工作（story_text_job）在同一個交易裡標成完成，劇本存了就一定記得存成哪一份，重複輪詢不會再存。
        /// </summary>
        /// <param name="partySize">AI 從那句話解析出的旅遊人數（parsed_intent.traveler_count）；2 人以上才能協作配對，解析不出來（0）存 NULL</param>
        /// <param name="places">spot_uuid → PlaceLookupService 查到的景點（名稱、座標）</param>
        /// <param name="placeRecords">spot_uuid → PlaceLookupService.GetPlaceRecordsAsync 查到的 place 表欄位；Neo4j 連不上時是空的</param>
        public async Task<int> SaveFullAiGeneratedStory(
            int auId, string cityName, string districtName, int partySize, ScriptBlueprintData data,
            IReadOnlyDictionary<string, backend.Services.Neo4j.PlaceLookupService.PlaceInfo> places,
            IReadOnlyDictionary<string, backend.Services.Neo4j.PlaceLookupService.PlaceRecord> placeRecords,
            string textJobId, string textJobResultJson)
        {
            if (data == null) throw new Exception("AI 回傳的劇本資料為空！");

            string insertStorySql = @"
                INSERT INTO story (
                    au_id, npc_id, city_name, district_name, party_size, sd_transport,
                    story_title, story_prologue, story_synopsis,
                    story_badge, story_postcards, is_active, is_night_mode
                ) VALUES (
                    @auId, @npcId, @cityName, @districtName, @partySize, '客製化交通',
                    @title, @prologue, @synopsis,
                    '', @expectedPostcards, 1, @isNightMode
                );
                SELECT LAST_INSERT_ID();
            ";

            string insertNodeSql = @"
                INSERT INTO story_node (
                    s_id, npc_id, place_id, sn_order, sn_title,
                    is_hidden, is_night_only, is_active,
                    location_codename, sn_opening_text, sn_success_text, sn_task_type
                ) VALUES (
                    @storyId, @npcId, @placeId, @order, @title,
                    2, @isNightOnly, 2,
                    @codename, @opening, @success, @taskType
                );
            ";

            // place 沒有 UNIQUE 欄位可做 upsert：先用 Neo4j uid 找（同一個景點 AI 換了寫法也認得），再用名稱找，都查不到才新增。
            string findPlaceSql = @"
                SELECT p_id, region_id FROM place
                WHERE (@regionId IS NOT NULL AND region_id = @regionId) OR p_name = @name
                ORDER BY COALESCE(region_id = @regionId, 0) DESC, p_id
                LIMIT 1;
            ";
            string insertPlaceSql = @"
                INSERT INTO place (
                    region_id, p_name, p_category, p_type, p_summary, p_introduction, p_address,
                    p_latitude, p_longitude, p_open_time, p_tel, p_website_url, p_image, p_business
                ) VALUES (
                    @regionId, @name, @p_category, @p_type, @p_summary, @p_introduction, @p_address,
                    @lat, @lng, @p_open_time, @p_tel, @p_website_url, @p_image, @p_business
                );
            ";
            // 已經有的景點只補空欄位，原本有值的不覆蓋
            string fillPlaceSql = @"
                UPDATE place SET
                    region_id      = COALESCE(region_id, @regionId),
                    p_category     = COALESCE(NULLIF(p_category, ''), @p_category),
                    p_type         = COALESCE(NULLIF(p_type, ''), @p_type),
                    p_summary      = COALESCE(NULLIF(p_summary, ''), @p_summary),
                    p_introduction = COALESCE(NULLIF(p_introduction, ''), @p_introduction),
                    p_address      = COALESCE(NULLIF(p_address, ''), @p_address),
                    p_latitude     = COALESCE(p_latitude, @lat),
                    p_longitude    = COALESCE(p_longitude, @lng),
                    p_open_time    = COALESCE(NULLIF(p_open_time, ''), @p_open_time),
                    p_tel          = COALESCE(NULLIF(p_tel, ''), @p_tel),
                    p_website_url  = COALESCE(NULLIF(p_website_url, ''), @p_website_url),
                    p_image        = COALESCE(NULLIF(p_image, ''), @p_image),
                    p_business     = COALESCE(NULLIF(p_business, ''), @p_business)
                WHERE p_id = @placeId;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                await conn.OpenAsync();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        int? npcId = (await ResolveNpcAsync(conn, transaction, data.npc?.name))?.npc_id;

                        int newStoryId = await conn.ExecuteScalarAsync<int>(insertStorySql, new
                        {
                            auId,
                            npcId,
                            cityName = cityName ?? "",
                            districtName = districtName ?? "",
                            partySize = partySize > 0 ? partySize : (int?)null,
                            title = data.title ?? "專屬客製化旅程",
                            prologue = data.preface ?? "",
                            synopsis = data.synopsis ?? "",
                            expectedPostcards = data.nodes?.Count ?? 0,
                            isNightMode = data.is_night_mode ? 1 : 0
                        }, transaction);

                        foreach (var node in data.nodes ?? new List<ScriptBlueprintNode>())
                        {
                            // AI 給的 spot_uuid 就是 Neo4j uid（景點、餐廳、旅宿都有），座標用 PlaceLookupService 查到的；
                            // 沒有 spot_uuid 時才用名稱找 Neo4j 景點；都沒有座標就退回 Nominatim 地理編碼。
                            string neo4jUid = string.IsNullOrWhiteSpace(node.spot_uuid) ? null : node.spot_uuid.Trim();
                            double? lat = null;
                            double? lng = null;

                            if (neo4jUid != null && places != null && places.TryGetValue(neo4jUid, out var place))
                            {
                                lat = place.lat;
                                lng = place.lng;
                            }
                            else if (neo4jUid == null)
                            {
                                var neo4jMatch = await _neo4jService.FindAttractionCoordinatesAsync(node.place_name);
                                lat = neo4jMatch.lat;
                                lng = neo4jMatch.lon;
                                neo4jUid = neo4jMatch.uid;
                            }

                            if (!lat.HasValue || !lng.HasValue)
                            {
                                var geoResult = await _geocodingService.SearchPlaceCoordinatesAsync(node.place_name, cityName);
                                lat = geoResult.lat;
                                lng = geoResult.lng;
                            }

                            if (!string.IsNullOrWhiteSpace(node.place_name))
                            {
                                var existing = await conn.QueryFirstOrDefaultAsync<(int p_id, string region_id)>(
                                    findPlaceSql, new { regionId = neo4jUid, name = node.place_name }, transaction);

                                var record = neo4jUid != null ? placeRecords?.GetValueOrDefault(neo4jUid) : null;
                                var placeParams = new
                                {
                                    placeId = existing.p_id,
                                    regionId = neo4jUid,
                                    name = node.place_name,
                                    lat,
                                    lng,
                                    p_category = record?.p_category,
                                    p_type = record?.p_type,
                                    p_summary = record?.p_summary,
                                    p_introduction = record?.p_introduction,
                                    p_address = record?.p_address,
                                    p_open_time = record?.p_open_time,
                                    p_tel = record?.p_tel,
                                    p_website_url = record?.p_website_url,
                                    p_image = record?.p_image,
                                    p_business = record?.p_business
                                };

                                if (existing.p_id == 0)
                                    await conn.ExecuteAsync(insertPlaceSql, placeParams, transaction);
                                else if (existing.region_id == null || existing.region_id == neo4jUid)
                                    await conn.ExecuteAsync(fillPlaceSql, placeParams, transaction);   // 同名但記的是別的 uid（不同景點）時不動
                            }

                            await conn.ExecuteAsync(insertNodeSql, new
                            {
                                storyId = newStoryId,
                                npcId,
                                placeId = neo4jUid,
                                order = node.node_order,
                                title = node.node_title ?? node.place_name ?? "",
                                isNightOnly = data.is_night_mode ? 1 : 0,
                                codename = node.location_codename ?? "",
                                opening = node.dialogues?.opening ?? "",
                                success = node.dialogues?.success ?? "",
                                taskType = node.task_type ?? ""
                            }, transaction);
                        }

                        await conn.ExecuteAsync(@"
                            UPDATE story_text_job
                            SET status = @done, s_id = @storyId, result_json = @resultJson, error_message = NULL
                            WHERE job_id = @jobId;",
                            new { done = TextJobRow.Done, storyId = newStoryId, resultJson = textJobResultJson, jobId = textJobId }, transaction);

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


        #region 遊你說算：AI 生成工作（story_text_job）

        /// <summary>遊你說算的一個 AI 生成工作</summary>
        public class TextJobRow
        {
            public const int Processing = 1;
            public const int Saving = 2;
            public const int Done = 3;
            public const int Failed = 4;

            public string job_id { get; set; }
            public int au_id { get; set; }
            public int status { get; set; }
            public int? s_id { get; set; }
            public string result_json { get; set; }
            public string error_message { get; set; }

            /// <summary>送出到現在幾秒</summary>
            public long elapsed_seconds { get; set; }

            /// <summary>最後一次更新到現在幾秒（存檔中卡太久時用來判斷能不能接手）</summary>
            public long idle_seconds { get; set; }
        }

        /// <summary>
        /// 記下這個 AI 工作是誰送的。AI 的 job_id 只有 8 碼，重啟後可能發到以前用過的代號，同代號就當成新工作覆蓋。
        /// </summary>
        public async Task CreateTextJobAsync(string jobId, int auId, string userPrompt)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            await conn.ExecuteAsync(@"
                INSERT INTO story_text_job (job_id, au_id, user_prompt, status)
                VALUES (@jobId, @auId, @userPrompt, @processing)
                ON DUPLICATE KEY UPDATE
                    au_id = @auId, user_prompt = @userPrompt, status = @processing,
                    s_id = NULL, result_json = NULL, error_message = NULL, created_at = NOW();",
                new { jobId, auId, userPrompt, processing = TextJobRow.Processing });
        }

        /// <summary>查自己送出的工作，別人的查不到</summary>
        public async Task<TextJobRow> GetTextJobAsync(string jobId, int auId)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            return await conn.QueryFirstOrDefaultAsync<TextJobRow>(@"
                SELECT job_id, au_id, status, s_id, result_json, error_message,
                       TIMESTAMPDIFF(SECOND, created_at, NOW()) AS elapsed_seconds,
                       TIMESTAMPDIFF(SECOND, updated_at, NOW()) AS idle_seconds
                FROM story_text_job
                WHERE job_id = @jobId AND au_id = @auId;", new { jobId, auId });
        }

        /// <summary>
        /// 搶存檔權：生成中 → 存檔中，搶到的請求才存劇本；存檔中卡超過 savingTimeout 的也可以接手。
        /// </summary>
        public async Task<bool> TryStartSavingTextJobAsync(string jobId, TimeSpan savingTimeout)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            int affected = await conn.ExecuteAsync(@"
                UPDATE story_text_job SET status = @saving
                WHERE job_id = @jobId
                  AND (status = @processing
                       OR (status = @saving AND updated_at < NOW() - INTERVAL @timeoutSeconds SECOND));",
                new { jobId, saving = TextJobRow.Saving, processing = TextJobRow.Processing, timeoutSeconds = (int)savingTimeout.TotalSeconds });
            return affected == 1;
        }

        /// <summary>存檔失敗：存檔中 → 生成中，下次輪詢再存一次</summary>
        public async Task ResetTextJobAsync(string jobId)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            await conn.ExecuteAsync("UPDATE story_text_job SET status = @processing WHERE job_id = @jobId AND status = @saving;",
                new { jobId, processing = TextJobRow.Processing, saving = TextJobRow.Saving });
        }

        /// <summary>生成失敗（AI 回報失敗、查不到工作或逾時）</summary>
        public async Task FailTextJobAsync(string jobId, string error)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            await conn.ExecuteAsync("UPDATE story_text_job SET status = @failed, error_message = @error WHERE job_id = @jobId AND status = @processing;",
                new { jobId, failed = TextJobRow.Failed, processing = TextJobRow.Processing, error = error?.Length > 500 ? error[..500] : error });
        }

        #endregion

        #region 現在揪出發：劇本生成工作（story_game_job）

        /// <summary>現在揪出發（GenerateGameStory）的一個生成工作，一個工作包含好幾份劇本</summary>
        public class GameJobRow
        {
            public const int Processing = 1;
            public const int Saving = 2;
            public const int Done = 3;
            public const int Failed = 4;

            public string job_id { get; set; }
            public int au_id { get; set; }
            public int status { get; set; }

            /// <summary>送給 AI 的每份劇本條件、AI 任務代號，以及存檔要用的資料（StoryService.GameJobPlan）</summary>
            public string plan_json { get; set; }

            /// <summary>
            /// 存好的劇本：完成（status = 3）時是回傳給前端的最終結果；還沒完成卻有值，代表劇本已寫進資料庫、只差最後整理
            /// </summary>
            public string result_json { get; set; }
            public string error_message { get; set; }

            /// <summary>送出到現在幾秒</summary>
            public long elapsed_seconds { get; set; }

            /// <summary>最後一次更新到現在幾秒（存檔中卡太久時用來判斷能不能接手）</summary>
            public long idle_seconds { get; set; }
        }

        public async Task CreateGameJobAsync(string jobId, int auId, string planJson)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            await conn.ExecuteAsync(@"
                INSERT INTO story_game_job (job_id, au_id, status, plan_json)
                VALUES (@jobId, @auId, @processing, @planJson);",
                new { jobId, auId, planJson, processing = GameJobRow.Processing });
        }

        /// <summary>查自己送出的工作，別人的查不到</summary>
        public async Task<GameJobRow> GetGameJobAsync(string jobId, int auId)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            return await conn.QueryFirstOrDefaultAsync<GameJobRow>(@"
                SELECT job_id, au_id, status, plan_json, result_json, error_message,
                       TIMESTAMPDIFF(SECOND, created_at, NOW()) AS elapsed_seconds,
                       TIMESTAMPDIFF(SECOND, updated_at, NOW()) AS idle_seconds
                FROM story_game_job
                WHERE job_id = @jobId AND au_id = @auId;", new { jobId, auId });
        }

        /// <summary>
        /// 搶存檔權：生成中 → 存檔中，搶到的請求才存劇本；存檔中卡超過 savingTimeout 的也可以接手。
        /// </summary>
        public async Task<bool> TryStartSavingGameJobAsync(string jobId, TimeSpan savingTimeout)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            int affected = await conn.ExecuteAsync(@"
                UPDATE story_game_job SET status = @saving, updated_at = NOW()
                WHERE job_id = @jobId
                  AND (status = @processing
                       OR (status = @saving AND updated_at < NOW() - INTERVAL @timeoutSeconds SECOND));",
                new { jobId, saving = GameJobRow.Saving, processing = GameJobRow.Processing, timeoutSeconds = (int)savingTimeout.TotalSeconds });
            return affected == 1;
        }

        /// <summary>存檔失敗：存檔中 → 生成中，下次輪詢再存一次（劇本已寫進資料庫的話，下次只做最後整理）</summary>
        public async Task ResetGameJobAsync(string jobId)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            await conn.ExecuteAsync("UPDATE story_game_job SET status = @processing WHERE job_id = @jobId AND status = @saving;",
                new { jobId, processing = GameJobRow.Processing, saving = GameJobRow.Saving });
        }

        /// <summary>完成：記下回傳給前端的最終結果</summary>
        public async Task CompleteGameJobAsync(string jobId, string resultJson)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            await conn.ExecuteAsync("UPDATE story_game_job SET status = @done, result_json = @resultJson, error_message = NULL WHERE job_id = @jobId;",
                new { jobId, resultJson, done = GameJobRow.Done });
        }

        /// <summary>生成失敗（AI 全部失敗、查不到工作或逾時）</summary>
        public async Task FailGameJobAsync(string jobId, string error)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            await conn.ExecuteAsync(@"
                UPDATE story_game_job SET status = @failed, error_message = @error
                WHERE job_id = @jobId AND status IN (@processing, @saving);",
                new { jobId, failed = GameJobRow.Failed, processing = GameJobRow.Processing, saving = GameJobRow.Saving,
                      error = error?.Length > 500 ? error[..500] : error });
        }

        #endregion

        #region 劇本任務生成（AI service /api/stories/tasks，一次只送一份劇本）

        /// <summary>place_type + type 查出的單筆景點任務類型</summary>
        public class PlaceTaskTypeRow
        {
            public string place_id { get; set; }
            public string place_category { get; set; }
            public int type_id { get; set; }
            public string type_name { get; set; }
        }

        /// <summary>
        /// 查多個景點（Neo4j uuid）各自可出的任務類型，來源 place_type JOIN type。
        /// </summary>
        public async Task<List<PlaceTaskTypeRow>> GetPlaceTaskTypesAsync(List<string> placeIds)
        {
            if (placeIds == null || placeIds.Count == 0) return new List<PlaceTaskTypeRow>();

            string sql = @"
                SELECT pt.place_id, pt.place_category, pt.type_id, t.type_name
                FROM place_type pt
                INNER JOIN `type` t ON t.type_id = pt.type_id
                WHERE pt.place_id IN @placeIds
                ORDER BY pt.place_id, pt.type_id;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                var rows = await conn.QueryAsync<PlaceTaskTypeRow>(sql, new { placeIds });
                return rows.ToList();
            }
        }

        /// <summary>
        /// 範圍內的景點（MySQL place 表），Neo4j 連不上時的備援。
        /// uid 用 place_type 依名稱對回 Neo4j uuid（同 MapDao 的橋接方式），對不到時用 "place-{p_id}"。
        /// </summary>
        public async Task<List<ReachableAttractionNode>> GetPlacesInBoundsAsync(double lat, double lng, double minLat, double maxLat, double minLon, double maxLon)
        {
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                var places = (await conn.QueryAsync<(int p_id, string p_name, double lat, double lng)>(@"
                    SELECT p_id, p_name, p_latitude AS lat, p_longitude AS lng
                    FROM place
                    WHERE p_latitude BETWEEN @minLat AND @maxLat AND p_longitude BETWEEN @minLon AND @maxLon
                      AND (p_category IS NULL OR p_category <> '飲食');",
                    new { minLat, maxLat, minLon, maxLon })).ToList();
                if (places.Count == 0) return new List<ReachableAttractionNode>();

                var names = places.Select(p => p.p_name).Distinct().ToList();
                var uids = (await conn.QueryAsync<(string place_name, string place_id)>(@"
                    SELECT place_name, MIN(place_id) AS place_id FROM place_type
                    WHERE place_name IN @names GROUP BY place_name;", new { names }))
                    .ToDictionary(r => r.place_name, r => r.place_id);

                return places
                    .Select(p => new ReachableAttractionNode
                    {
                        uid = uids.TryGetValue(p.p_name, out string uid) ? uid : $"place-{p.p_id}",
                        name = p.p_name,
                        lat = p.lat,
                        lon = p.lng,
                        distance_m = Math.Round(Services.Geo.DistanceMeters(lat, lng, p.lat, p.lng), 1)
                    })
                    .OrderBy(p => p.distance_m)
                    .ToList();
            }
        }

        /// <summary>商家題庫的一題（含選項），place_id 為商家對應的 Neo4j uuid（store.store_uid）</summary>
        public class MerchantQuestionRow
        {
            public string place_id { get; set; }
            public int question_id { get; set; }
            public string question_describe { get; set; }
            public List<GameStoryTaskOption> options { get; set; } = new List<GameStoryTaskOption>();
        }

        private class MerchantQuestionOptionRow
        {
            public int question_id { get; set; }
            public string option_key { get; set; }
            public string option_context { get; set; }
            public string option_url { get; set; }
            public int is_correct { get; set; }
        }

        /// <summary>
        /// 查多個景點中，屬於已註冊商家（store.store_uid）的商家題庫（store_question + question_option）。
        /// 沒有選項的題目不回傳（無法作答）。
        /// </summary>
        public async Task<List<MerchantQuestionRow>> GetMerchantQuestionsByPlaceIdsAsync(List<string> placeIds)
        {
            if (placeIds == null || placeIds.Count == 0) return new List<MerchantQuestionRow>();

            string sql = @"
                SELECT s.store_uid AS place_id, q.question_id, q.question_describe
                FROM store s
                INNER JOIN store_question q ON q.store_id = s.s_id
                WHERE s.store_uid IN @placeIds;

                SELECT o.question_id, o.option_key, o.option_context, o.option_url, o.is_correct
                FROM question_option o
                INNER JOIN store_question q ON q.question_id = o.question_id
                INNER JOIN store s ON s.s_id = q.store_id
                WHERE s.store_uid IN @placeIds
                ORDER BY o.option_id;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                using var multi = await conn.QueryMultipleAsync(sql, new { placeIds });
                List<MerchantQuestionRow> questions = (await multi.ReadAsync<MerchantQuestionRow>()).ToList();
                ILookup<int, MerchantQuestionOptionRow> options = (await multi.ReadAsync<MerchantQuestionOptionRow>())
                    .ToLookup(o => o.question_id);

                foreach (MerchantQuestionRow question in questions)
                {
                    question.options = options[question.question_id]
                        .Select(o => new GameStoryTaskOption
                        {
                            option_key = o.option_key,
                            option_context = o.option_context,
                            option_url = o.option_url,
                            is_correct = o.is_correct == 1 ? 1 : 0
                        })
                        .ToList();
                }

                return questions.Where(q => q.options.Count > 0).ToList();
            }
        }

        /// <summary>
        /// 現有商家的景點 uid（store.store_uid，含自建景點與綁定的既有景點），行程規劃補商家候選用，
        /// 座標由 PlaceLookupService 向 Neo4j 查（依 uid 查，不看類型標籤）。已停用帳號的商家不列入。
        /// </summary>
        public async Task<List<string>> GetStoreUidsAsync()
        {
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                var rows = await conn.QueryAsync<string>(@"
                    SELECT DISTINCT s.store_uid
                    FROM store s
                    INNER JOIN auth a ON a.au_id = s.au_id
                    WHERE s.store_uid IS NOT NULL AND s.store_uid <> '' AND a.is_active = 1;");
                return rows.ToList();
            }
        }

        /// <summary>任務類型對照（type_id → type_name）</summary>
        public async Task<Dictionary<int, string>> GetTaskTypeNamesAsync()
        {
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                var rows = await conn.QueryAsync<(int type_id, string type_name)>("SELECT type_id, type_name FROM `type`;");
                return rows.GroupBy(r => r.type_id).ToDictionary(g => g.Key, g => g.First().type_name);
            }
        }

        /// <summary>啟用中的敘事語氣名稱（narrative_tone.nt_name）</summary>
        public async Task<List<string>> GetNarrativeTonesAsync()
        {
            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                var rows = await conn.QueryAsync<string>("SELECT nt_name FROM narrative_tone WHERE is_active = 1;");
                return rows.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct().ToList();
            }
        }

        /// <summary>
        /// 把 AI 生成的多份劇本 + 任務整包寫入 story、story_tag、story_node、task、task_option、task_clue（同一個交易，全部成功才寫入）。
        /// 商家知識問答（task.question_id 有值）只寫 task 本身，題目與選項沿用商家題庫。
        /// 寫入後把 story_id、sn_id、task_db_id 回填到 results。
        /// 有帶 gameJobId 時，在同一個交易把存好的劇本（savedJson，回填代號後才產生）記進 story_game_job.result_json：
        /// 劇本存了就一定記得存成哪幾份，存到一半中斷、下次輪詢接手時不會重複存。
        /// </summary>
        public async Task SaveGameStoriesAsync(int auId, List<string> preferences, List<GameStoryResult> results, bool planBusTransit,
                                               string gameJobId = null, Func<string> savedJson = null)
        {
            string insertStorySql = @"
                INSERT INTO story (
                    au_id, npc_id, city_name, district_name, party_size, sd_transport,
                    story_title, story_prologue, story_synopsis,
                    story_badge, story_postcards, is_active, is_night_mode
                ) VALUES (
                    @auId, @npcId, @cityName, @districtName, @partySize, @transport,
                    @title, @prologue, @synopsis,
                    @badge, @postcards, 1, @isNightMode
                );
                SELECT LAST_INSERT_ID();
            ";

            string insertTagSql = "INSERT IGNORE INTO story_tag (s_id, s_tag) VALUES (@storyId, @tag);";

            string insertNodeSql = @"
                INSERT INTO story_node (
                    s_id, npc_id, place_id, sn_order, sn_title,
                    is_hidden, is_night_only, is_active,
                    location_codename, sn_opening_text, sn_success_text, sn_task_type
                ) VALUES (
                    @storyId, @npcId, @placeId, @order, @title,
                    @isHidden, @isNightOnly, 2,
                    @codename, @opening, @success, @taskType
                );
                SELECT LAST_INSERT_ID();
            ";

            // 商家題庫任務只寫 question_id，題目文字與選項由 store_question / question_option 帶出
            string insertTaskSql = @"
                INSERT INTO task (story_id, node_id, task_type, question_id, task_describe, correct_answer, task_hint)
                VALUES (@storyId, @nodeId, @typeId, @questionId, @describe, @answer, @hint);
                SELECT LAST_INSERT_ID();
            ";

            // task_option.option_id 沒有自動遞增，交易內鎖住後自行編號
            string maxOptionIdSql = "SELECT COALESCE(MAX(option_id), 0) FROM task_option FOR UPDATE;";
            string insertOptionSql = @"
                INSERT INTO task_option (option_id, task_id, option_context, is_correct, option_key)
                VALUES (@optionId, @taskId, @text, @isCorrect, @key);
            ";

            string insertClueSql = @"
                INSERT INTO task_clue (task_id, seat_no, clue_text)
                VALUES (@taskId, @seatNo, @text);
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                await conn.OpenAsync();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // task_option.option_id 所有劇本共用同一個遞增計數
                        int nextOptionId = await conn.ExecuteScalarAsync<int>(maxOptionIdSql, transaction: transaction);

                        foreach (GameStoryResult result in results)
                        {
                            GameStoryInfo story = result.story;

                            // AI 挑的 NPC（名單上找不到就用薯光），劇本與每一站都掛上這位 NPC；回傳的 NPC 資料以 npc 表為準
                            story.npc = await ResolveNpcAsync(conn, transaction, story.npc?.npc_name);
                            int? npcId = story.npc?.npc_id;

                            int storyId = await conn.ExecuteScalarAsync<int>(insertStorySql, new
                            {
                                auId,
                                npcId,
                                cityName = story.city_name ?? "",
                                districtName = story.district_name ?? "",
                                partySize = story.party_size,
                                transport = string.Join(",", story.sd_transport ?? new List<string>()),
                                title = string.IsNullOrWhiteSpace(story.story_title) ? "專屬客製化旅程" : Truncate(story.story_title, 255),
                                prologue = story.story_prologue ?? "",
                                synopsis = story.story_synopsis ?? "",
                                badge = string.Join(",", story.story_badge ?? new List<string>()),
                                postcards = story.story_postcards,
                                isNightMode = story.is_night_mode == 1 ? 1 : 0
                            }, transaction);

                            foreach (string tag in (preferences ?? new List<string>())
                                         .Where(t => !string.IsNullOrWhiteSpace(t))
                                         .Select(t => Truncate(t.Trim(), 100))
                                         .Distinct())
                            {
                                await conn.ExecuteAsync(insertTagSql, new { storyId, tag }, transaction);
                            }

                            foreach (GameStoryNode node in result.nodes ?? new List<GameStoryNode>())
                            {
                                List<GameStoryTask> tasks = node.tasks ?? new List<GameStoryTask>();
                                node.npc_id = npcId;

                                node.sn_id = await conn.ExecuteScalarAsync<int>(insertNodeSql, new
                                {
                                    storyId,
                                    npcId = node.npc_id,
                                    placeId = node.place_id,
                                    order = node.sn_order,
                                    title = Truncate(node.sn_title ?? "", 255),
                                    isHidden = node.is_hidden == 1 ? 1 : 2,
                                    isNightOnly = node.is_night_only == 1 ? 1 : 0,
                                    codename = Truncate(node.location_codename ?? "", 255),
                                    opening = node.sn_opening_text ?? "",
                                    success = node.sn_success_text ?? "",
                                    taskType = Truncate(node.sn_task_type ?? "", 50)
                                }, transaction);

                                foreach (GameStoryTask task in tasks)
                                {
                                    bool fromQuestionBank = task.question_id.HasValue;

                                    task.task_id = await conn.ExecuteScalarAsync<int>(insertTaskSql, new
                                    {
                                        storyId,
                                        nodeId = node.sn_id,
                                        typeId = task.task_type,
                                        questionId = task.question_id,
                                        describe = fromQuestionBank ? null : task.task_describe ?? "",
                                        answer = string.IsNullOrWhiteSpace(task.correct_answer) ? null : Truncate(task.correct_answer, 255),
                                        hint = fromQuestionBank ? null : task.task_hint ?? ""
                                    }, transaction);

                                    if (fromQuestionBank) continue;

                                    foreach (GameStoryTaskOption option in task.task_option ?? new List<GameStoryTaskOption>())
                                    {
                                        await conn.ExecuteAsync(insertOptionSql, new
                                        {
                                            optionId = ++nextOptionId,
                                            taskId = task.task_id,
                                            text = Truncate(option.option_context ?? "", 255),
                                            isCorrect = option.is_correct == 1 ? 1 : 0,
                                            key = Truncate(option.option_key ?? "", 10)
                                        }, transaction);
                                    }

                                    foreach (GameStoryTaskClue clue in task.task_clue ?? new List<GameStoryTaskClue>())
                                    {
                                        await conn.ExecuteAsync(insertClueSql, new
                                        {
                                            taskId = task.task_id,
                                            seatNo = clue.seat_no,
                                            text = clue.clue_text ?? ""
                                        }, transaction);
                                    }
                                }
                            }

                            // 相鄰節點之間的直達公車（nodes 已依 sn_order 排序）
                            if (planBusTransit)
                            {
                                List<GameStoryNode> nodes = result.nodes ?? new List<GameStoryNode>();
                                for (int i = 1; i < nodes.Count; i++)
                                {
                                    await SaveDirectBusLegAsync(conn, transaction, nodes[i - 1], nodes[i]);
                                }
                            }

                            result.story_id = storyId;
                        }

                        if (gameJobId != null)
                        {
                            await conn.ExecuteAsync("UPDATE story_game_job SET result_json = @json WHERE job_id = @jobId;",
                                new { json = savedJson?.Invoke(), jobId = gameJobId }, transaction);
                        }

                        transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        throw new Exception($"劇本任務寫入資料庫失敗: {ex.Message}");
                    }
                }
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
            return value.Substring(0, maxLength);
        }

        /// <summary>
        /// AI 挑的 NPC 名字 → NPC 名單（npc 表）裡的那一位；沒給或名字不在名單上時用預設 NPC（薯光）。
        /// 名單是空的（還沒匯入）時回傳 null。
        /// </summary>
        private static async Task<GameStoryNpc> ResolveNpcAsync(MySqlConnection conn, MySqlTransaction transaction, string name)
        {
            return await conn.QueryFirstOrDefaultAsync<GameStoryNpc>(@"
                SELECT npc_id, npc_name, npc_role, npc_intro, npc_avatar AS npc_avatar_url
                FROM npc
                WHERE npc_name = @name OR is_default = 1
                ORDER BY npc_name = @name DESC, npc_id
                LIMIT 1;",
                new { name = name?.Trim() ?? "" }, transaction);
        }

        #endregion


        #region 公車 / 台灣好行

        // 粗估每站乘車時間（分鐘），存進 story_node_transit.est_ride_minutes 當快照
        private const double EstMinutesPerStop = 2.0;

        public static string RouteTypeName(int routeType)
        {
            switch (routeType)
            {
                case 1: return "市區公車";
                case 2: return "公路客運";
                case 3: return "台灣好行";
                default: return "公車";
            }
        }

        /// <summary>
        /// 找出從景點 A 附近站牌上車、在景點 B 附近站牌下車的直達公車（同一個行駛型態、下車站序較大），
        /// 優先搭乘站數少、再來步行距離短的；找到就寫入 story_node_transit。找不到直達就不寫（目前不做轉乘）。
        /// </summary>
        private static async Task SaveDirectBusLegAsync(MySqlConnection conn, MySqlTransaction transaction, GameStoryNode from, GameStoryNode to)
        {
            if (string.IsNullOrWhiteSpace(from.place_id) || string.IsNullOrWhiteSpace(to.place_id)) return;

            string findSql = @"
                SELECT b.brs_id AS board_brs_id, a.brs_id AS alight_brs_id,
                       a.stop_sequence - b.stop_sequence AS stop_count
                FROM place_bus_stop pa
                INNER JOIN bus_route_stop b ON b.bs_id = pa.bs_id
                INNER JOIN bus_route_stop a ON a.brp_id = b.brp_id AND a.stop_sequence > b.stop_sequence
                INNER JOIN place_bus_stop pb ON pb.bs_id = a.bs_id AND pb.place_id = @toPlace
                INNER JOIN bus_route_pattern brp ON brp.brp_id = b.brp_id
                INNER JOIN bus_sub_route bsr ON bsr.bsr_id = brp.bsr_id
                INNER JOIN bus_route br ON br.br_id = bsr.br_id AND br.is_active = 1
                WHERE pa.place_id = @fromPlace
                ORDER BY stop_count ASC, pa.walk_distance_m + pb.walk_distance_m ASC
                LIMIT 1;
            ";

            var leg = await conn.QueryFirstOrDefaultAsync<DirectLegRow>(findSql,
                new { fromPlace = from.place_id, toPlace = to.place_id }, transaction);
            if (leg == null) return;

            await conn.ExecuteAsync(@"
                INSERT INTO story_node_transit (from_sn_id, to_sn_id, leg_order, board_brs_id, alight_brs_id, est_ride_minutes)
                VALUES (@fromSnId, @toSnId, 1, @board, @alight, @minutes);
            ", new
            {
                fromSnId = from.sn_id,
                toSnId = to.sn_id,
                board = leg.board_brs_id,
                alight = leg.alight_brs_id,
                minutes = Math.Round(leg.stop_count * EstMinutesPerStop, 1)
            }, transaction);
        }

        private class DirectLegRow
        {
            public int board_brs_id { get; set; }
            public int alight_brs_id { get; set; }
            public int stop_count { get; set; }
        }

        /// <summary>
        /// 讀出劇本所有節點之間的公車交通方案，含上下車站之間依序經過的所有站牌。
        /// </summary>
        public async Task<List<BusTransitLeg>> GetStoryTransitAsync(int storyId)
        {
            string legSql = @"
                SELECT snt.from_sn_id, snt.to_sn_id, snt.leg_order, snt.est_ride_minutes,
                       br.route_name, br.route_type, bsr.sub_route_name, brp.headsign, ttr.ttr_theme AS tripper_theme,
                       b.brp_id, b.stop_sequence AS board_seq, a.stop_sequence AS alight_seq,
                       bb.stop_name AS board_stop_name, ab.stop_name AS alight_stop_name
                FROM story_node_transit snt
                INNER JOIN story_node sn ON sn.sn_id = snt.to_sn_id
                INNER JOIN bus_route_stop b ON b.brs_id = snt.board_brs_id
                INNER JOIN bus_route_stop a ON a.brs_id = snt.alight_brs_id
                INNER JOIN bus_stop bb ON bb.bs_id = b.bs_id
                INNER JOIN bus_stop ab ON ab.bs_id = a.bs_id
                INNER JOIN bus_route_pattern brp ON brp.brp_id = b.brp_id
                INNER JOIN bus_sub_route bsr ON bsr.bsr_id = brp.bsr_id
                INNER JOIN bus_route br ON br.br_id = bsr.br_id
                LEFT JOIN taiwan_tripper_route ttr ON ttr.br_id = br.br_id
                WHERE sn.s_id = @storyId
                ORDER BY sn.sn_order, snt.leg_order;
            ";

            string stopsSql = @"
                SELECT brs.stop_sequence, bs.stop_name, bs.bs_latitude AS lat, bs.bs_longitude AS lng
                FROM bus_route_stop brs
                INNER JOIN bus_stop bs ON bs.bs_id = brs.bs_id
                WHERE brs.brp_id = @brpId AND brs.stop_sequence BETWEEN @fromSeq AND @toSeq
                ORDER BY brs.stop_sequence;
            ";

            var legs = new List<BusTransitLeg>();

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                var rows = (await conn.QueryAsync<TransitLegRow>(legSql, new { storyId })).ToList();
                if (rows.Count == 0) return legs;

                var shapes = (await conn.QueryAsync<(int brp_id, string geometry)>(
                    "SELECT brp_id, geometry FROM bus_route_shape WHERE brp_id IN @brpIds;",
                    new { brpIds = rows.Select(r => r.brp_id).Distinct().ToList() }))
                    .ToDictionary(s => s.brp_id, s => s.geometry);

                foreach (var row in rows)
                {
                    var stops = (await conn.QueryAsync<TransitStopRow>(stopsSql,
                        new { brpId = row.brp_id, fromSeq = row.board_seq, toSeq = row.alight_seq })).ToList();

                    List<double[]> coordinates = null;
                    if (stops.Count >= 2 && shapes.TryGetValue(row.brp_id, out string wkt))
                        coordinates = BusDao.SliceShape(wkt,
                            (double)stops[0].lat, (double)stops[0].lng, (double)stops[^1].lat, (double)stops[^1].lng);

                    legs.Add(new BusTransitLeg
                    {
                        from_sn_id = row.from_sn_id,
                        to_sn_id = row.to_sn_id,
                        leg_order = row.leg_order,
                        route_name = row.route_name,
                        sub_route_name = row.sub_route_name,
                        route_type = row.route_type,
                        route_type_name = RouteTypeName(row.route_type),
                        tripper_theme = row.tripper_theme,
                        headsign = row.headsign,
                        board_stop_name = row.board_stop_name,
                        alight_stop_name = row.alight_stop_name,
                        stop_count = row.alight_seq - row.board_seq,
                        est_ride_minutes = row.est_ride_minutes.HasValue ? (double)row.est_ride_minutes.Value : (double?)null,
                        stops = stops.Select(s => new BusTransitStop
                        {
                            stop_sequence = s.stop_sequence,
                            stop_name = s.stop_name,
                            lat = (double)s.lat,
                            lng = (double)s.lng
                        }).ToList(),
                        coordinates = coordinates ?? stops.Select(s => new[] { (double)s.lng, (double)s.lat }).ToList()
                    });
                }
            }

            return legs;
        }

        private class TransitLegRow
        {
            public int from_sn_id { get; set; }
            public int to_sn_id { get; set; }
            public int leg_order { get; set; }
            public decimal? est_ride_minutes { get; set; }
            public string route_name { get; set; }
            public int route_type { get; set; }
            public string sub_route_name { get; set; }
            public string headsign { get; set; }
            public string tripper_theme { get; set; }
            public int brp_id { get; set; }
            public int board_seq { get; set; }
            public int alight_seq { get; set; }
            public string board_stop_name { get; set; }
            public string alight_stop_name { get; set; }
        }

        private class TransitStopRow
        {
            public int stop_sequence { get; set; }
            public string stop_name { get; set; }
            public decimal lat { get; set; }
            public decimal lng { get; set; }
        }

        #endregion


        #endregion
    }
}