// 檔案路徑：System\Services\History\HistoryService.cs
using System.Collections.Generic;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    public class HistoryService
    {
        private readonly HistoryDao _dao;

        public HistoryService(HistoryDao dao)
        {
            _dao = dao;
        }

        #region 取得所有過往劇本
        public List<HistoryStoryItem> GetHistoryList(int auId)
        {
            return _dao.GetHistoryList(auId);
        }
        #endregion

        #region 取得過往劇本詳情
        public HistoryStoryItem GetHistoryDetail(int storyId, int auId)
        {
            return _dao.GetHistoryDetail(storyId, auId);
        }
        #endregion
    }
}
