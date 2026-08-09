using System.Collections.Generic;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    public class FavoriteService
    {
        private readonly FavoriteDao _dao;
        public FavoriteService(FavoriteDao dao) { _dao = dao; }

        #region 取得收藏清單
        public List<FavoriteItemResponse> GetFavorites()
        {
            return _dao.GetFavorites();
        }
        #endregion
    }
}