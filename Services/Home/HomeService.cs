// 檔案路徑：System\Services\Home\HomeService.cs
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
            HomeOverviewResponse overview = _dao.GetOverview(auId);

            // 明信片卡片的圖片跟其他明信片 API 一樣給顯示網址（資料庫存的是 AI 服務的檔案連結，打開會變成下載）
            foreach (HomeCardItem card in overview.ho_recent_cards ?? new())
            {
                if (card.hc_type == "postcard") card.hc_image = PostcardCatalogService.ImagePath(card.hc_id);
            }

            return overview;
        }

        #endregion
    }
}