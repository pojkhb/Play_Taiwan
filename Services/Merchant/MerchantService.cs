// 檔案路徑：System\Services\Merchant\MerchantService.cs
using System.Collections.Generic;
using backend.dao;
using backend.Models;
using backend.ViewModels;

namespace backend.Services
{
    public class MerchantService
    {
        private readonly MerchantDao _dao;

        public MerchantService(MerchantDao dao)
        {
            _dao = dao;
        }

        #region 修改店家名稱
        public void UpdateStoreName(int auId, string storeName)
        {
            _dao.UpdateStoreName(auId, storeName);
        }
        #endregion

        #region 已生成的影音檔案
        public List<MerchantFileItem> GetMerchantFiles(int auId)
        {
            return _dao.GetMerchantFiles(auId);
        }
        #endregion

        #region 建立商家影音專案
        public int CreateVlogTask(int auId, GenerateVlogRequest req)
        {
            return _dao.CreateVlogTask(auId, req);
        }
        #endregion

        #region 取得最後生成畫面
        public MerchantVlogResult GetVlogResult(int mmId, int auId)
        {
            return _dao.GetVlogResult(mmId, auId);
        }
        #endregion
    }
}
