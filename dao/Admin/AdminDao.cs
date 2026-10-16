// 管理員後台：用戶（auth）、劇本（story）、勳章（badge）的基礎維護。
using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using backend.utils;
using backend.ViewModels;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class AdminDao
    {
        private readonly AppSettings _appSettings;

        public AdminDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        private MySqlConnection Open()
        {
            var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            return connection;
        }

        private static string LikePattern(string keyword) =>
            string.IsNullOrWhiteSpace(keyword) ? null : $"%{keyword.Trim()}%";

        #region 用戶

        private const string UserColumns = @"
            a.au_id, a.auth_name, a.auth_email, a.auth_type, a.is_active, a.is_email_verified,
            a.au_birthday, a.au_gender, a.last_login_at, a.created_at, a.updated_at,
            (SELECT COUNT(1) FROM story s WHERE s.au_id = a.au_id) AS story_count,
            (SELECT COUNT(1) FROM au_badge ab WHERE ab.au_id = a.au_id) AS badge_count";

        private const string UserFilter = @"
            WHERE (@keyword IS NULL OR a.auth_name LIKE @keyword OR a.auth_email LIKE @keyword)
              AND (@authType IS NULL OR a.auth_type = @authType)
              AND (@isActive IS NULL OR a.is_active = @isActive)";

        public (List<AdminUserResponse> items, int total) ListUsers(AdminUserListQuery query, int pageSize, int offset)
        {
            string sql = $@"
                SELECT {UserColumns}
                FROM auth a
                {UserFilter}
                ORDER BY a.au_id DESC
                LIMIT @pageSize OFFSET @offset;

                SELECT COUNT(1) FROM auth a {UserFilter};
            ";

            using var connection = Open();
            using var multi = connection.QueryMultiple(sql, new
            {
                keyword = LikePattern(query.keyword),
                authType = query.auth_type,
                isActive = query.is_active,
                pageSize,
                offset
            });
            return (multi.Read<AdminUserResponse>().ToList(), multi.ReadFirst<int>());
        }

        public AdminUserResponse GetUser(int auId)
        {
            using var connection = Open();
            return connection.QueryFirstOrDefault<AdminUserResponse>(
                $"SELECT {UserColumns} FROM auth a WHERE a.au_id = @auId LIMIT 1;", new { auId });
        }

        public bool UserExists(int auId)
        {
            using var connection = Open();
            return connection.ExecuteScalar<int>("SELECT COUNT(1) FROM auth WHERE au_id = @auId;", new { auId }) > 0;
        }

        /// <summary>Email 是否已被其他帳號使用（exceptAuId 為修改中的帳號本身）</summary>
        public bool EmailTaken(string email, int? exceptAuId = null)
        {
            using var connection = Open();
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM auth WHERE auth_email = @email AND (@exceptAuId IS NULL OR au_id <> @exceptAuId);",
                new { email, exceptAuId }) > 0;
        }

        public int CreateUser(AdminUserCreateRequest req, string passwordHash)
        {
            const string sql = @"
                INSERT INTO auth
                    (auth_name, auth_type, auth_email, auth_pswd, is_active, is_email_verified, au_birthday, au_gender)
                VALUES
                    (@auth_name, @auth_type, @auth_email, @passwordHash, 1, 1, @au_birthday, @au_gender);
                SELECT LAST_INSERT_ID();
            ";

            using var connection = Open();
            return connection.ExecuteScalar<int>(sql, new
            {
                req.auth_name,
                req.auth_type,
                req.auth_email,
                passwordHash,
                req.au_birthday,
                req.au_gender
            });
        }

        public void UpdateUser(int auId, AdminUserUpdateRequest req)
        {
            const string sql = @"
                UPDATE auth
                SET auth_name = @auth_name,
                    auth_email = @auth_email,
                    auth_type = @auth_type,
                    is_active = @is_active,
                    is_email_verified = @is_email_verified,
                    au_birthday = @au_birthday,
                    au_gender = @au_gender
                WHERE au_id = @auId;
            ";

            using var connection = Open();
            connection.Execute(sql, new
            {
                auId,
                req.auth_name,
                req.auth_email,
                req.auth_type,
                req.is_active,
                req.is_email_verified,
                req.au_birthday,
                req.au_gender
            });
        }

        /// <summary>重設密碼，同時清掉還沒用掉的忘記密碼 Token</summary>
        public void UpdateUserPassword(int auId, string passwordHash)
        {
            using var connection = Open();
            connection.Execute(
                "UPDATE auth SET auth_pswd = @passwordHash, pwd_reset_token = NULL, pwd_reset_expires = NULL WHERE au_id = @auId;",
                new { auId, passwordHash });
        }

        public void SetUserActive(int auId, bool isActive)
        {
            using var connection = Open();
            connection.Execute("UPDATE auth SET is_active = @isActive WHERE au_id = @auId;", new { auId, isActive });
        }

        /// <summary>商家帳號綁定的景點 uid（store.store_uid）；不是商家或沒有店家資料時為 null</summary>
        public string GetStoreUid(int auId)
        {
            using var connection = Open();
            return connection.QueryFirstOrDefault<string>(
                "SELECT store_uid FROM store WHERE au_id = @auId AND store_uid IS NOT NULL AND store_uid <> '' LIMIT 1;", new { auId });
        }

        /// <summary>還有沒有啟用中的帳號綁定這個景點（同一個景點可能有多間店綁定）</summary>
        public bool HasActiveStore(string storeUid)
        {
            using var connection = Open();
            return connection.ExecuteScalar<bool>(@"
                SELECT EXISTS (
                    SELECT 1 FROM store s
                    INNER JOIN auth a ON a.au_id = s.au_id
                    WHERE s.store_uid = @storeUid AND a.is_active = 1);", new { storeUid });
        }

        #endregion

        #region 劇本

        private const string StoryColumns = @"
            s.s_id, s.au_id, a.auth_name, s.story_title, s.city_name, s.district_name,
            s.npc_id, n.npc_name, s.party_size, s.sd_transport,
            s.is_active, s.is_night_mode, s.is_favorite,
            (SELECT COUNT(1) FROM story_node sn WHERE sn.s_id = s.s_id) AS node_count,
            s.created_at, s.updated_at";

        private const string StoryJoins = @"
            FROM story s
            LEFT JOIN auth a ON a.au_id = s.au_id
            LEFT JOIN npc n ON n.npc_id = s.npc_id";

        private const string StoryFilter = @"
            WHERE (@keyword IS NULL OR s.story_title LIKE @keyword OR s.city_name LIKE @keyword OR s.district_name LIKE @keyword)
              AND (@auId IS NULL OR s.au_id = @auId)
              AND (@isActive IS NULL OR s.is_active = @isActive)";

        public (List<AdminStoryListItem> items, int total) ListStories(AdminStoryListQuery query, int pageSize, int offset)
        {
            string sql = $@"
                SELECT {StoryColumns}
                {StoryJoins}
                {StoryFilter}
                ORDER BY s.s_id DESC
                LIMIT @pageSize OFFSET @offset;

                SELECT COUNT(1) FROM story s {StoryFilter};
            ";

            using var connection = Open();
            using var multi = connection.QueryMultiple(sql, new
            {
                keyword = LikePattern(query.keyword),
                auId = query.au_id,
                isActive = query.is_active,
                pageSize,
                offset
            });
            return (multi.Read<AdminStoryListItem>().ToList(), multi.ReadFirst<int>());
        }

        public AdminStoryDetailResponse GetStory(int storyId)
        {
            string sql = $@"
                SELECT {StoryColumns},
                       s.story_prologue, s.story_synopsis, s.story_badge, s.story_postcards
                {StoryJoins}
                WHERE s.s_id = @storyId
                LIMIT 1;

                SELECT s_tag FROM story_tag WHERE s_id = @storyId ORDER BY s_tag;

                SELECT sn_id, sn_order, sn_title, place_id, npc_id, location_codename, sn_task_type,
                       is_hidden, is_night_only, is_active, sn_opening_text, sn_success_text, last_time
                FROM story_node
                WHERE s_id = @storyId
                ORDER BY sn_order, sn_id;
            ";

            using var connection = Open();
            using var multi = connection.QueryMultiple(sql, new { storyId });
            AdminStoryDetailResponse story = multi.ReadFirstOrDefault<AdminStoryDetailResponse>();
            if (story == null) return null;

            story.tags = multi.Read<string>().ToList();
            story.nodes = multi.Read<AdminStoryNodeItem>().ToList();
            return story;
        }

        public bool StoryExists(int storyId)
        {
            using var connection = Open();
            return connection.ExecuteScalar<int>("SELECT COUNT(1) FROM story WHERE s_id = @storyId;", new { storyId }) > 0;
        }

        public bool NpcExists(int npcId)
        {
            using var connection = Open();
            return connection.ExecuteScalar<int>("SELECT COUNT(1) FROM npc WHERE npc_id = @npcId;", new { npcId }) > 0;
        }

        public int CreateStory(AdminStoryCreateRequest req)
        {
            const string sql = @"
                INSERT INTO story
                    (au_id, npc_id, city_name, district_name, party_size, sd_transport,
                     story_title, story_prologue, story_synopsis, is_active, is_night_mode)
                VALUES
                    (@au_id, @npc_id, @city_name, @district_name, @party_size, @sd_transport,
                     @story_title, @story_prologue, @story_synopsis, 1, @is_night_mode);
                SELECT LAST_INSERT_ID();
            ";

            using var connection = Open();
            return connection.ExecuteScalar<int>(sql, new
            {
                req.au_id,
                req.npc_id,
                req.city_name,
                req.district_name,
                req.party_size,
                req.sd_transport,
                req.story_title,
                req.story_prologue,
                req.story_synopsis,
                req.is_night_mode
            });
        }

        public void UpdateStory(int storyId, AdminStoryUpdateRequest req)
        {
            const string sql = @"
                UPDATE story
                SET story_title = @story_title,
                    story_prologue = @story_prologue,
                    story_synopsis = @story_synopsis,
                    city_name = @city_name,
                    district_name = @district_name,
                    party_size = @party_size,
                    sd_transport = @sd_transport,
                    npc_id = @npc_id,
                    is_night_mode = @is_night_mode,
                    is_active = @is_active
                WHERE s_id = @storyId;
            ";

            using var connection = Open();
            connection.Execute(sql, new
            {
                storyId,
                req.story_title,
                req.story_prologue,
                req.story_synopsis,
                req.city_name,
                req.district_name,
                req.party_size,
                req.sd_transport,
                req.npc_id,
                req.is_night_mode,
                req.is_active
            });
        }

        /// <summary>劇本只做軟刪除（is_active = 0），玩家端的查詢都已經濾掉停用的劇本</summary>
        public void SetStoryActive(int storyId, bool isActive)
        {
            using var connection = Open();
            connection.Execute("UPDATE story SET is_active = @isActive WHERE s_id = @storyId;", new { storyId, isActive });
        }

        #endregion

        #region 勳章

        private const string BadgeColumns = @"
            b.b_id, b.b_fication, b.b_thing, b.b_name, b.b_image, b.is_active,
            (SELECT COUNT(1) FROM au_badge ab WHERE ab.b_id = b.b_id) AS owned_count,
            b.created_at, b.updated_at";

        private const string BadgeFilter = @"
            WHERE (@keyword IS NULL OR b.b_name LIKE @keyword OR b.b_thing LIKE @keyword)
              AND (@fication IS NULL OR b.b_fication = @fication)
              AND (@isActive IS NULL OR b.is_active = @isActive)";

        public (List<AdminBadgeResponse> items, int total) ListBadges(AdminBadgeListQuery query, int pageSize, int offset)
        {
            string sql = $@"
                SELECT {BadgeColumns}
                FROM badge b
                {BadgeFilter}
                ORDER BY b.b_id
                LIMIT @pageSize OFFSET @offset;

                SELECT COUNT(1) FROM badge b {BadgeFilter};
            ";

            using var connection = Open();
            using var multi = connection.QueryMultiple(sql, new
            {
                keyword = LikePattern(query.keyword),
                fication = string.IsNullOrWhiteSpace(query.b_fication) ? null : query.b_fication.Trim(),
                isActive = query.is_active,
                pageSize,
                offset
            });
            return (multi.Read<AdminBadgeResponse>().ToList(), multi.ReadFirst<int>());
        }

        public AdminBadgeResponse GetBadge(int badgeId)
        {
            using var connection = Open();
            return connection.QueryFirstOrDefault<AdminBadgeResponse>(
                $"SELECT {BadgeColumns} FROM badge b WHERE b.b_id = @badgeId LIMIT 1;", new { badgeId });
        }

        public bool BadgeExists(int badgeId)
        {
            using var connection = Open();
            return connection.ExecuteScalar<int>("SELECT COUNT(1) FROM badge WHERE b_id = @badgeId;", new { badgeId }) > 0;
        }

        public int CreateBadge(AdminBadgeSaveRequest req)
        {
            const string sql = @"
                INSERT INTO badge (b_fication, b_thing, b_name, b_image)
                VALUES (@b_fication, @b_thing, @b_name, @b_image);
                SELECT LAST_INSERT_ID();
            ";

            using var connection = Open();
            return connection.ExecuteScalar<int>(sql, new { req.b_fication, req.b_thing, req.b_name, req.b_image });
        }

        public void UpdateBadge(int badgeId, AdminBadgeUpdateRequest req)
        {
            const string sql = @"
                UPDATE badge
                SET b_fication = @b_fication,
                    b_thing = @b_thing,
                    b_name = @b_name,
                    b_image = @b_image,
                    is_active = @is_active
                WHERE b_id = @badgeId;
            ";

            using var connection = Open();
            connection.Execute(sql, new { badgeId, req.b_fication, req.b_thing, req.b_name, req.b_image, req.is_active });
        }

        /// <summary>
        /// 勳章只做軟刪除（is_active = 0）：圖鑑、首頁徽章數、抽勳章都會濾掉停用的勳章，
        /// 用戶已收集的紀錄（au_badge）保留，改回啟用就會回來。
        /// </summary>
        public void SetBadgeActive(int badgeId, bool isActive)
        {
            using var connection = Open();
            connection.Execute("UPDATE badge SET is_active = @isActive WHERE b_id = @badgeId;", new { badgeId, isActive });
        }

        #endregion
    }
}
