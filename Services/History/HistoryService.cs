// 檔案路徑：System\Services\History\HistoryService.cs
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>過往劇本清單，每份帶玩家這趟拍的第一張照片當封面</summary>
        public List<HistoryStoryItem> GetHistoryList(int auId)
        {
            List<HistoryStoryItem> items = _dao.GetHistoryList(auId);
            var photos = ToPhotos(_dao.GetMedia(auId, items.Select(i => i.story_id).Distinct().ToList()));

            foreach (HistoryStoryItem item in items)
            {
                item.cover_image_url = photos.TryGetValue(item.story_id, out List<HistoryPhoto> list) ? list[0].image_url : null;
            }

            return items;
        }
        #endregion

        #region 取得過往劇本詳情
        /// <summary>過往劇本詳情，帶這趟拍的所有照片（依站的順序）</summary>
        public HistoryStoryItem GetHistoryDetail(int storyId, int auId)
        {
            HistoryStoryItem item = _dao.GetHistoryDetail(storyId, auId);
            var photos = ToPhotos(_dao.GetMedia(auId, new List<int> { storyId }));

            item.photos = photos.TryGetValue(storyId, out List<HistoryPhoto> list) ? list : new List<HistoryPhoto>();
            item.cover_image_url = item.photos.FirstOrDefault()?.image_url;

            return item;
        }
        #endregion

        /// <summary>
        /// 素材 → 每份劇本的照片：影片、錄音不算，同一個網址只留一次（跟劇本回顧、Vlog 用同一個規則），保留原本的排列順序
        /// </summary>
        internal static Dictionary<int, List<HistoryPhoto>> ToPhotos(IEnumerable<HistoryDao.MediaRow> rows)
        {
            var result = new Dictionary<int, List<HistoryPhoto>>();
            var seen = new HashSet<(int, string)>();

            foreach (HistoryDao.MediaRow row in rows)
            {
                if (!VlogAiGateway.LooksLikeImage(row.url) || !seen.Add((row.story_id, row.url))) continue;

                if (!result.TryGetValue(row.story_id, out List<HistoryPhoto> list))
                    result[row.story_id] = list = new List<HistoryPhoto>();

                list.Add(new HistoryPhoto
                {
                    node_id = row.node_id,
                    node_order = row.node_order,
                    place_name = row.place_name,
                    image_url = row.url
                });
            }

            return result;
        }
    }
}
