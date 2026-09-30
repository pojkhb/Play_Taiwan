// 檔案路徑：System\dao\Pair\PairDao.cs
// 協作解謎配對：story_pair_session（隊伍場次、四位數配對碼）+ story_pair_member（成員與座位）。
// 配對碼只保證「候位中」不重複：active_code 是虛擬欄位（waiting 時等於 pair_code，其餘為 NULL）並有唯一索引，
// 所以場次結束（locked / expired / completed / cancelled）後號碼會自動釋出給其他隊伍使用。
using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using backend.Models;
using backend.utils;
using backend.ViewModels;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class PairDao
    {
        private readonly AppSettings _appSettings;

        public PairDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        // 同一份劇本同時只會有一支「進行中」的隊伍（程式控制，資料表沒有限制）
        private const string ActiveStatuses = "('waiting', 'locked', 'expired')";

        // 配對碼撞號（候位中已有人用）時換一組重試的次數
        private const int MaxCodeAttempts = 30;

        public class StoryRow
        {
            public int s_id { get; set; }
            public int au_id { get; set; }
            public int? party_size { get; set; }
            public string story_title { get; set; }
        }

        public class SessionRow
        {
            public int pair_id { get; set; }
            public int s_id { get; set; }
            public string pair_code { get; set; }
            public string pair_status { get; set; }
            public DateTime? expires_at { get; set; }
            public long? expires_in_seconds { get; set; }
        }

        private class MemberRow
        {
            public int member_id { get; set; }
            public int au_id { get; set; }
            public int seat_no { get; set; }
            public bool is_host { get; set; }
        }

        #region 產生配對碼（擁有者）
        /// <summary>
        /// 劇本擁有者產生配對碼，回傳隊伍 pair_id：
        /// - 還沒有隊伍：建立隊伍（waiting），擁有者自己坐 1 號座位（隊長）
        /// - 隊伍 waiting 且配對碼仍有效：沿用原配對碼
        /// - 隊伍 locked 有空位（有人退出）或配對碼已逾期：重新開放，給一組新配對碼
        /// - 隊伍已滿：409
        /// </summary>
        public int IssueCode(int auId, int storyId, int validMinutes)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();

            // 逾期處理獨立生效，不跟後面的交易一起回滾（例如配對碼錯誤時）
            ExpireStaleCodes(conn, null);

            using var transaction = conn.BeginTransaction();

            try
            {

                // 鎖住劇本，避免同一份劇本同時建出兩支隊伍
                StoryRow story = conn.QueryFirstOrDefault<StoryRow>(@"
                    SELECT s_id, au_id, party_size, story_title FROM story
                    WHERE s_id = @storyId AND is_active = 1
                    FOR UPDATE;", new { storyId }, transaction);

                if (story == null) throw new NotFoundException($"找不到 story_id={storyId} 的劇本");
                if (story.au_id != auId) throw new UnauthorizedAccessException("只有劇本擁有者可以產生配對碼");
                if ((story.party_size ?? 0) < 2) throw new ConflictException("這份劇本是單人劇本，沒有協作解謎，不需要配對");

                SessionRow current = GetCurrentSession(conn, transaction, storyId, forUpdate: true);
                int pairId;

                if (current == null)
                {
                    pairId = WithNewCode(code => conn.ExecuteScalar<int>(@"
                        INSERT INTO story_pair_session (s_id, pair_code, pair_status, expires_at)
                        VALUES (@storyId, @code, 'waiting', DATE_ADD(NOW(), INTERVAL @validMinutes MINUTE));
                        SELECT LAST_INSERT_ID();", new { storyId, code, validMinutes }, transaction));

                    conn.Execute(@"
                        INSERT INTO story_pair_member (pair_id, au_id, seat_no, is_host)
                        VALUES (@pairId, @auId, 1, 1);", new { pairId, auId }, transaction);
                }
                else
                {
                    pairId = current.pair_id;

                    if (current.pair_status != "waiting")
                    {
                        int memberCount = conn.ExecuteScalar<int>(
                            "SELECT COUNT(*) FROM story_pair_member WHERE pair_id = @pairId;", new { pairId }, transaction);

                        if (memberCount >= story.party_size)
                            throw new ConflictException("隊伍已滿，不需要再產生配對碼");

                        WithNewCode(code => conn.Execute(@"
                            UPDATE story_pair_session
                            SET pair_code = @code,
                                pair_status = 'waiting',
                                expires_at = DATE_ADD(NOW(), INTERVAL @validMinutes MINUTE),
                                locked_at = NULL
                            WHERE pair_id = @pairId;", new { pairId, code, validMinutes }, transaction));
                    }
                }

                transaction.Commit();
                return pairId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        #endregion

        #region 輸入配對碼加入
        /// <summary>
        /// 輸入配對碼加入隊伍，坐最小的空座位；加入後人數到齊（劇本 party_size）就自動鎖定，配對碼隨即失效。
        /// 已經是成員時直接回傳原隊伍。回傳 (pair_id, 劇本 s_id)。
        /// </summary>
        public (int pairId, int storyId) Join(int auId, string code)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();

            // 逾期處理獨立生效，不跟後面的交易一起回滾（例如配對碼錯誤時）
            ExpireStaleCodes(conn, null);

            using var transaction = conn.BeginTransaction();

            try
            {

                // 鎖住隊伍，避免兩個人同時搶到同一個座位
                SessionRow session = conn.QueryFirstOrDefault<SessionRow>(@"
                    SELECT pair_id, s_id, pair_code, pair_status, expires_at
                    FROM story_pair_session
                    WHERE active_code = @code
                    FOR UPDATE;", new { code }, transaction);

                if (session == null) throw new NotFoundException("配對碼錯誤或已過期，請向隊長確認最新的配對碼");

                int partySize = conn.ExecuteScalar<int?>(
                    "SELECT party_size FROM story WHERE s_id = @storyId;", new { storyId = session.s_id }, transaction) ?? 0;

                List<MemberRow> members = conn.Query<MemberRow>(@"
                    SELECT member_id, au_id, seat_no, (is_host = 1) AS is_host
                    FROM story_pair_member
                    WHERE pair_id = @pairId
                    FOR UPDATE;", new { pairId = session.pair_id }, transaction).ToList();

                if (members.Any(m => m.au_id == auId))
                {
                    transaction.Commit();
                    return (session.pair_id, session.s_id);
                }

                int seat = Enumerable.Range(1, Math.Max(partySize, 0))
                    .FirstOrDefault(s => members.All(m => m.seat_no != s));

                if (seat == 0) throw new ConflictException("隊伍已滿，無法加入");

                conn.Execute(@"
                    INSERT INTO story_pair_member (pair_id, au_id, seat_no, is_host)
                    VALUES (@pairId, @auId, @seat, 0);", new { pairId = session.pair_id, auId, seat }, transaction);

                if (members.Count + 1 >= partySize)
                {
                    conn.Execute(@"
                        UPDATE story_pair_session
                        SET pair_status = 'locked', locked_at = NOW()
                        WHERE pair_id = @pairId;", new { pairId = session.pair_id }, transaction);
                }

                transaction.Commit();
                return (session.pair_id, session.s_id);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        #endregion

        #region 退出 / 取消
        /// <summary>
        /// 成員退出隊伍：隊伍進度先寫進留下的人各自的遊玩紀錄，再刪除成員資料，並把他在這份劇本的遊玩狀態改成暫停。
        /// 隊長（劇本擁有者）不能退出，要改用取消隊伍。已鎖定的隊伍有人退出後維持 locked，隊長可重新產生配對碼補人。
        /// </summary>
        public void Leave(int auId, int storyId)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                SessionRow session = GetCurrentSession(conn, transaction, storyId, forUpdate: true);
                MemberRow member = session == null ? null : conn.QueryFirstOrDefault<MemberRow>(@"
                    SELECT member_id, au_id, seat_no, (is_host = 1) AS is_host
                    FROM story_pair_member
                    WHERE pair_id = @pairId AND au_id = @auId;", new { pairId = session.pair_id, auId }, transaction);

                if (member == null) throw new NotFoundException("你不在這份劇本的協作隊伍中");
                if (member.is_host) throw new ConflictException("隊長（劇本擁有者）不能退出隊伍，請改用取消隊伍");

                // 退出前先把隊伍進度留給擁有者與其他隊員，避免少了這位隊員的抵達紀錄後進度倒退
                int ownerId = conn.ExecuteScalar<int>("SELECT au_id FROM story WHERE s_id = @storyId;", new { storyId }, transaction);
                int teamOrder = TeamProgress.GetCurrentNodeOrder(conn, ownerId, storyId, transaction);
                List<int> staying = conn.Query<int>(
                    "SELECT au_id FROM story_pair_member WHERE pair_id = @pairId AND au_id <> @auId;",
                    new { pairId = session.pair_id, auId }, transaction).Append(ownerId).ToList();
                TeamProgress.KeepProgress(conn, transaction, storyId, teamOrder, staying);

                conn.Execute("DELETE FROM story_pair_member WHERE member_id = @memberId;", new { memberId = member.member_id }, transaction);
                PauseStorySessions(conn, transaction, storyId, new[] { auId });

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// 隊長（劇本擁有者）取消隊伍：隊伍進度先寫進隊長的遊玩紀錄，隊伍改成 cancelled（配對碼釋出、成員不能再用隊伍身分作答），
        /// 其他成員在這份劇本的遊玩狀態改成暫停。
        /// </summary>
        public void Cancel(int auId, int storyId)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                int? ownerId = conn.ExecuteScalar<int?>(
                    "SELECT au_id FROM story WHERE s_id = @storyId FOR UPDATE;", new { storyId }, transaction);

                if (ownerId == null) throw new NotFoundException($"找不到 story_id={storyId} 的劇本");
                if (ownerId != auId) throw new UnauthorizedAccessException("只有劇本擁有者可以取消隊伍");

                SessionRow session = GetCurrentSession(conn, transaction, storyId, forUpdate: true);
                if (session == null) throw new NotFoundException("這份劇本目前沒有協作隊伍");

                // 取消前先把隊伍進度留給擁有者，避免取消後只剩自己抵達過的站而倒退
                int teamOrder = TeamProgress.GetCurrentNodeOrder(conn, auId, storyId, transaction);
                TeamProgress.KeepProgress(conn, transaction, storyId, teamOrder, new[] { auId });

                conn.Execute(
                    "UPDATE story_pair_session SET pair_status = 'cancelled' WHERE pair_id = @pairId;",
                    new { pairId = session.pair_id }, transaction);

                List<int> memberIds = conn.Query<int>(
                    "SELECT au_id FROM story_pair_member WHERE pair_id = @pairId AND is_host = 0;",
                    new { pairId = session.pair_id }, transaction).ToList();
                PauseStorySessions(conn, transaction, storyId, memberIds);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        #endregion

        #region 查詢
        public StoryRow GetStory(int storyId)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            return conn.QueryFirstOrDefault<StoryRow>(
                "SELECT s_id, au_id, party_size, story_title FROM story WHERE s_id = @storyId;", new { storyId });
        }

        /// <summary>劇本目前進行中的隊伍（waiting / locked / expired），沒有時回傳 null；查詢前先處理逾期的配對碼。</summary>
        public SessionRow GetCurrentSession(int storyId)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            conn.Open();
            ExpireStaleCodes(conn, null);
            return GetCurrentSession(conn, null, storyId, forUpdate: false);
        }

        public List<PairMemberItem> GetMembers(int pairId)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            return conn.Query<PairMemberItem>(@"
                SELECT pm.au_id, a.auth_name, pm.seat_no, (pm.is_host = 1) AS is_host, pm.joined_at
                FROM story_pair_member pm
                INNER JOIN auth a ON a.au_id = pm.au_id
                WHERE pm.pair_id = @pairId
                ORDER BY pm.seat_no;", new { pairId }).ToList();
        }
        #endregion

        #region 共用
        /// <summary>配對碼已過期但狀態還是 waiting 的隊伍改成 expired，讓號碼釋出（active_code 變 NULL）。</summary>
        private static void ExpireStaleCodes(MySqlConnection conn, MySqlTransaction transaction)
        {
            conn.Execute(@"
                UPDATE story_pair_session
                SET pair_status = 'expired'
                WHERE pair_status = 'waiting' AND expires_at IS NOT NULL AND expires_at < NOW();",
                transaction: transaction);
        }

        private static SessionRow GetCurrentSession(MySqlConnection conn, MySqlTransaction transaction, int storyId, bool forUpdate)
        {
            return conn.QueryFirstOrDefault<SessionRow>($@"
                SELECT pair_id, s_id, pair_code, pair_status, expires_at,
                       CASE WHEN pair_status = 'waiting'
                            THEN GREATEST(TIMESTAMPDIFF(SECOND, NOW(), expires_at), 0) END AS expires_in_seconds
                FROM story_pair_session
                WHERE s_id = @storyId AND pair_status IN {ActiveStatuses}
                ORDER BY pair_id DESC
                LIMIT 1
                {(forUpdate ? "FOR UPDATE" : "")};", new { storyId }, transaction);
        }

        private static void PauseStorySessions(MySqlConnection conn, MySqlTransaction transaction, int storyId, IEnumerable<int> auIds)
        {
            List<int> ids = auIds.ToList();
            if (ids.Count == 0) return;

            conn.Execute(@"
                UPDATE story_session
                SET ss_status = 'paused'
                WHERE s_id = @storyId AND au_id IN @ids AND ss_status = 'in_progress';",
                new { storyId, ids }, transaction);
        }

        /// <summary>產生隨機四位數配對碼寫入；撞到候位中的號碼（唯一索引 uk_sps_active_code）就換一組重試。</summary>
        private static T WithNewCode<T>(Func<string, T> write)
        {
            for (int attempt = 0; attempt < MaxCodeAttempts; attempt++)
            {
                string code = Random.Shared.Next(0, 10000).ToString("D4");
                try
                {
                    return write(code);
                }
                catch (MySqlException ex) when (ex.Number == 1062)
                {
                    // 號碼正被其他候位中的隊伍使用，換一組
                }
            }

            throw new ConflictException("目前可用的配對碼不足，請稍後再試");
        }
        #endregion
    }
}
