// 檔案路徑：System\dao\Pair\TeamProgress.cs
// 協作隊伍共用進度：登入者與同一支隊伍（未取消）的成員看同一個進度，
// 取所有人在這份劇本最遠抵達的節點順序（story_session.ss_current）。
// 沒有隊伍時就是登入者自己的進度。地圖解鎖、抵達檢查、任務門檻都用這個。
// 另外負責「誰能玩這份劇本」：劇本擁有者，或協作隊伍（未取消）的成員。
using System.Collections.Generic;
using System.Linq;
using Dapper;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public static class TeamProgress
    {
        private const string CurrentNodeOrderSql = @"
            SELECT COALESCE(MAX(ss.ss_current), 0)
            FROM story_session ss
            WHERE ss.s_id = @storyId
              AND ss.au_id IN (
                  SELECT @auId
                  UNION
                  SELECT pm.au_id
                  FROM story_pair_member pm
                  WHERE pm.pair_id = (
                      SELECT me.pair_id
                      FROM story_pair_member me
                      INNER JOIN story_pair_session ps ON ps.pair_id = me.pair_id
                      WHERE me.au_id = @auId AND ps.s_id = @storyId AND ps.pair_status <> 'cancelled'
                      ORDER BY me.pair_id DESC
                      LIMIT 1));";

        /// <summary>登入者（及其隊伍）在這份劇本最遠抵達的節點順序，還沒抵達過任何一站為 0。</summary>
        public static int GetCurrentNodeOrder(MySqlConnection conn, int auId, int storyId, MySqlTransaction transaction = null)
        {
            return conn.ExecuteScalar<int>(CurrentNodeOrderSql, new { auId, storyId }, transaction);
        }

        /// <summary>節點是否已解鎖：第一站固定開放，之後抵達第 N 站就解鎖第 N+1 站。</summary>
        public static bool IsUnlocked(int nodeOrder, int currentNodeOrder)
        {
            return nodeOrder == 1 || nodeOrder <= currentNodeOrder + 1;
        }

        /// <summary>節點是否已被隊伍抵達過（抵達過才能看、做這一站的任務）。</summary>
        public static bool IsReached(int nodeOrder, int currentNodeOrder)
        {
            return nodeOrder <= currentNodeOrder;
        }

        /// <summary>
        /// 登入者能不能玩這份劇本：劇本擁有者，或這份劇本協作隊伍（未取消）的成員。
        /// 確認開始、地圖、抵達、結束劇本都用這個判斷（任務相關見 TaskDao.GetStoryAccess，規則相同）。
        /// </summary>
        public static bool CanPlay(MySqlConnection conn, int auId, int storyId)
        {
            return conn.ExecuteScalar<bool>(@"
                SELECT EXISTS (SELECT 1 FROM story WHERE s_id = @storyId AND au_id = @auId)
                    OR EXISTS (SELECT 1
                               FROM story_pair_member pm
                               INNER JOIN story_pair_session ps ON ps.pair_id = pm.pair_id
                               WHERE pm.au_id = @auId AND ps.s_id = @storyId AND ps.pair_status <> 'cancelled');",
                new { auId, storyId });
        }

        /// <summary>
        /// 把隊伍進度寫進指定成員各自的遊玩紀錄（隊員退出或隊伍取消前呼叫），
        /// 之後不再共用進度時，留下的人不會倒退回自己抵達過的站。
        /// 沒有遊玩紀錄的人（例如擁有者沒按過確認開始）補一筆暫停中的紀錄保存進度。
        /// </summary>
        public static void KeepProgress(MySqlConnection conn, MySqlTransaction transaction, int storyId, int nodeOrder, IEnumerable<int> auIds)
        {
            List<int> ids = auIds.Distinct().ToList();
            if (ids.Count == 0 || nodeOrder <= 0) return;

            conn.Execute(@"
                UPDATE story_session
                SET ss_current = GREATEST(ss_current, @nodeOrder)
                WHERE s_id = @storyId AND au_id IN @ids;", new { storyId, nodeOrder, ids }, transaction);

            conn.Execute(@"
                INSERT INTO story_session (au_id, s_id, ss_current, ss_status, started_at, last_played_at)
                SELECT a.au_id, @storyId, @nodeOrder, 'paused', NOW(), NOW()
                FROM auth a
                WHERE a.au_id IN @ids
                  AND NOT EXISTS (SELECT 1 FROM story_session ss WHERE ss.au_id = a.au_id AND ss.s_id = @storyId);",
                new { storyId, nodeOrder, ids }, transaction);
        }
    }
}
