using System.Collections.Generic;
using backend.utils;
using backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class FavoriteDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        private readonly HttpContext _ipContext;

        public FavoriteDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 取得收藏清單
        public List<FavoriteItemResponse> GetFavorites()
        {
            // TODO: 資料表尚未建置，預計欄位:
            // ep_favorite(ep_id, favorite_id, item_type, ref_id, created_at)
            return new List<FavoriteItemResponse>();
        }
        #endregion
    }
}