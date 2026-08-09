using System.Collections.Generic;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    public class HistoryService
    {
        private readonly HistoryDao _dao;
        public HistoryService(HistoryDao dao) { _dao = dao; }

        #region 取得所有過往劇本
        public List<HistoryStoryItem> GetHistoryList()
        {
            return _dao.GetHistoryList();
        }
        #endregion

        #region 取得過往劇本詳情
        public HistoryStoryItem GetHistoryDetail(string story_id)
        {
            return _dao.GetHistoryDetail(story_id);
        }
        #endregion
    }
}