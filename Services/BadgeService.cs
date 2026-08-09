using System.Collections.Generic;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    public class BadgeService
    {
        private readonly BadgeDao _dao;
        public BadgeService(BadgeDao dao) { _dao = dao; }

        #region 取得我的所有徽章
        public List<BadgeResponse> GetMyBadges()
        {
            return _dao.GetMyBadges();
        }
        #endregion
    }
}