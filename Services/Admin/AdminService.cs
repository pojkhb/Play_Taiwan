using System;
using System.Collections.Generic;
using System.Linq;
using backend.dao;
using backend.Models;
using backend.utils;
using backend.ViewModels;
using Microsoft.Extensions.Options;

namespace backend.Services
{
    /// <summary>
    /// 管理員後台：用戶、劇本、勳章的基礎維護。
    /// 用戶、劇本、勳章的「刪除」都是軟刪除（is_active = 0），資料都保留，可以再用修改 API 改回啟用。
    /// </summary>
    public class AdminService
    {
        private const int MaxPageSize = 100;
        private const int AdminAuthType = 3;

        private readonly AdminDao _dao;
        private readonly AppSettings _appSettings;

        public AdminService(AdminDao dao, IOptions<AppSettings> appSettings)
        {
            _dao = dao;
            _appSettings = appSettings.Value;
        }

        private static (int page, int pageSize, int offset) Paging(int page, int pageSize)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
            return (page, pageSize, (page - 1) * pageSize);
        }

        private static string Required(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new BadRequestException($"{fieldName} 不能空白");
            return value.Trim();
        }

        #region 用戶

        public AdminPagedResponse<AdminUserResponse> ListUsers(AdminUserListQuery query)
        {
            var (page, pageSize, offset) = Paging(query.page, query.page_size);
            var (items, total) = _dao.ListUsers(query, pageSize, offset);
            return new AdminPagedResponse<AdminUserResponse> { items = items, total = total, page = page, page_size = pageSize };
        }

        public AdminUserResponse GetUser(int auId)
        {
            return _dao.GetUser(auId) ?? throw new NotFoundException($"找不到 au_id={auId} 的用戶");
        }

        public int CreateUser(AdminUserCreateRequest req)
        {
            req.auth_name = Required(req.auth_name, "使用者名稱");
            req.auth_email = Required(req.auth_email, "Email");
            Required(req.auth_pswd, "密碼");
            ValidateAuthType(req.auth_type);

            if (_dao.EmailTaken(req.auth_email)) throw new ConflictException("此 Email 已被註冊");

            return _dao.CreateUser(req, HashPassword(req.auth_pswd));
        }

        /// <summary>operatorAuId 是目前登入的管理員，不能停用自己或拿掉自己的管理員身分</summary>
        public void UpdateUser(int operatorAuId, int auId, AdminUserUpdateRequest req)
        {
            req.auth_name = Required(req.auth_name, "使用者名稱");
            req.auth_email = Required(req.auth_email, "Email");
            ValidateAuthType(req.auth_type);

            if (!_dao.UserExists(auId)) throw new NotFoundException($"找不到 au_id={auId} 的用戶");
            if (auId == operatorAuId && (!req.is_active || req.auth_type != AdminAuthType))
                throw new BadRequestException("不能停用自己的帳號或拿掉自己的管理員身分");
            if (_dao.EmailTaken(req.auth_email, auId)) throw new ConflictException("此 Email 已被其他帳號使用");

            _dao.UpdateUser(auId, req);
        }

        public void ResetUserPassword(int auId, string newPassword)
        {
            Required(newPassword, "新密碼");
            if (!_dao.UserExists(auId)) throw new NotFoundException($"找不到 au_id={auId} 的用戶");

            _dao.UpdateUserPassword(auId, HashPassword(newPassword));
        }

        /// <summary>停用帳號（軟刪除），停用後無法登入</summary>
        public void DeleteUser(int operatorAuId, int auId)
        {
            if (auId == operatorAuId) throw new BadRequestException("不能停用自己的帳號");
            if (!_dao.UserExists(auId)) throw new NotFoundException($"找不到 au_id={auId} 的用戶");

            _dao.SetUserActive(auId, false);
        }

        private static void ValidateAuthType(int authType)
        {
            if (authType < 1 || authType > 3)
                throw new BadRequestException("auth_type 只能是 1（遊客）、2（商家）、3（管理員）");
        }

        private string HashPassword(string password) => new sha256Hash().getSha256(password, _appSettings.hash_key);

        #endregion

        #region 劇本

