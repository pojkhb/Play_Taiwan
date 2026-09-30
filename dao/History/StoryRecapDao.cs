// 檔案路徑：System\dao\History\StoryRecapDao.cs
// 劇本回顧要用的資料：story、story_session、story_node（橋接 place 取真正的景點）、task + user_task_record、postcard、au_badge
// 玩家拍的照片與 Vlog 沿用 VisitorVlogDao
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using backend.utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class StoryRecapDao
    {
        private readonly AppSettings _appSettings;

        public StoryRecapDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        private MySqlConnection Open() => new MySqlConnection(_appSettings.mydb);

        public class StoryRow
        {
            public int s_id { get; set; }
            public string story_title { get; set; }
            public string story_prologue { get; set; }
            public string story_synopsis { get; set; }
            public string city_name { get; set; }
            public string district_name { get; set; }
            public int is_night_mode { get; set; }
        }

        public async Task<StoryRow> GetStoryAsync(int storyId)
        {
            using var conn = Open();
            return await conn.QueryFirstOrDefaultAsync<StoryRow>(@"
                SELECT s_id, story_title, story_prologue, story_synopsis, city_name, district_name, is_night_mode
                FROM story
                WHERE s_id = @storyId AND is_active = 1;", new { storyId });
        }

        public class CompletedSession
        {
            public DateTime? started_at { get; set; }
            public DateTime? completed_at { get; set; }
        }

        /// <summary>使用者最近一次玩完這個劇本的紀錄；沒玩完回傳 null</summary>
        public async Task<CompletedSession> GetCompletedSessionAsync(int auId, int storyId)
        {
            using var conn = Open();
            return await conn.QueryFirstOrDefaultAsync<CompletedSession>(@"
                SELECT started_at, completed_at
                FROM story_session
                WHERE au_id = @auId AND s_id = @storyId AND ss_status = 'completed'
                ORDER BY completed_at DESC, ss_id DESC
                LIMIT 1;", new { auId, storyId });
        }

        public class NodeRow
        {
            public int sn_id { get; set; }
            public int sn_order { get; set; }
            public string sn_title { get; set; }
            public string location_codename { get; set; }
            public string sn_opening_text { get; set; }
            public string sn_success_text { get; set; }
            public string place_name { get; set; }
            public string place_image { get; set; }
        }

        public async Task<List<NodeRow>> GetNodesAsync(int storyId)
        {
            using var conn = Open();
            return (await conn.QueryAsync<NodeRow>($@"
                SELECT sn.sn_id, sn.sn_order, sn.sn_title, sn.location_codename, sn.sn_opening_text, sn.sn_success_text,
                       p.p_name AS place_name, NULLIF(p.p_image, '') AS place_image
                FROM story_node sn
                {MapDao.PlaceBridgeJoin}
                WHERE sn.s_id = @storyId
                ORDER BY sn.sn_order, sn.sn_id;", new { storyId })).ToList();
        }

        public class TaskRow
        {
            public int task_id { get; set; }
            public int? node_id { get; set; }
            public string type_name { get; set; }
            public string question { get; set; }
            public bool answered { get; set; }

            /// <summary>任一次作答答對＝1、都答錯＝0、沒有對錯（例如拍照）或沒作答＝null</summary>
            public int? any_correct { get; set; }
        }

        /// <summary>劇本的所有任務，以及這位使用者的作答結果</summary>
        public async Task<List<TaskRow>> GetTasksAsync(int auId, int storyId)
        {
            using var conn = Open();
            return (await conn.QueryAsync<TaskRow>(@"
                SELECT t.task_id, t.node_id, ty.type_name,
                       COALESCE(NULLIF(t.task_describe, ''), sq.question_describe) AS question,
                       (r.task_id IS NOT NULL) AS answered,
                       r.any_correct
                FROM task t
                LEFT JOIN `type` ty          ON ty.type_id = t.task_type
                LEFT JOIN store_question sq  ON sq.question_id = t.question_id
                LEFT JOIN (SELECT task_id, MAX(is_correct) AS any_correct
                             FROM user_task_record
                            WHERE au_id = @auId
                            GROUP BY task_id) r ON r.task_id = t.task_id
                WHERE t.story_id = @storyId
                ORDER BY t.node_id, t.task_id;", new { auId, storyId })).ToList();
        }

        public class PostcardRow
        {
            public int p_id { get; set; }
            public string p_name { get; set; }
            public string p_imag_url { get; set; }
            public int is_night { get; set; }
        }

        public async Task<List<PostcardRow>> GetPostcardsAsync(int auId, int storyId)
        {
            using var conn = Open();
            return (await conn.QueryAsync<PostcardRow>(@"
                SELECT p_id, p_name, p_imag_url, is_night
                FROM postcard
                WHERE au_id = @auId AND s_id = @storyId
                ORDER BY created_at, p_id;", new { auId, storyId })).ToList();
        }

        public class BadgeRow
        {
            public int b_id { get; set; }
            public string b_name { get; set; }
            public string b_fication { get; set; }
            public string b_image { get; set; }
        }

        /// <summary>這個劇本抽到的勳章（一個劇本只能抽一次）；還沒抽回傳 null</summary>
        public async Task<BadgeRow> GetBadgeAsync(int auId, int storyId)
        {
            using var conn = Open();
            return await conn.QueryFirstOrDefaultAsync<BadgeRow>(@"
                SELECT b.b_id, b.b_name, b.b_fication, b.b_image
                FROM au_badge ab
                INNER JOIN badge b ON b.b_id = ab.b_id
                WHERE ab.au_id = @auId AND ab.s_id = @storyId
                ORDER BY ab.ab_id
                LIMIT 1;", new { auId, storyId });
        }
    }
}
