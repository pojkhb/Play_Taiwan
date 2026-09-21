// 檔案路徑：System\Services\BadgeService.cs
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

        #region 取得徽章圖鑑（依 b_fication 分類分組）
        public List<BadgeSeriesGroup> GetBadgeCatalog(int auId)
        {
            List<BadgeResponse> rows = _dao.GetAllBadgeStatus(auId);

            return rows
                .GroupBy(b => b.b_fication)
                .Select(g => new BadgeSeriesGroup
                {
                    series_name = g.Key,
                    badges = g.Select(b => new BadgeItem
                    {
                        b_id = b.b_id,
                        b_name = b.b_name,
                        b_thing = b.b_thing,
                        b_image = b.b_image,
                        is_owned = b.is_owned,
                        obtained_at = b.obtained_at
                    }).ToList()
                })
                .ToList();
        }
        #endregion
    }
}
