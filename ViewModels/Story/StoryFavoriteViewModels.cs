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

        /// <summary>
        /// 封面（完整網址）：第一站的圖片。抵達過第一站是景點照片（景點沒有照片時為 null）；
        /// 還沒抵達是這張照片的迷霧版（還沒做好或景點沒有照片時是通用迷霧圖）
        /// </summary>
        public string cover_image_url { get; set; }

        /// <summary>內部用：第一站的景點 ID，用它向 Neo4j 拿封面照片，不回傳給前端</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string cover_place_id { get; set; }

        /// <summary>是否已經玩完</summary>
        public bool is_completed { get; set; }

        /// <summary>劇本建立時間</summary>
        public DateTime created_at { get; set; }
    }
}
