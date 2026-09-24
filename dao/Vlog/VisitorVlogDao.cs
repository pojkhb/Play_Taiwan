// 檔案路徑：System\dao\Vlog\VisitorVlogDao.cs
// 遊客 VLOG：story / story_session / story_node（+ place_type、place 帶出真實景點）/
// user_task_record + task + record_media（照片）/ au_vlog（結果）。
// user_task_record 沒有 story_id / node_id，要 JOIN task 才能篩出某個劇本、某個節點的作答紀錄。
// au_vlog 以「同一位使用者的同一個劇本只有一支 VLOG」為準（HistoryDao / HomeDao 都用 au_id + s_id JOIN）。
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
    public class VisitorVlogDao
    {
        private readonly AppSettings _appSettings;

        public VisitorVlogDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        private MySqlConnection Open() => new MySqlConnection(_appSettings.mydb);

        #region 劇本與遊玩紀錄

        public class StoryRow
        {
            public int s_id { get; set; }
            public int au_id { get; set; }
            public string story_title { get; set; }
        }

        public async Task<StoryRow> GetStoryAsync(int storyId)
        {
            using var conn = Open();
            return await conn.QueryFirstOrDefaultAsync<StoryRow>(
                "SELECT s_id, au_id, story_title FROM story WHERE s_id = @storyId;", new { storyId });
        }

        public class SessionRow
        {
            public string ss_status { get; set; }
            public DateTime? started_at { get; set; }
            public DateTime? last_played_at { get; set; }
            public DateTime? completed_at { get; set; }
        }

        /// <summary>使用者在這個劇本最近一次的遊玩紀錄；沒玩過回傳 null</summary>
        public async Task<SessionRow> GetSessionAsync(int auId, int storyId)
        {
            using var conn = Open();
            return await conn.QueryFirstOrDefaultAsync<SessionRow>(@"
                SELECT ss_status, started_at, last_played_at, completed_at
                FROM story_session
                WHERE au_id = @auId AND s_id = @storyId
                ORDER BY ss_id DESC
                LIMIT 1;", new { auId, storyId });
        }

        public class SpotRow
        {
            public int sn_id { get; set; }
            public int sn_order { get; set; }
            public string sn_title { get; set; }
            public string location_codename { get; set; }
            public int is_hidden { get; set; }
            public string place_name { get; set; }
            public string p_address { get; set; }
            public decimal? lat { get; set; }
            public decimal? lng { get; set; }

            /// <summary>玩家在這個節點第一次作答的時間</summary>
            public DateTime? first_at { get; set; }

            /// <summary>玩家在這個節點最後一次作答的時間</summary>
            public DateTime? last_at { get; set; }

            /// <summary>玩家在這個節點作答過的任務數</summary>
            public int task_count { get; set; }
        }

        /// <summary>
        /// 劇本所有節點（依順序），附真實景點名稱、地址、座標與玩家的作答統計。
        /// story_node.place_id 是 Neo4j UUID，先經 place_type 換成景點名稱，再用名稱對到 place。
        /// </summary>
        public async Task<List<SpotRow>> GetSpotsAsync(int auId, int storyId)
        {
            using var conn = Open();
            return (await conn.QueryAsync<SpotRow>(@"
                SELECT sn.sn_id, sn.sn_order, sn.sn_title, sn.location_codename, sn.is_hidden,
                       pt.place_name, p.p_address, p.p_latitude AS lat, p.p_longitude AS lng,
                       r.first_at, r.last_at, IFNULL(r.task_count, 0) AS task_count
                FROM story_node sn
                LEFT JOIN (SELECT place_id, MIN(place_name) AS place_name FROM place_type GROUP BY place_id) pt
                       ON pt.place_id = sn.place_id
                LEFT JOIN place p
                       ON p.p_id = (SELECT MIN(p2.p_id) FROM place p2 WHERE p2.p_name = pt.place_name)
                LEFT JOIN (
                    SELECT t.node_id,
                           MIN(utr.answered_at) AS first_at,
                           MAX(utr.answered_at) AS last_at,
                           COUNT(DISTINCT utr.task_id) AS task_count
                    FROM user_task_record utr
                    INNER JOIN task t ON t.task_id = utr.task_id
                    WHERE utr.au_id = @auId AND t.story_id = @storyId
                    GROUP BY t.node_id
                ) r ON r.node_id = sn.sn_id
                WHERE sn.s_id = @storyId
                ORDER BY sn.sn_order, sn.sn_id;", new { auId, storyId })).ToList();
        }

        public class PhotoRow
        {
            /// <summary>照片所屬節點（task.node_id），任務沒掛節點時為 null</summary>
            public int? node_id { get; set; }
            public DateTime answered_at { get; set; }
            public string url { get; set; }
        }

        /// <summary>
        /// 玩家在這個劇本拍的所有素材（依作答時間）：record_media 與 user_task_record.answer_media_url 都算。
        /// 可能含影片、錄音與重複網址，由上層過濾。
        /// </summary>
        public async Task<List<PhotoRow>> GetMediaAsync(int auId, int storyId)
        {
            using var conn = Open();
            return (await conn.QueryAsync<PhotoRow>(@"
                SELECT t.node_id, utr.answered_at, rm.media_url AS url
                FROM user_task_record utr
                INNER JOIN task t          ON t.task_id = utr.task_id
                INNER JOIN record_media rm ON rm.record_id = utr.record_id
                WHERE utr.au_id = @auId AND t.story_id = @storyId AND rm.media_url <> ''
                UNION ALL
                SELECT t.node_id, utr.answered_at, utr.answer_media_url
                FROM user_task_record utr
                INNER JOIN task t ON t.task_id = utr.task_id
                WHERE utr.au_id = @auId AND t.story_id = @storyId
                  AND utr.answer_media_url IS NOT NULL AND utr.answer_media_url <> ''
                ORDER BY answered_at;", new { auId, storyId })).ToList();
        }

        #endregion

        #region au_vlog

        public class AuVlogRow
        {
            public int av_id { get; set; }
            public int au_id { get; set; }
            public int? s_id { get; set; }
            public string av_title { get; set; }
            public string av_final_script { get; set; }
            public string av_seo_keywords { get; set; }
            public string av_promo_copy { get; set; }
            public string av_video_url { get; set; }
            public string av_thumbnail { get; set; }

            /// <summary>1=待處理、2=處理中、3=已完成、4=失敗</summary>
            public int av_vlog_status { get; set; }
            public string error_message { get; set; }
            public DateTime updated_at { get; set; }
        }

        public async Task<AuVlogRow> GetVlogAsync(int auId, int storyId)
        {
            using var conn = Open();
            return await conn.QueryFirstOrDefaultAsync<AuVlogRow>(@"
                SELECT av_id, au_id, s_id, av_title, av_final_script, av_seo_keywords, av_promo_copy,
                       av_video_url, av_thumbnail, av_vlog_status, error_message, updated_at
                FROM au_vlog
                WHERE au_id = @auId AND s_id = @storyId
                ORDER BY av_id DESC
                LIMIT 1;", new { auId, storyId });
        }

        /// <summary>寫入草稿（標題、宣傳文案、關鍵字），已存在就更新；不動影片與狀態。回傳 av_id</summary>
        public async Task<int> SaveDraftAsync(int auId, int storyId, string title, string promoCopy, string seoKeywords)
        {
            using var conn = Open();
            int? avId = await conn.ExecuteScalarAsync<int?>(@"
                SELECT av_id FROM au_vlog WHERE au_id = @auId AND s_id = @storyId ORDER BY av_id DESC LIMIT 1;",
                new { auId, storyId });

            if (avId.HasValue)
            {
                await conn.ExecuteAsync(@"
                    UPDATE au_vlog
                    SET av_title = @title, av_promo_copy = @promoCopy, av_seo_keywords = @seoKeywords
                    WHERE av_id = @avId;", new { avId, title, promoCopy, seoKeywords });
                return avId.Value;
            }

            return await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO au_vlog (au_id, s_id, av_title, av_promo_copy, av_seo_keywords, av_vlog_status)
                VALUES (@auId, @storyId, @title, @promoCopy, @seoKeywords, 1);
                SELECT LAST_INSERT_ID();", new { auId, storyId, title, promoCopy, seoKeywords });
        }

        public async Task MarkProcessingAsync(int avId, string finalScript, string promoCopy, string seoKeywords)
        {
            using var conn = Open();
            await conn.ExecuteAsync(@"
                UPDATE au_vlog
                SET av_final_script = @finalScript, av_promo_copy = @promoCopy, av_seo_keywords = @seoKeywords,
                    av_video_url = NULL, av_vlog_status = 2, error_message = NULL
                WHERE av_id = @avId;", new { avId, finalScript, promoCopy, seoKeywords });
        }

        public async Task MarkCompletedAsync(int avId, string videoUrl)
        {
            using var conn = Open();
            await conn.ExecuteAsync(@"
                UPDATE au_vlog
                SET av_video_url = @videoUrl, av_vlog_status = 3, error_message = NULL
                WHERE av_id = @avId;", new { avId, videoUrl });
        }

        public async Task MarkFailedAsync(int avId, string errorMessage)
        {
            using var conn = Open();
            await conn.ExecuteAsync(@"
                UPDATE au_vlog
                SET av_vlog_status = 4, error_message = @errorMessage
                WHERE av_id = @avId;", new { avId, errorMessage });
        }

        #endregion
    }
}
