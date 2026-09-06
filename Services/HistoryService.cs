using System.Collections.Generic;
using backend.dao;
using backend.Models;
using backend.ViewModels;

namespace backend.Services
{
    public class HistoryService
    {
        private readonly HistoryDao _dao;

        public HistoryService(HistoryDao dao)
        {
            _dao = dao;
        }

        public List<HistoryStoryItem> GetHistoryList(string ep_id)
        {
            return _dao.GetHistoryList(ep_id);
        }

        public HistoryStoryItem GetHistoryDetail(string story_id, string ep_id)
        {
            return _dao.GetHistoryDetail(story_id, ep_id);
        }
    }
}