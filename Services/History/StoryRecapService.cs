// 檔案路徑：System\Services\History\StoryRecapService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using backend.dao;
using backend.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace backend.Services
{
    /// <summary>
    /// 劇本回顧：玩完的劇本一次回傳劇情、每一站（真正的景點、劇情、玩家照片、作答結果）、明信片、勳章、Vlog 與旁白。
    /// 旁白語音按需產生：交給 AI 服務轉成 mp3 後存在 wwwroot/audio/narration/，同一段文字與聲音只產生一次。
    /// </summary>
    public class StoryRecapService
    {
        public const string NarrationFolder = "/audio/narration/";

        private readonly StoryRecapDao _dao;
        private readonly VisitorVlogDao _vlogDao;
        private readonly NpcVoiceService _voice;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<StoryRecapService> _logger;
        private readonly string _outputRoot;

        public StoryRecapService(StoryRecapDao dao, VisitorVlogDao vlogDao, NpcVoiceService voice, IHttpClientFactory httpClientFactory,
                                 IWebHostEnvironment environment, IConfiguration configuration, ILogger<StoryRecapService> logger)
        {
            _dao = dao;
            _vlogDao = vlogDao;
            _voice = voice;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            // 旁白語音存放的根目錄，預設就是 wwwroot（測試時改到暫存資料夾）
            _outputRoot = string.IsNullOrWhiteSpace(configuration["Narration:OutputRoot"]) ? environment.WebRootPath : configuration["Narration:OutputRoot"];
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
            string cached = PhysicalPath(NarrationKey(NpcVoiceService.DefaultVoice, narration.text));
            narration.audio_url = File.Exists(cached) ? AudioUrl(NarrationKey(NpcVoiceService.DefaultVoice, narration.text), baseUrl) : null;

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
                    image_url = p.p_imag_url,
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

            return (story, session, await _dao.GetNodesAsync(storyId), await _vlogDao.GetVlogAsync(auId, storyId));
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
        /// 產生旁白語音（mp3）：同一段文字與聲音產生過就直接回傳；沒有就交給 AI 服務轉成語音並存到後端。
        /// </summary>
        public async Task<StoryRecapNarration> NarrateAsync(int auId, int storyId, string voice, string baseUrl)
        {
            var (story, _, nodes, vlog) = await LoadAsync(auId, storyId);
            StoryRecapNarration narration = BuildNarration(story, nodes, vlog);
            voice = string.IsNullOrWhiteSpace(voice) ? NpcVoiceService.DefaultVoice : voice.Trim();

            string key = NarrationKey(voice, narration.text);
            string physical = PhysicalPath(key);
            if (File.Exists(physical))
            {
                narration.audio_url = AudioUrl(key, baseUrl);
                return narration;
            }

            NpcSpeakResponse speech = await _voice.SpeakAsync(narration.text, voice);   // AI 服務失敗會丟出錯誤
            try
            {
                HttpClient client = _httpClientFactory.CreateClient();
                byte[] audio = await client.GetByteArrayAsync(speech.audio_url);
                Directory.CreateDirectory(Path.GetDirectoryName(physical));
                await File.WriteAllBytesAsync(physical, audio);
                narration.audio_url = AudioUrl(key, baseUrl);
            }
            catch (Exception e)
            {
                // 存不下來就先直接給 AI 服務的網址，下次再試著存
                _logger.LogWarning(e, "旁白語音存檔失敗，改回傳 AI 服務的網址：{Url}", speech.audio_url);
                narration.audio_url = speech.audio_url;
            }
            return narration;
        }

        /// <summary>聲音 + 文字的 SHA-1，當作語音檔名</summary>
        internal static string NarrationKey(string voice, string text) =>
            Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(voice + "\n" + text))).ToLowerInvariant();

        private string PhysicalPath(string key) => Path.Combine(_outputRoot, "audio", "narration", key + ".mp3");

        private static string AudioUrl(string key, string baseUrl) => $"{baseUrl?.TrimEnd('/')}{NarrationFolder}{key}.mp3";

        #endregion
    }
}
