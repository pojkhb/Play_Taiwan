// 檔案路徑：System\dao\Badge\BadgeDao.cs
// 對應新資料表 `badge` + `au_badge`，取代舊的 md_badge / ep_badge。
// au_badge.s_id 記錄勳章是從哪個劇本抽到的，一個劇本只能抽一枚（Sqls/mysql/AuBadgeStoryId.sql）。
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using backend.utils;
using backend.Models;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class BadgeDao
    {
        private readonly AppSettings _appSettings;

        public BadgeDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        #region 取得徽章圖鑑狀態（全部勳章 + 該使用者是否已解鎖）
        public List<BadgeResponse> GetAllBadgeStatus(int auId)
        {
            string sql = @"
                SELECT
                    b.b_id,
                    b.b_name,
                    b.b_fication,
                    b.b_thing,
                    b.b_image,
                    IF(ab.ab_id IS NOT NULL, TRUE, FALSE) AS is_owned,
                    ab.created_at AS obtained_at
                FROM badge b
                LEFT JOIN au_badge ab ON ab.b_id = b.b_id AND ab.au_id = @auId
                ORDER BY b.b_fication, b.b_id;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.Query<BadgeResponse>(sql, new { auId }).ToList();
            }
        }
        #endregion

        #region 劇本勳章：可抽類別與抽勳章

        private MySqlConnection Open() => new MySqlConnection(_appSettings.mydb);

        public async Task<List<BadgeInfo>> GetBadgesAsync()
        {
            using var conn = Open();
            return (await conn.QueryAsync<BadgeInfo>(
                "SELECT b_id, b_name, b_fication, b_thing, b_image FROM badge ORDER BY b_id;")).ToList();
        }

        /// <summary>判斷劇本可抽哪些勳章要看的資料</summary>
        public class StoryFacts
        {
            public string city_name { get; set; }

            /// <summary>1 = 夜間劇本</summary>
            public int is_night_mode { get; set; }

            public List<string> tags { get; set; }
            public List<StoryPlace> places { get; set; }
            public List<int> task_types { get; set; }
        }

        public class StoryPlace
        {
            public string place_name { get; set; }

            /// <summary>place_type.place_category，例如 Attraction、Restaurant</summary>
            public string place_category { get; set; }
        }

        /// <summary>
        /// 劇本的縣市、夜間模式、偏好標籤、節點景點與任務類型；找不到劇本回傳 null。
        /// story_node.place_id 是 Neo4j UUID 時經 place_type 換成景點名稱；
        /// Neo4j 連不上時生成的節點是 "place-{p_id}"，直接對 place 表。
        /// </summary>
        public async Task<StoryFacts> GetStoryFactsAsync(int storyId)
        {
            using var conn = Open();
            using var multi = await conn.QueryMultipleAsync(@"
                SELECT city_name, is_night_mode FROM story WHERE s_id = @storyId;
                SELECT s_tag FROM story_tag WHERE s_id = @storyId;
                SELECT DISTINCT COALESCE(pt.place_name, p.p_name) AS place_name, pt.place_category
                FROM story_node sn
                LEFT JOIN place_type pt ON pt.place_id = sn.place_id
                LEFT JOIN place p ON sn.place_id LIKE 'place-%'
                                 AND p.p_id = CAST(SUBSTRING(sn.place_id, 7) AS UNSIGNED)
                WHERE sn.s_id = @storyId;
                SELECT DISTINCT task_type FROM task WHERE story_id = @storyId;", new { storyId });

            StoryFacts facts = await multi.ReadFirstOrDefaultAsync<StoryFacts>();
            if (facts == null) return null;

            facts.tags = (await multi.ReadAsync<string>()).ToList();
            facts.places = (await multi.ReadAsync<StoryPlace>()).Where(p => !string.IsNullOrWhiteSpace(p.place_name)).ToList();
            facts.task_types = (await multi.ReadAsync<int>()).ToList();
            return facts;
        }

        /// <summary>story.story_badge 存可抽的勳章類別（逗號分隔）</summary>
        public async Task UpdateStoryBadgeAsync(int storyId, string categories)
        {
            using var conn = Open();
            await conn.ExecuteAsync("UPDATE story SET story_badge = @categories WHERE s_id = @storyId;", new { storyId, categories });
        }

        public async Task<bool> HasCompletedStoryAsync(int auId, int storyId)
        {
            using var conn = Open();
            return await conn.ExecuteScalarAsync<bool>(@"
                SELECT EXISTS (
                    SELECT 1 FROM story_session
                    WHERE au_id = @auId AND s_id = @storyId AND ss_status = 'completed'
                );", new { auId, storyId });
        }

        /// <summary>這個劇本已經抽到的勳章；還沒抽回傳 null</summary>
        public async Task<BadgeInfo> GetStoryDrawAsync(int auId, int storyId)
        {
            using var conn = Open();
            return await conn.QueryFirstOrDefaultAsync<BadgeInfo>(@"
                SELECT b.b_id, b.b_name, b.b_fication, b.b_thing, b.b_image
                FROM au_badge ab
                INNER JOIN badge b ON b.b_id = ab.b_id
                WHERE ab.au_id = @auId AND ab.s_id = @storyId
                LIMIT 1;", new { auId, storyId });
        }

        public async Task<List<int>> GetOwnedBadgeIdsAsync(int auId)
        {
            using var conn = Open();
            return (await conn.QueryAsync<int>("SELECT b_id FROM au_badge WHERE au_id = @auId;", new { auId })).ToList();
        }

        /// <summary>
        /// 寫入抽到的勳章。同一個劇本已經抽過（uk_au_badge_story）或已擁有這枚（uk_au_badge）時不寫入，回傳 false。
        /// </summary>
        public async Task<bool> TryInsertDrawAsync(int auId, int bId, int storyId)
        {
            using var conn = Open();
            return await conn.ExecuteAsync(
                "INSERT IGNORE INTO au_badge (au_id, b_id, s_id) VALUES (@auId, @bId, @storyId);",
                new { auId, bId, storyId }) > 0;
        }

        #endregion
    }
}
