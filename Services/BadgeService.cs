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
                .GroupBy(b => new { b.series_id, b.series_name })
                .Select(g => new BadgeSeriesGroup
                {
                    series_id = g.Key.series_id,
                    series_name = g.Key.series_name,
                    badges = g.Select(b => new BadgeItem
                    {
                        badge_id = b.badge_id,
                        badge_name = b.badge_name,
                        description = b.description,
                        image_url = b.image_url,
                        is_owned = b.is_owned,
                        obtained_at = b.obtained_at
                    }).ToList()
                })
                .ToList();
        }
    }
}