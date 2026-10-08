// 檔案路徑：System\Services\Badge\BadgeService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    public class BadgeService
    {
        private readonly BadgeDao _dao;

        public BadgeService(BadgeDao dao)
        {
            _dao = dao;
        }

        #region 取得徽章圖鑑（依 b_fication 分類分組）
        /// <summary>
        /// 依 b_id 排序後分組：分類依序是島嶼城市、台灣味、台灣印記、島嶼生靈、老台灣、午夜台灣，
        /// 每個分類裡也照 b_id，跟勳章對照表的順序一致。
        /// </summary>
        public List<BadgeSeriesGroup> GetBadgeCatalog(int auId)
        {
            List<BadgeResponse> rows = _dao.GetAllBadgeStatus(auId);

            return rows
                .GroupBy(b => b.b_fication)
                .Select(g => new BadgeSeriesGroup
                {
                    series_name = CategoryLabel(g.Key),
                    badges = g.Select(b => new BadgeItem
                    {
                        b_id = b.b_id,
                        b_name = b.b_name,
                        b_thing = b.b_thing,
                        b_image = b.b_image,
                        is_owned = b.is_owned,
                        obtained_at = b.obtained_at
                    }).ToList()
                })
                .ToList();
        }
        #endregion

        #region 劇本可抽的勳章

        private const string CityCategory = "島嶼城市";
        private const string FoodCategory = "台灣味";
        private const string LandmarkCategory = "台灣印記";
        private const string WildlifeCategory = "島嶼生靈";
        private const string OldTaiwanCategory = "老台灣";
        private const string NightCategory = "午夜台灣";

        /// <summary>
        /// 回傳給前端的分類名稱前面加上圖示，例如「🌙 午夜台灣」，前端直接顯示。
        /// 只用在 API 回應；資料庫、story.story_badge 與抽勳章的比對規則仍用原本的分類名稱。
        /// </summary>
        public static string CategoryLabel(string category) => $"{CategoryIcon(category)} {category}";

        private static string CategoryIcon(string category) => category switch
        {
            NightCategory => "🌙",
            LandmarkCategory => "🏛️",
            FoodCategory => "🍜",
            CityCategory => "🏝️",
            WildlifeCategory => "🐻",
            OldTaiwanCategory => "🏮",
            _ => "🏆"
        };

        /// <summary>總章不對應單一縣市，不放進劇本的抽獎池</summary>
        private const string IslandTotalThing = "臺灣島本島總章";

        /// <summary>type.type_id 4 = 地方美食型</summary>
        private const int FoodTaskTypeId = 4;

        // 景點名稱比對前已把「臺」換成「台」、去掉空白
        private const string FoodPlacePattern = "夜市|市場|小吃|美食";
        private const string WildlifePlacePattern = "動物園|國家公園|森林|步道|濕地|溼地|生態|保護區|水族館|海洋|牧場|農場|賞鯨|鳥園";
        private const string OldTaiwanPlacePattern = "老街|古厝|眷村|新村|市場|騎樓|宿舍|故居|古蹟|街屋|老屋|大稻埕|剝皮寮|柑仔店";

        /// <summary>
        /// 台灣印記：勳章對應物件 → 景點名稱要符合的關鍵字、要排除的關鍵字。
        /// 沒列在這裡的直接用對應物件名稱比對（總統府、日月潭、阿里山、安平古堡…）。
        /// </summary>
        private static readonly Dictionary<string, (string include, string exclude)> LandmarkPatterns = new()
        {
            ["台北101"] = ("台北101", null),
            ["國立故宮博物院"] = ("故宮博物院", null),
            ["台南孔廟"] = ("台南孔子?廟", null),
            ["龍山寺"] = ("龍山寺", "地下街"),
            ["淡水紅毛城"] = ("紅毛城", null),
            ["九份"] = ("九份", "九份二山"),
            ["野柳女王頭"] = ("野柳地質公園|女王頭", null),
            ["赤崁樓"] = ("赤[崁嵌]樓", null),
            ["佛光山"] = ("佛光山", "道場|惠中寺|極樂寺"),
            ["駁二藝術特區"] = ("駁二", null),
            ["鵝鑾鼻燈塔"] = ("鵝鑾鼻", null),
            ["金門莒光樓"] = ("莒光樓", null),
            ["馬祖芹壁"] = ("芹壁", null),
        };

        /// <summary>
        /// 依劇本內容算出可抽的勳章類別，寫回 story.story_badge，劇本卡片「預計獲得」顯示用。
        /// </summary>
        public async Task<List<string>> RefreshStoryCategoriesAsync(int storyId)
        {
            BadgeDao.StoryFacts facts = await _dao.GetStoryFactsAsync(storyId);
            if (facts == null) return new List<string>();

            List<string> categories = Categories(MatchPool(facts, await _dao.GetBadgesAsync()));
            await _dao.UpdateStoryBadgeAsync(storyId, string.Join(",", categories));
            return categories;
        }

        /// <summary>
        /// 完成劇本後抽一枚勳章：先平均抽類別、再從類別裡抽一枚，已擁有的不會再抽到。
        /// 一個劇本只能抽一次，重複呼叫回傳當初抽到的那枚。
        /// </summary>
        public async Task<BadgeDrawResponse> DrawAsync(int auId, int storyId)
        {
            if (!await _dao.HasCompletedStoryAsync(auId, storyId))
                throw new Exception("完成這個劇本後才能抽勳章");

            BadgeDao.StoryFacts facts = await _dao.GetStoryFactsAsync(storyId)
                ?? throw new Exception($"找不到劇本 story_id={storyId}");

            List<BadgeInfo> pool = MatchPool(facts, await _dao.GetBadgesAsync());
            var response = new BadgeDrawResponse { story_id = storyId, categories = Categories(pool).Select(CategoryLabel).ToList() };

            response.badge = ForDisplay(await _dao.GetStoryDrawAsync(auId, storyId));
            if (response.badge != null) return response;

            HashSet<int> owned = (await _dao.GetOwnedBadgeIdsAsync(auId)).ToHashSet();
            List<IGrouping<string, BadgeInfo>> left = pool
                .Where(b => !owned.Contains(b.b_id))
                .GroupBy(b => b.b_fication)
                .ToList();
            if (left.Count == 0) return response;

            // 先抽類別：只有一枚的城市勳章才不會被整類七枚的午夜台灣稀釋
            List<BadgeInfo> category = left[Random.Shared.Next(left.Count)].ToList();
            BadgeInfo picked = category[Random.Shared.Next(category.Count)];

            if (await _dao.TryInsertDrawAsync(auId, picked.b_id, storyId))
            {
                response.badge = ForDisplay(picked);
                response.is_new = true;
                return response;
            }

            // 同一個劇本同時送出兩次，以先寫入的為準
            response.badge = ForDisplay(await _dao.GetStoryDrawAsync(auId, storyId)
                ?? throw new Exception("抽勳章失敗，請再試一次"));
            return response;
        }

        /// <summary>回傳給前端的勳章：分類前面加上圖示</summary>
        private static BadgeInfo ForDisplay(BadgeInfo badge) => badge == null ? null : new BadgeInfo
        {
            b_id = badge.b_id,
            b_name = badge.b_name,
            b_fication = CategoryLabel(badge.b_fication),
            b_thing = badge.b_thing,
            b_image = badge.b_image
        };

        /// <summary>劇本可抽的勳章（臺灣島本島總章除外）</summary>
        internal static List<BadgeInfo> MatchPool(BadgeDao.StoryFacts facts, List<BadgeInfo> badges)
        {
            List<string> places = facts.places.Select(p => Normalize(p.place_name)).ToList();
            bool AnyPlace(string pattern) => places.Any(p => Regex.IsMatch(p, pattern));

            // 整類都能抽的類別
            var wholeCategories = new HashSet<string>();
            if (facts.is_night_mode == 1)
                wholeCategories.Add(NightCategory);
            if (facts.task_types.Contains(FoodTaskTypeId)
                || facts.places.Any(p => p.place_category == "Restaurant")
                || facts.tags.Contains("美食")
                || AnyPlace(FoodPlacePattern))
                wholeCategories.Add(FoodCategory);
            if (AnyPlace(WildlifePlacePattern))
                wholeCategories.Add(WildlifeCategory);
            if (AnyPlace(OldTaiwanPlacePattern))
                wholeCategories.Add(OldTaiwanCategory);

            string city = CityKey(facts.city_name);

            return badges
                .Where(b => b.b_thing != IslandTotalThing)
                .Where(b => wholeCategories.Contains(b.b_fication)
                         || (b.b_fication == CityCategory && Normalize(b.b_thing) == city)
                         || (b.b_fication == LandmarkCategory && PassesLandmark(b.b_thing, places)))
                .ToList();
        }

        private static bool PassesLandmark(string thing, List<string> places)
        {
            var (include, exclude) = LandmarkPatterns.TryGetValue(thing, out var p) ? p : (Regex.Escape(Normalize(thing)), null);
            return places.Any(name => Regex.IsMatch(name, include) && (exclude == null || !Regex.IsMatch(name, exclude)));
        }

        /// <summary>依圖鑑順序列出不重複的類別</summary>
        private static List<string> Categories(List<BadgeInfo> pool) => pool.Select(b => b.b_fication).Distinct().ToList();

        /// <summary>「臺中市」→「台中」，對得上島嶼城市勳章的對應物件</summary>
        private static string CityKey(string cityName)
        {
            string city = Normalize(cityName);
            return city.Length > 2 && (city.EndsWith("市") || city.EndsWith("縣")) ? city[..^1] : city;
        }

        private static string Normalize(string s) => (s ?? "").Replace("臺", "台").Replace(" ", "").Trim();

        #endregion
    }
}
