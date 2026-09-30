// 檔案路徑：System\ViewModels\Story\StoryFavoriteViewModels.cs
using System;

namespace backend.ViewModels
{
    /// <summary>設定或取消喜愛的劇本</summary>
    public class StoryFavoriteRequest
    {
        /// <summary>true＝加入喜愛、false＝取消喜愛</summary>
        public bool is_favorite { get; set; }
    }

    /// <summary>「我喜愛的劇本」清單的一筆</summary>
    public class FavoriteStoryItem
    {
        public int story_id { get; set; }
        public string title { get; set; }

        /// <summary>劇本簡介</summary>
        public string synopsis { get; set; }

        public string city_name { get; set; }
        public string district_name { get; set; }

        /// <summary>站數</summary>
        public int node_count { get; set; }

        public bool is_night_mode { get; set; }

        /// <summary>封面：第一站的景點照片（第一站本來就開放）；沒有照片時為 null</summary>
        public string cover_image_url { get; set; }

        /// <summary>是否已經玩完</summary>
        public bool is_completed { get; set; }

        /// <summary>劇本建立時間</summary>
        public DateTime created_at { get; set; }
    }
}
