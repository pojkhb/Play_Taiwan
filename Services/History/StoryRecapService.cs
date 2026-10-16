// 檔案路徑：System\Services\History\StoryRecapService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using backend.dao;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>
    /// 劇本回顧：玩完的劇本一次回傳劇情、每一站（真正的景點、劇情、玩家照片、作答結果）、明信片、勳章、Vlog 與旁白。
    /// 旁白語音按需產生：由劇本的 NPC 唸，交給 NpcVoiceService（AI 排隊產生 wav、存在後端），同一段文字與 NPC 只產生一次。
    /// </summary>
    public class StoryRecapService
    {
        private readonly StoryRecapDao _dao;
        private readonly VisitorVlogDao _vlogDao;
        private readonly NpcVoiceService _voice;
        private readonly Neo4j.PlaceLookupService _placeLookup;

        public StoryRecapService(StoryRecapDao dao, VisitorVlogDao vlogDao, NpcVoiceService voice, Neo4j.PlaceLookupService placeLookup)
        {
            _dao = dao;
            _placeLookup = placeLookup;
            _vlogDao = vlogDao;
            _voice = voice;
        }

        #region 回顧

        public async Task<StoryRecapResponse> GetRecapAsync(int auId, int storyId, string baseUrl)
        {
            var (story, session, nodeRows, vlog) = await LoadAsync(auId, storyId);
            var tasks = await _dao.GetTasksAsync(auId, storyId);
            var postcards = await _dao.GetPostcardsAsync(auId, storyId);
            var badge = await _dao.GetBadgeAsync(auId, storyId);

            // 玩家拍的照片（只留圖片、去掉重複），依拍攝的站分組
            var seen = new HashSet<string>();
            var photosByNode = new Dictionary<int, List<string>>();
            int photoCount = 0;
            foreach (var m in await _vlogDao.GetMediaAsync(auId, storyId))
            {
                if (!VlogAiGateway.LooksLikeImage(m.url) || !seen.Add(m.url)) continue;
                photoCount++;
                int node = m.node_id ?? 0;
                if (!photosByNode.TryGetValue(node, out List<string> list))
                    photosByNode[node] = list = new List<string>();
                list.Add(m.url);
            }

            StoryRecapNarration narration = BuildNarration(story, nodeRows, vlog);
            narration.npc_name = await _voice.ResolveNpcAsync(null, story.npc_name);
            string cached = _voice.CachedAudioPath(narration.npc_name, narration.text);
            narration.audio_url = cached == null ? null : $"{baseUrl?.TrimEnd('/')}{cached}";

            return new StoryRecapResponse
            {
                story_id = story.s_id,
                title = story.story_title,
                prologue = story.story_prologue,
                synopsis = story.story_synopsis,
                city_name = story.city_name,
                district_name = story.district_name,
                is_night_mode = story.is_night_mode == 1,
                started_at = session.started_at,
                completed_at = session.completed_at,
                play_minutes = session.started_at.HasValue && session.completed_at.HasValue
                    ? (int)Math.Round((session.completed_at.Value - session.started_at.Value).TotalMinutes)
                    : null,
                stats = new StoryRecapStats
                {
                    node_count = nodeRows.Count,
                    tasks_answered = tasks.Count(t => t.answered),
                    tasks_correct = tasks.Count(t => t.any_correct == 1),
                    photo_count = photoCount,
                    postcard_count = postcards.Count
                },
                nodes = nodeRows.Select(n => new StoryRecapNode
                {
                    node_id = n.sn_id,
                    order = n.sn_order,
                    chapter_title = n.sn_title,
                    location_codename = n.location_codename,
                    place_name = n.place_name ?? n.sn_title,
                    place_image_url = n.place_image,
                    opening_text = n.sn_opening_text,
                    success_text = n.sn_success_text,
                    photos = photosByNode.TryGetValue(n.sn_id, out List<string> photos) ? photos : new List<string>(),
                    tasks = tasks.Where(t => t.node_id == n.sn_id).Select(t => new StoryRecapTask
                    {
                        task_id = t.task_id,
                        type_name = t.type_name,
                        question = t.question,
                        answered = t.answered,
                        is_correct = t.any_correct.HasValue ? t.any_correct == 1 : null
                    }).ToList()
                }).ToList(),
                postcards = postcards.Select(p => new StoryRecapPostcard
                {
                    postcard_id = p.p_id,
                    name = p.p_name,
                    image_url = $"{baseUrl?.TrimEnd('/')}{PostcardCatalogService.ImagePath(p.p_id)}",          // 顯示網址，打開就是圖片
                    download_url = $"{baseUrl?.TrimEnd('/')}{PostcardCatalogService.DownloadPath(p.p_id)}",   // 下載網址，打開會存成檔案
                    is_night_edition = p.is_night == 1
                }).ToList(),
                badge = badge == null ? null : new StoryRecapBadge
                {
                    badge_id = badge.b_id,
                    name = badge.b_name,
                    series = BadgeService.CategoryLabel(badge.b_fication),
                    image_url = badge.b_image
                },
                vlog = vlog == null ? null : new StoryRecapVlog
                {
                    vlog_id = vlog.av_id,
                    status = vlog.av_vlog_status,
                    video_url = vlog.av_video_url,
                    thumbnail_url = vlog.av_thumbnail
                },
                narration = narration
            };
        }

        /// <summary>劇本、玩完的紀錄、每一站、Vlog；找不到劇本或還沒玩完就丟出錯誤</summary>
        private async Task<(StoryRecapDao.StoryRow, StoryRecapDao.CompletedSession, List<StoryRecapDao.NodeRow>, VisitorVlogDao.AuVlogRow)>
            LoadAsync(int auId, int storyId)
        {
            StoryRecapDao.StoryRow story = await _dao.GetStoryAsync(storyId)
                ?? throw new KeyNotFoundException("找不到此劇本：" + storyId);
            StoryRecapDao.CompletedSession session = await _dao.GetCompletedSessionAsync(auId, storyId)
                ?? throw new InvalidOperationException("還沒完成這個劇本，完成後才能回顧");

            // 每一站的景點照片直接用景點 ID 向 Neo4j 拿
            List<StoryRecapDao.NodeRow> nodes = await _dao.GetNodesAsync(storyId);
            Dictionary<string, string> images = await _placeLookup.GetPlaceImagesAsync(nodes.Select(n => n.place_id));
            foreach (StoryRecapDao.NodeRow node in nodes)
                node.place_image = node.place_id != null ? images.GetValueOrDefault(node.place_id) : null;

            return (story, session, nodes, await _vlogDao.GetVlogAsync(auId, storyId));
        }

        #endregion

        #region 旁白

        /// <summary>有 Vlog 旁白就用玩家確認過的版本；沒有就依各站劇情組一段回顧旁白</summary>
        private static StoryRecapNarration BuildNarration(StoryRecapDao.StoryRow story, List<StoryRecapDao.NodeRow> nodes, VisitorVlogDao.AuVlogRow vlog)
        {
            string script = vlog?.av_final_script?.Trim();
            if (!string.IsNullOrEmpty(script))
            {
                return new StoryRecapNarration
                {
                    source = "vlog",
                    text = script.Length > NpcVoiceService.MaxTextLength ? script[..(NpcVoiceService.MaxTextLength - 1)] + "…" : script
                };
            }

            var stops = nodes.Select(n => (place: n.place_name ?? n.sn_title,
                                            text: string.IsNullOrWhiteSpace(n.sn_success_text) ? n.sn_opening_text : n.sn_success_text)).ToList();
            return new StoryRecapNarration { source = "story", text = BuildStoryNarration(story.story_title, stops) };
        }

        /// <summary>
        /// 依各站劇情組成回顧旁白：「〈標題〉旅程回顧。第1站，〈景點〉。〈劇情〉……」。
        /// 超過語音上限時，每站只留第一句；還是太長就截斷。
        /// </summary>
        internal static string BuildStoryNarration(string title, IList<(string place, string text)> stops, int maxLength = NpcVoiceService.MaxTextLength)
        {
            string Compose(Func<string, string> shorten)
            {
                var sb = new StringBuilder($"「{title}」旅程回顧。");
                for (int i = 0; i < stops.Count; i++)
                {
                    sb.Append($"第{i + 1}站，{stops[i].place}。");
                    sb.Append(shorten((stops[i].text ?? "").Trim()));
                }
                sb.Append($"{stops.Count}站的冒險到這裡告一段落，謝謝你走完這趟旅程。");
                return sb.ToString();
            }

            string full = Compose(t => t);
            if (full.Length <= maxLength) return full;

            string brief = Compose(FirstSentence);
            return brief.Length <= maxLength ? brief : brief[..(maxLength - 1)] + "…";
        }

        private static string FirstSentence(string text)
        {
            int end = text.IndexOfAny(new[] { '。', '！', '？', '!', '?' });
            return end < 0 ? text : text[..(end + 1)];
        }

        /// <summary>
        /// 產生旁白語音：沒指定 NPC 時由劇本的 NPC 唸。做好過直接回傳 done，否則交給 AI 排隊，
        /// 回傳 processing 與 job_id（用 GET /api/Npc/Speak/{job_id} 查進度）
        /// </summary>
        public async Task<NpcSpeakResponse> NarrateAsync(int auId, int storyId, string npcName)
        {
            var (story, _, nodes, vlog) = await LoadAsync(auId, storyId);
            StoryRecapNarration narration = BuildNarration(story, nodes, vlog);
            return await _voice.RequestAsync(await _voice.ResolveNpcAsync(npcName, story.npc_name), narration.text);
        }

        #endregion
    }
}
