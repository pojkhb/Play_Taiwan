// 檔案路徑：System\Services\HomeService.cs
using backend.dao;
using backend.Models;

namespace backend.Services
{
    public class HomeService
    {
        private readonly HomeDao _dao;

        public HomeService(HomeDao dao)
        {
            _dao = dao;
        }

        #region 首頁目前總覽

        public HomeOverviewResponse GetOverview(int auId)
        {
            return _dao.GetOverview(auId);
        }

        #endregion
    }
}