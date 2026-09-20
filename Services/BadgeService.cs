using System.Collections.Generic;
using System.Linq;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    public class BadgeService
    {
        private readonly BadgeDao _dao;

        public BadgeService(BadgeDao dao)
        {
            _dao = dao;
        }

        public List<BadgeSeriesGroup> GetBadgeCatalog(string ep_id)
        {
            var flatList = _dao.GetAllBadgeStatus(ep_id);

            return flatList
                .GroupBy(b => b.series_name)
                .Select(g => new BadgeSeriesGroup
                {
                    series_name = g.Key,
                    badges = g.Select(b => new BadgeItem
                    {
                        b_id = int.TryParse(b.badge_id, out int bId) ? bId : 0,
                        b_name = b.badge_name,
                        b_image = b.image_url,
                        is_owned = b.is_owned,
                        obtained_at = b.obtained_at
                    }).ToList()
                })
                .ToList();
        }
    }
}