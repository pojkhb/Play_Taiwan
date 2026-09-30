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
}
