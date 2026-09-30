using System.Collections.Generic;
using System.Linq;
using Dapper;
using backend.utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    /// <summary>
    /// 任務動態難度用的玩家紀錄查詢（見 TaskDifficultyService）：去過哪些景點、最近的答題表現、作答過哪些題型。
    /// </summary>
    public class TaskDifficultyDao
    {
        private readonly AppSettings _appSettings;

        public TaskDifficultyDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        /// <summary>玩家去過的景點，連同所屬劇本生成時的縣市／鄉鎮區（景點查不到行政區時的備援）</summary>
        public class VisitedPlaceRow
        {
            public string place_id { get; set; }
            public string city_name { get; set; }
            public string district_name { get; set; }
        }

        /// <summary>
        /// 玩家去過的景點：自己抵達過的站（story_session.ss_current 以內），加上自己作答過的站。
        /// 作答時也檢查過位置，而協作時隊友不一定每站都有按抵達（抵達只推進按的人自己的 ss_current）。
        /// </summary>
        public List<VisitedPlaceRow> GetVisitedPlaces(int auId)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            return conn.Query<VisitedPlaceRow>(@"
                SELECT sn.place_id, s.city_name, s.district_name
                FROM story_session ss
                INNER JOIN story s ON s.s_id = ss.s_id
                INNER JOIN story_node sn ON sn.s_id = ss.s_id AND sn.sn_order <= ss.ss_current
                WHERE ss.au_id = @auId AND sn.place_id IS NOT NULL AND sn.place_id <> ''
                UNION
                SELECT sn.place_id, s.city_name, s.district_name
                FROM user_task_record r
                INNER JOIN task t ON t.task_id = r.task_id
                INNER JOIN story_node sn ON sn.sn_id = t.node_id
                INNER JOIN story s ON s.s_id = sn.s_id
                WHERE r.au_id = @auId AND sn.place_id IS NOT NULL AND sn.place_id <> '';", new { auId }).ToList();
        }

        /// <summary>一題有對錯的任務：玩家作答幾次、是否已答對</summary>
        public class GradedTaskRow
        {
            public int task_id { get; set; }
            public int attempts { get; set; }
            public int solved { get; set; }
        }

        /// <summary>
        /// 玩家最近作答（依最後作答時間）的 limit 題有對錯的任務。
        /// 答對後就不能再作答，所以「只作答一次且答對」就是第一次就答對。人工審核中（is_correct 為 NULL）的不算。
        /// </summary>
        public List<GradedTaskRow> GetRecentGradedTasks(int auId, int[] gradedTypeIds, int limit)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            return conn.Query<GradedTaskRow>($@"
                SELECT r.task_id, COUNT(*) AS attempts, MAX(r.is_correct = 1) AS solved
                FROM user_task_record r
                INNER JOIN task t ON t.task_id = r.task_id
                WHERE r.au_id = @auId AND r.is_correct IS NOT NULL AND t.task_type IN @gradedTypeIds
                GROUP BY r.task_id
                ORDER BY MAX(r.answered_at) DESC, r.task_id DESC
                LIMIT {limit};", new { auId, gradedTypeIds }).ToList();
        }

        /// <summary>玩家作答過的題型（task.task_type）</summary>
        public HashSet<int> GetAnsweredTypeIds(int auId)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            return conn.Query<int>(@"
                SELECT DISTINCT t.task_type
                FROM user_task_record r
                INNER JOIN task t ON t.task_id = r.task_id
                WHERE r.au_id = @auId;", new { auId }).ToHashSet();
        }

        /// <summary>題型名稱（type.type_name）</summary>
        public Dictionary<int, string> GetTypeNames(int[] typeIds)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            return conn.Query<(int type_id, string type_name)>(
                    "SELECT type_id, type_name FROM `type` WHERE type_id IN @typeIds;", new { typeIds })
                .GroupBy(r => r.type_id)
                .ToDictionary(g => g.Key, g => g.First().type_name);
        }
    }
}
