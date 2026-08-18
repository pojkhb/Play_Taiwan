using System;
using System.Collections.Generic;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    public class SilhouetteService
    {
        private readonly SilhouetteDao _dao;

        public SilhouetteService(SilhouetteDao dao)
        {
            _dao = dao;
        }

        #region 取得剪影清單

        public List<Silhouette> GetSilhouettes()
        {
            return _dao.GetSilhouettes();
        }

        #endregion

        #region 取得单一剪影

        public Silhouette GetSilhouetteById(string silhouetteId)
        {
            if (string.IsNullOrWhiteSpace(silhouetteId))
            {
                throw new ArgumentException("silhouette_id 不可為空白。");
            }

            Silhouette result = _dao.GetSilhouetteById(silhouetteId);

            if (result == null)
            {
                throw new KeyNotFoundException("找不到指定剪影。");
            }

            return result;
        }

        #endregion
    }
}