        public AdminPagedResponse<AdminStoryListItem> ListStories(AdminStoryListQuery query)
        {
            var (page, pageSize, offset) = Paging(query.page, query.page_size);
            var (items, total) = _dao.ListStories(query, pageSize, offset);
            return new AdminPagedResponse<AdminStoryListItem> { items = items, total = total, page = page, page_size = pageSize };
        }

        public AdminStoryDetailResponse GetStory(int storyId)
        {
            return _dao.GetStory(storyId) ?? throw new NotFoundException($"找不到 s_id={storyId} 的劇本");
        }

        public int CreateStory(AdminStoryCreateRequest req)
        {
            req.story_title = Required(req.story_title, "劇本標題");
            if (!_dao.UserExists(req.au_id)) throw new BadRequestException($"找不到 au_id={req.au_id} 的用戶");
            ValidateStory(req.npc_id, req.party_size);

            return _dao.CreateStory(req);
        }

        public void UpdateStory(int storyId, AdminStoryUpdateRequest req)
        {
            req.story_title = Required(req.story_title, "劇本標題");
            if (!_dao.StoryExists(storyId)) throw new NotFoundException($"找不到 s_id={storyId} 的劇本");
            ValidateStory(req.npc_id, req.party_size);

            _dao.UpdateStory(storyId, req);
        }

        /// <summary>刪除劇本（軟刪除），玩家端就看不到這個劇本</summary>
        public void DeleteStory(int storyId)
        {
            if (!_dao.StoryExists(storyId)) throw new NotFoundException($"找不到 s_id={storyId} 的劇本");
            _dao.SetStoryActive(storyId, false);
        }

        private void ValidateStory(int? npcId, int? partySize)
        {
            if (npcId.HasValue && !_dao.NpcExists(npcId.Value))
                throw new BadRequestException($"找不到 npc_id={npcId} 的 NPC");
            if (partySize.HasValue && partySize.Value < 1)
                throw new BadRequestException("人數至少要 1 人");
        }

        #endregion

        #region 勳章

        public IReadOnlyList<string> GetBadgeCategories() => BadgeService.AllCategories;

        public AdminPagedResponse<AdminBadgeResponse> ListBadges(AdminBadgeListQuery query)
        {
            var (page, pageSize, offset) = Paging(query.page, query.page_size);
            var (items, total) = _dao.ListBadges(query, pageSize, offset);
            return new AdminPagedResponse<AdminBadgeResponse> { items = items, total = total, page = page, page_size = pageSize };
        }

        public AdminBadgeResponse GetBadge(int badgeId)
        {
            return _dao.GetBadge(badgeId) ?? throw new NotFoundException($"找不到 b_id={badgeId} 的勳章");
        }

        public int CreateBadge(AdminBadgeSaveRequest req)
        {
            ValidateBadge(req);
            return _dao.CreateBadge(req);
        }

        public void UpdateBadge(int badgeId, AdminBadgeUpdateRequest req)
        {
            ValidateBadge(req);
            if (!_dao.BadgeExists(badgeId)) throw new NotFoundException($"找不到 b_id={badgeId} 的勳章");

            _dao.UpdateBadge(badgeId, req);
        }

        /// <summary>刪除勳章（軟刪除），玩家端看不到也抽不到；用戶已收集的紀錄保留</summary>
        public void DeleteBadge(int badgeId)
        {
            if (!_dao.BadgeExists(badgeId)) throw new NotFoundException($"找不到 b_id={badgeId} 的勳章");
            _dao.SetBadgeActive(badgeId, false);
        }

        private static void ValidateBadge(AdminBadgeSaveRequest req)
        {
            req.b_name = Required(req.b_name, "勳章名字");
            req.b_fication = Required(req.b_fication, "勳章分類");
            if (!BadgeService.AllCategories.Contains(req.b_fication))
                throw new BadRequestException($"勳章分類只能是：{string.Join("、", BadgeService.AllCategories)}");

            req.b_thing = string.IsNullOrWhiteSpace(req.b_thing) ? null : req.b_thing.Trim();
            req.b_image = string.IsNullOrWhiteSpace(req.b_image) ? null : req.b_image.Trim();
        }

        #endregion
    }
}
