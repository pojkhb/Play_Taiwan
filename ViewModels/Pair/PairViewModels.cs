// 檔案路徑：System\ViewModels\Pair\PairViewModels.cs
// 協作解謎配對（story_pair_session / story_pair_member）
using System;
using System.Collections.Generic;

namespace backend.ViewModels
{
    /// <summary>指定劇本的請求（產生配對碼、查詢、退出、取消都用這個）</summary>
    public class PairStoryRequest
    {
        /// <summary>劇本代號（story.s_id）</summary>
        public int story_id { get; set; }
    }

    /// <summary>輸入配對碼加入隊伍</summary>
    public class PairJoinRequest
    {
        /// <summary>劇本擁有者分享的四位數配對碼</summary>
        public string pair_code { get; set; }
    }

    /// <summary>協作隊伍目前的狀態</summary>
    public class PairSessionResponse
    {
        /// <summary>隊伍代號（story_pair_session.pair_id）</summary>
        public int pair_id { get; set; }

        /// <summary>劇本代號</summary>
        public int story_id { get; set; }

        /// <summary>劇本標題</summary>
        public string story_title { get; set; }

        /// <summary>四位數配對碼，只有 waiting（可加入）時有值，其他狀態為 null</summary>
        public string pair_code { get; set; }

        /// <summary>隊伍狀態：waiting 可加入、locked 已鎖定（人滿）、expired 配對碼逾期、completed 已完成、cancelled 已取消</summary>
        public string pair_status { get; set; }

        /// <summary>配對碼到期時間（資料庫時間），只有 waiting 時有值</summary>
        public DateTime? expires_at { get; set; }

        /// <summary>配對碼剩餘秒數（前端倒數用），只有 waiting 時有值</summary>
        public int? expires_in_seconds { get; set; }

        /// <summary>隊伍人數上限（劇本的 party_size）</summary>
        public int party_size { get; set; }

        /// <summary>目前人數</summary>
        public int member_count { get; set; }

        /// <summary>登入者的座位編號（對應 task_clue.seat_no），不是成員時為 null</summary>
        public int? my_seat_no { get; set; }

        /// <summary>登入者是否為隊長（劇本擁有者）</summary>
        public bool is_host { get; set; }

        /// <summary>隊伍成員（依座位排序）</summary>
        public List<PairMemberItem> members { get; set; } = new List<PairMemberItem>();
    }

    /// <summary>隊伍成員</summary>
    public class PairMemberItem
    {
        public int au_id { get; set; }
        public string auth_name { get; set; }

        /// <summary>座位編號，決定在協作解謎看到哪一段線索</summary>
        public int seat_no { get; set; }

        /// <summary>是否為隊長（劇本擁有者）</summary>
        public bool is_host { get; set; }

        public DateTime joined_at { get; set; }
    }

    /// <summary>回報自己目前的位置</summary>
    public class TeamLocationRequest
    {
        /// <summary>緯度</summary>
        public double lat { get; set; }

        /// <summary>經度</summary>
        public double lng { get; set; }

        /// <summary>定位精準度（公尺），可不帶</summary>
        public double? accuracy { get; set; }

        /// <summary>面向的方向（度，0＝正北，順時針），可不帶；地圖上要畫箭頭時用</summary>
        public double? heading { get; set; }
    }

    /// <summary>一起玩這份劇本的人各自的位置</summary>
    public class TeamLocationsResponse
    {
        /// <summary>劇本代號</summary>
        public int story_id { get; set; }

        /// <summary>幾秒內有回報才算在線上（members[].is_online）</summary>
        public int online_within_seconds { get; set; }

        /// <summary>一起玩的人（依座位排序，包含自己）；沒有協作隊伍時只有劇本擁有者自己</summary>
        public List<TeamMemberLocation> members { get; set; } = new List<TeamMemberLocation>();
    }

    /// <summary>一位隊員與他最後回報的位置</summary>
    public class TeamMemberLocation
    {
        public int au_id { get; set; }
        public string auth_name { get; set; }

        /// <summary>座位編號</summary>
        public int seat_no { get; set; }

        /// <summary>是否為隊長（劇本擁有者）</summary>
        public bool is_host { get; set; }

        /// <summary>是不是登入者自己（自己的位置前端用手機定位畫就好）</summary>
        public bool is_me { get; set; }

        /// <summary>緯度；還沒回報過、或最後一次回報超過 30 分鐘時為 null</summary>
        public double? lat { get; set; }

        /// <summary>經度；沒有位置時為 null</summary>
        public double? lng { get; set; }

        /// <summary>定位精準度（公尺），回報時有帶才有</summary>
        public double? accuracy { get; set; }

        /// <summary>面向的方向（度，0＝正北，順時針），回報時有帶才有</summary>
        public double? heading { get; set; }

        /// <summary>後端收到這筆位置的時間（UTC）；沒有位置時為 null</summary>
        public DateTime? updated_at { get; set; }

        /// <summary>這筆位置是幾秒前回報的；沒有位置時為 null</summary>
        public int? seconds_ago { get; set; }

        /// <summary>是否在線上：最近 60 秒內有回報位置。false 時地圖上可以把他畫成灰色</summary>
        public bool is_online { get; set; }
    }
}
