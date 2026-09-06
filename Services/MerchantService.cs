using System.Collections.Generic;
using System.Threading.Tasks;
using backend.dao;
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

        public void UpdateStoreName(string epId, string storeName)
        {
            _dao.UpdateStoreName(epId, storeName);
        }

        public List<Dictionary<string, object>> GetMerchantFiles(string epId)
        {
            return _dao.GetMerchantFiles(epId);
        }

        public async Task<string> CreateVlogTaskAsync(string epId, GenerateVlogRequest req)
        {
            // 修正這裡：呼叫 Dao 中的 CreateVlogTask
            return _dao.CreateVlogTask(epId, req);
        }

        public Dictionary<string, object> GetVlogResult(string vlogId)
        {
            return _dao.GetVlogResult(vlogId);
        }
    }
}