// 檔案路徑：System\Services\Pair\PairService.cs
// 協作解謎配對：劇本擁有者產生四位數配對碼分享給同行玩家，其他玩家輸入配對碼加入同一份劇本的協作隊伍，
// 每位成員依座位（seat_no）看到協作解謎任務的不同線索（task_clue）。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using backend.dao;
using backend.Models;
using backend.ViewModels;

namespace backend.Services
{
    public class PairService
    {
        // 配對碼有效時間（分鐘）
        private const int CodeValidMinutes = 10;

        private readonly PairDao _dao;
        private readonly StoryDao _storyDao;
        private readonly TeamLocationStore _locations;

        public PairService(PairDao dao, StoryDao storyDao, TeamLocationStore locations)
        {
            _dao = dao;
            _storyDao = storyDao;
            _locations = locations;
        }

        /// <summary>劇本擁有者產生（或重新開放、沿用）配對碼。</summary>
        public PairSessionResponse IssueCode(int auId, int storyId)
        {
            if (storyId <= 0) throw new BadRequestException("請提供 story_id");

            _dao.IssueCode(auId, storyId, CodeValidMinutes);
            return GetByStory(auId, storyId);
        }

        /// <summary>輸入配對碼加入隊伍；加入後這份劇本自動設為加入者進行中的劇本（同確認開始）。</summary>
        public PairSessionResponse Join(int auId, string pairCode)
        {
            string code = (pairCode ?? "").Trim();
            if (!Regex.IsMatch(code, @"^\d{4}$")) throw new BadRequestException("配對碼是四位數字");

            (_, int storyId) = _dao.Join(auId, code);
            _storyDao.SetStoryPlaying(auId, storyId);

            return GetByStory(auId, storyId);
        }

        /// <summary>
        /// 查詢劇本目前的協作隊伍（隊長等待隊員加入、隊員確認座位用）。
        /// 只有劇本擁有者或隊伍成員可以查詢；還沒有隊伍時回傳 null。
        /// </summary>
        public PairSessionResponse GetByStory(int auId, int storyId)
        {
            PairDao.StoryRow story = _dao.GetStory(storyId)
                ?? throw new NotFoundException($"找不到 story_id={storyId} 的劇本");

            bool isOwner = story.au_id == auId;
            PairDao.SessionRow session = _dao.GetCurrentSession(storyId);

            if (session == null)
            {
                if (!isOwner) throw new UnauthorizedAccessException("你沒有參與這份劇本的協作隊伍");
                return null;
            }

            var members = _dao.GetMembers(session.pair_id);
            PairMemberItem me = members.FirstOrDefault(m => m.au_id == auId);

            if (!isOwner && me == null) throw new UnauthorizedAccessException("你沒有參與這份劇本的協作隊伍");

            bool waiting = session.pair_status == "waiting";

            return new PairSessionResponse
            {
                pair_id = session.pair_id,
                story_id = story.s_id,
                story_title = story.story_title,
                pair_code = waiting ? session.pair_code : null,
                pair_status = session.pair_status,
                expires_at = waiting ? session.expires_at : null,
                expires_in_seconds = waiting && session.expires_in_seconds.HasValue ? (int)session.expires_in_seconds.Value : null,
                party_size = story.party_size ?? 0,
                member_count = members.Count,
                my_seat_no = me?.seat_no,
                is_host = me?.is_host ?? isOwner,
                members = members
            };
        }

        /// <summary>隊員退出隊伍。</summary>
        public void Leave(int auId, int storyId)
        {
            if (storyId <= 0) throw new BadRequestException("請提供 story_id");
            _dao.Leave(auId, storyId);
        }

        /// <summary>隊長（劇本擁有者）取消隊伍。</summary>
        public void Cancel(int auId, int storyId)
        {
            if (storyId <= 0) throw new BadRequestException("請提供 story_id");
            _dao.Cancel(auId, storyId);
        }

        #region 隊友位置

        /// <summary>回報自己目前的位置，並回傳一起玩的人各自的位置（前端每幾秒呼叫一次，一趟來回就夠）。</summary>
        public TeamLocationsResponse ReportLocation(int auId, int storyId, TeamLocationRequest req)
        {
            if (req == null || req.lat < -90 || req.lat > 90 || req.lng < -180 || req.lng > 180 || (req.lat == 0 && req.lng == 0))
                throw new BadRequestException("請帶目前位置的經緯度（lat、lng）");

            List<PairMemberItem> team = GetTeam(auId, storyId);

            _locations.Report(storyId, auId, new TeamLocationStore.Entry
            {
                lat = req.lat,
                lng = req.lng,
                accuracy = req.accuracy,
                heading = req.heading,
                updated_at = DateTime.UtcNow
            });

            return ToLocations(auId, storyId, team);
        }

        /// <summary>一起玩的人各自的位置（只看、不回報自己的位置）。</summary>
        public TeamLocationsResponse GetLocations(int auId, int storyId)
        {
            return ToLocations(auId, storyId, GetTeam(auId, storyId));
        }

        /// <summary>
        /// 現在一起玩這份劇本的人；只有其中的人（劇本擁有者或隊伍成員）可以回報、查看位置。
        /// 退出隊伍或隊伍取消後就不在名單裡，位置也不會再被看到。
        /// </summary>
        private List<PairMemberItem> GetTeam(int auId, int storyId)
        {
            List<PairMemberItem> team = _dao.GetTeam(storyId);

            if (team.Count == 0) throw new NotFoundException($"找不到 story_id={storyId} 的劇本");
            if (team.All(m => m.au_id != auId)) throw new UnauthorizedAccessException("你沒有參與這份劇本，無法查看隊友的位置");

            return team;
        }

        private TeamLocationsResponse ToLocations(int auId, int storyId, List<PairMemberItem> team)
        {
            DateTime now = DateTime.UtcNow;

            return new TeamLocationsResponse
            {
                story_id = storyId,
                online_within_seconds = TeamLocationStore.OnlineSeconds,
                members = team.Select(m =>
                {
                    TeamLocationStore.Entry at = _locations.Get(storyId, m.au_id, now);
                    int? secondsAgo = at == null ? null : Math.Max(0, (int)(now - at.updated_at).TotalSeconds);

                    return new TeamMemberLocation
                    {
                        au_id = m.au_id,
                        auth_name = m.auth_name,
                        seat_no = m.seat_no,
                        is_host = m.is_host,
                        is_me = m.au_id == auId,
                        lat = at?.lat,
                        lng = at?.lng,
                        accuracy = at?.accuracy,
                        heading = at?.heading,
                        updated_at = at?.updated_at,
                        seconds_ago = secondsAgo,
                        is_online = secondsAgo < TeamLocationStore.OnlineSeconds
                    };
                }).ToList()
            };
        }

        #endregion
    }
}
