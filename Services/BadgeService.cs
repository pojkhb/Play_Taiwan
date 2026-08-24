using System;
using System.Collections.Generic;
using backend.dao;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace backend.Services
{
    public class BadgeService
    {
        private readonly BadgeDao _dao;
        private readonly HttpContext _ipContext;

        public BadgeService(BadgeDao dao, IHttpContextAccessor httpContextAccessor)
        {
            _dao = dao;
            _ipContext = httpContextAccessor.HttpContext;
        }

        private string GetCurrentEpId()
        {
            var epIdClaim = _ipContext?.User?.FindFirst("ep_id") ?? _ipContext?.User?.FindFirst(ClaimTypes.NameIdentifier);
            if (epIdClaim == null) throw new Exception("無法取得當前探員身分，請重新登入");
            return epIdClaim.Value;
        }

        #region 取得我的所有徽章
        public List<BadgeResponse> GetMyBadges()
        {
            string ep_id = GetCurrentEpId();
            return _dao.GetMyBadges(ep_id);
        }
        #endregion

        #region 取得徽章圖鑑狀態
        public List<BadgeResponse> GetAllBadgeStatus()
        {
            string ep_id = GetCurrentEpId();
            return _dao.GetAllBadgeStatus(ep_id);
        }
        #endregion
    }
}