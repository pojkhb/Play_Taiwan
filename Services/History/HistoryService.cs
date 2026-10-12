// 檔案路徑：System\Services\History\HistoryService.cs
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.Services.Neo4j;

namespace backend.Services
{
    public class HistoryService
    {
        private readonly HistoryDao _dao;
        private readonly PlaceLookupService _placeLookup;

        public HistoryService(HistoryDao dao, PlaceLookupService placeLookup)
        {
            _dao = dao;
            _placeLookup = placeLookup;
        }

        #region 取得所有過往劇本
        /// <summary>過往劇本清單，每份帶封面：玩家這趟拍的第一張照片，沒拍過照片時用景點照片</summary>
        public async Task<List<HistoryStoryItem>> GetHistoryListAsync(int auId)
        {
            List<HistoryStoryItem> items = _dao.GetHistoryList(auId);
            var photos = await GetPhotosAsync(auId, items.Select(i => i.story_id).Distinct().ToList());

            foreach (HistoryStoryItem item in items)
            {
                item.cover_image_url = photos.TryGetValue(item.story_id, out List<HistoryPhoto> list) ? Cover(list) : null;
            }

            return items;
        }
        #endregion

        #region 取得過往劇本詳情
        /// <summary>過往劇本詳情，帶這趟的所有照片（依站的順序）：玩家拍的照片，沒拍照片的站補上景點照片</summary>
        public async Task<HistoryStoryItem> GetHistoryDetailAsync(int storyId, int auId)
        {
            HistoryStoryItem item = _dao.GetHistoryDetail(storyId, auId);
            var photos = await GetPhotosAsync(auId, new List<int> { storyId });

            item.photos = photos.TryGetValue(storyId, out List<HistoryPhoto> list) ? list : new List<HistoryPhoto>();
            item.cover_image_url = Cover(item.photos);

            return item;
        }
        #endregion

        /// <summary>
        /// 每份劇本的照片：玩家拍的照片，加上玩家沒拍照片的站的景點照片（用景點 ID 向 Neo4j 拿）。
        /// 景點照片只補玩完的劇本（HistoryDao.GetCompletedStations）
        /// </summary>
        private async Task<Dictionary<int, List<HistoryPhoto>>> GetPhotosAsync(int auId, List<int> storyIds)
        {
            Dictionary<int, List<HistoryPhoto>> photos = ToPhotos(_dao.GetMedia(auId, storyIds));

            List<HistoryDao.StationRow> stations = _dao.GetCompletedStations(auId, storyIds);
            Dictionary<string, string> placeImages = await _placeLookup.GetPlaceImagesAsync(stations.Select(s => s.place_id));

            return AddPlacePhotos(photos, stations, placeImages);
        }

        /// <summary>封面：玩家拍的第一張照片；沒拍過照片時是第一張景點照片</summary>
        internal static string Cover(List<HistoryPhoto> photos) =>
            (photos.FirstOrDefault(p => !p.is_place_photo) ?? photos.FirstOrDefault())?.image_url;

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

        /// <summary>
        /// 玩家沒拍照片的站補上那一站的景點照片（placeImages：景點 ID → 照片網址，景點沒有照片的站不補），
        /// 每份劇本依站的順序排好；同一站玩家拍的照片維持原本的順序
        /// </summary>
        internal static Dictionary<int, List<HistoryPhoto>> AddPlacePhotos(
            Dictionary<int, List<HistoryPhoto>> photos, IEnumerable<HistoryDao.StationRow> stations, Dictionary<string, string> placeImages)
        {
            var result = new Dictionary<int, List<HistoryPhoto>>(photos);

            foreach (var story in stations.GroupBy(s => s.story_id))
            {
                List<HistoryPhoto> taken = photos.TryGetValue(story.Key, out List<HistoryPhoto> list) ? list : new List<HistoryPhoto>();
                HashSet<int> photographed = taken.Select(p => p.node_id).ToHashSet();

                List<HistoryPhoto> placePhotos = story
                    .Where(s => !photographed.Contains(s.node_id) && s.place_id != null && placeImages.ContainsKey(s.place_id))
                    .Select(s => new HistoryPhoto
                    {
                        node_id = s.node_id,
                        node_order = s.node_order,
                        place_name = s.place_name,
                        image_url = placeImages[s.place_id],
                        is_place_photo = true
                    })
                    .ToList();

                if (placePhotos.Count > 0)
                    result[story.Key] = taken.Concat(placePhotos).OrderBy(p => p.node_order).ToList();
            }

            return result;
        }
    }
}
