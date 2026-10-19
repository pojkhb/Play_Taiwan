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
        /// <summary>過往劇本清單，每份帶封面：依站的順序第一張景點照片</summary>
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
        /// <summary>過往劇本詳情，帶每一站的景點照片（依站的順序）與景點的真實名稱</summary>
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
        /// 每份劇本的照片：每一站的景點照片（用景點 ID 向 Neo4j 拿），不用玩家上傳的照片。
        /// 只給玩完的劇本（HistoryDao.GetCompletedStations）
        /// </summary>
        private async Task<Dictionary<int, List<HistoryPhoto>>> GetPhotosAsync(int auId, List<int> storyIds)
        {
            List<HistoryDao.StationRow> stations = _dao.GetCompletedStations(auId, storyIds);
            Dictionary<string, string> placeImages = await _placeLookup.GetPlaceImagesAsync(stations.Select(s => s.place_id));

            return PlacePhotos(stations, placeImages);
        }

        /// <summary>封面：依站的順序第一張景點照片</summary>
        internal static string Cover(List<HistoryPhoto> photos) => photos.FirstOrDefault()?.image_url;

        /// <summary>
        /// 每一站的景點照片（placeImages：景點 ID → 照片網址，景點沒有照片的站不列出），每份劇本依站的順序排好；
        /// 沒有任何景點照片的劇本不會出現在結果裡
        /// </summary>
        internal static Dictionary<int, List<HistoryPhoto>> PlacePhotos(
            IEnumerable<HistoryDao.StationRow> stations, Dictionary<string, string> placeImages)
        {
            var result = new Dictionary<int, List<HistoryPhoto>>();

            foreach (var story in stations.GroupBy(s => s.story_id))
            {
                List<HistoryPhoto> photos = story
                    .Where(s => s.place_id != null && placeImages.ContainsKey(s.place_id))
                    .OrderBy(s => s.node_order)
                    .Select(s => new HistoryPhoto
                    {
                        node_id = s.node_id,
                        node_order = s.node_order,
                        place_name = s.place_name,
                        real_place_name = s.real_place_name,
                        image_url = placeImages[s.place_id],
                        is_place_photo = true
                    })
                    .ToList();

                if (photos.Count > 0) result[story.Key] = photos;
            }

            return result;
        }
    }
}
