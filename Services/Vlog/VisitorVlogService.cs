// 檔案路徑：System\Services\Vlog\VisitorVlogService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;
using backend.dao;
using backend.utils;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>
    /// 遊客 VLOG：
    /// 1. Preview：遊戲結束給 story_id → 從資料庫組出走過的景點與遊玩時長 → AI 旁白草稿，回傳給玩家確認。
    /// 2. CreateFinal：story_id + 確認後的旁白 → 抓這次遊玩拍的照片、依景點命名打包成 zip，
    ///    連同景點資料（spot_meta_json）送去合成影片。
    /// 3. Status：輪詢合成進度，完成時寫回 au_vlog。
    /// </summary>
    public class VisitorVlogService
    {
        private const string PreviewPath = "/api/visitor/vlog/preview";

        /// <summary>找不到任何遊玩時間紀錄時送給 AI 的時長</summary>
        private const string DefaultPlayTime = "2.5小時";

        private static readonly JsonSerializerOptions MetaJsonOptions = new()
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        private readonly VisitorVlogDao _dao;
        private readonly VlogAiGateway _ai;

        public VisitorVlogService(VisitorVlogDao dao, VlogAiGateway ai)
        {
            _dao = dao;
            _ai = ai;
        }

        #region 1. 旁白草稿

        public async Task<VisitorVlogPreviewResponse> PreviewAsync(int auId, int storyId)
        {
            var (story, session) = await LoadPlayAsync(auId, storyId);
            PlayMaterial material = await CollectAsync(auId, storyId);
            if (material.spots.Count == 0)
                throw new Exception($"劇本 story_id={storyId} 沒有任何節點");

            string playTime = PlayTime(session, material.rows);
            int tasksDone = material.rows.Sum(r => r.task_count);

            var payload = new
            {
                spot_history = material.spots.Select(s => new SpotHistoryItem
                {
                    spot_name = s.spot_name,
                    location_codename = s.location_codename,
                    visit_time = s.visit_time
                }),
                player_play_time = playTime,
                game_tasks_completed = tasksDone > 0 ? $"完成 {tasksDone} 個任務" : "完成解謎與尋寶任務"
            };

            using HttpClient client = _ai.CreateClient(TimeSpan.FromMinutes(3));
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PostAsync(AiServiceConfig.Url(PreviewPath), content);
            var result = await VlogAiGateway.ReadJsonAsync<VisitorVlogPreviewApiResponse>(response, "產生遊客 VLOG 草稿");

            VisitorDraftPreview draft = result?.draft_preview
                ?? throw new Exception($"AI 服務沒有回傳草稿（status={result?.status}）");

            List<string> keywords = draft.seo_keywords ?? new List<string>();
            int avId = await _dao.SaveDraftAsync(auId, storyId, story.story_title, draft.promo_copy, string.Join(",", keywords));

            return new VisitorVlogPreviewResponse
            {
                av_id = avId,
                story_id = storyId,
                story_title = story.story_title,
                play_time = playTime,
                photo_count = material.photos.Count,
                spots = material.spots,
                script = draft.suggested_script,
                promo_copy = draft.promo_copy,
                seo_keywords = keywords,
                itinerary = draft.itinerary ?? new List<ItineraryItem>()
            };
        }

        private static string PlayTime(VisitorVlogDao.SessionRow session, List<VisitorVlogDao.SpotRow> rows)
        {
            DateTime? start = session?.started_at ?? rows.Min(r => r.first_at);
            DateTime? end = session?.completed_at ?? session?.last_played_at ?? rows.Max(r => r.last_at);
            if (!start.HasValue || !end.HasValue || end <= start) return DefaultPlayTime;

            TimeSpan span = end.Value - start.Value;
            return span.TotalHours >= 1 ? $"{span.TotalHours:0.0}小時" : $"{span.TotalMinutes:0}分鐘";
        }

        #endregion

        #region 2. 送出影片合成

        public async Task<VisitorVlogTaskResponse> CreateFinalAsync(int auId, VisitorVlogCreateFinalRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.final_script))
                throw new Exception("請提供最終旁白 (final_script)");

            var (story, _) = await LoadPlayAsync(auId, req.story_id);

            VisitorVlogDao.AuVlogRow vlog = await _dao.GetVlogAsync(auId, req.story_id);
            if (vlog?.av_vlog_status == 2)
                throw new Exception("影片已在合成中，請用 Status 查詢進度");

            PlayMaterial material = await CollectAsync(auId, req.story_id);
            if (material.photos.Count == 0)
                throw new Exception("這次遊玩沒有拍到任何照片，無法合成影片");

            List<(string entryName, string url)> entries = NameZipEntries(material.photos);
            string spotMetaJson = JsonSerializer.Serialize(material.spots, MetaJsonOptions);

            string promoCopy = req.promo_copy ?? vlog?.av_promo_copy;
            string seoKeywords = req.seo_keywords != null ? string.Join(",", req.seo_keywords) : vlog?.av_seo_keywords;
            int avId = vlog?.av_id ?? await _dao.SaveDraftAsync(auId, req.story_id, story.story_title, promoCopy, seoKeywords);

            VlogCreateFinalApiResponse task;
            try
            {
                byte[] zip = await _ai.BuildZipAsync(entries);
                task = await _ai.CreateFinalAsync(req.final_script.Trim(), zip, null, spotMetaJson);
            }
            catch (Exception e)
            {
                await _dao.MarkFailedAsync(avId, e.Message);
                throw;
            }

            await _dao.MarkProcessingAsync(avId, req.final_script.Trim(), promoCopy, seoKeywords);

            return new VisitorVlogTaskResponse
            {
                av_id = avId,
                story_id = req.story_id,
                task_id = task.task_id,
                photo_count = entries.Count,
                status = 2,
                status_text = VlogAiGateway.StatusText(2)
            };
        }

        /// <summary>
        /// zip 內檔名 = {景點順序}_{第幾張}，例如 03_02.jpg 是第 3 站的第 2 張；沒掛節點的照片用 99_xx 排在最後。
        /// 同時把檔名寫回各景點的 images，讓 spot_meta_json 對得上照片。
        /// </summary>
        private static List<(string entryName, string url)> NameZipEntries(List<(VisitorVlogSpot spot, string url)> photos)
        {
            var counters = new Dictionary<int, int>();
            var entries = new List<(string, string)>();

            foreach (var (spot, url) in photos.OrderBy(p => p.spot?.order ?? int.MaxValue))
            {
                int group = spot?.order ?? 99;
                counters[group] = counters.TryGetValue(group, out int n) ? n + 1 : 1;

                string name = $"{group:D2}_{counters[group]:D2}{VlogAiGateway.ImageExtension(url)}";
                spot?.images.Add(name);
                entries.Add((name, url));
            }
            return entries;
        }

        #endregion

        #region 3. 查詢進度

        /// <summary>
        /// 回傳這個劇本的 VLOG。處理中且有帶 task_id 時會向 AI 查一次進度，完成或失敗就寫回 au_vlog。
        /// </summary>
        public async Task<VisitorVlogStatusResponse> GetStatusAsync(int auId, int storyId, string taskId)
        {
            VisitorVlogDao.AuVlogRow vlog = await _dao.GetVlogAsync(auId, storyId)
                ?? throw new Exception("這個劇本還沒有 VLOG，請先呼叫 Preview");

            if (vlog.av_vlog_status == 2 && !string.IsNullOrWhiteSpace(taskId))
            {
                VlogTaskStatusApiResponse r = await _ai.CheckStatusAsync(taskId);
                switch (VlogAiGateway.ParseState(r))
                {
                    case VlogTaskState.Ready:
                        vlog.av_video_url = VlogAiGateway.ResolveUrl(r.download_url);
                        vlog.av_vlog_status = 3;
                        vlog.error_message = null;
                        await _dao.MarkCompletedAsync(vlog.av_id, vlog.av_video_url);
                        break;
                    case VlogTaskState.Failed:
                        vlog.av_vlog_status = 4;
                        vlog.error_message = VlogAiGateway.FailureMessage(r);
                        await _dao.MarkFailedAsync(vlog.av_id, vlog.error_message);
                        break;
                }
            }

            return new VisitorVlogStatusResponse
            {
                av_id = vlog.av_id,
                story_id = storyId,
                status = vlog.av_vlog_status,
                status_text = VlogAiGateway.StatusText(vlog.av_vlog_status),
                title = vlog.av_title,
                final_script = vlog.av_final_script,
                promo_copy = vlog.av_promo_copy,
                seo_keywords = VlogAiGateway.SplitList(vlog.av_seo_keywords),
                video_url = vlog.av_video_url,
                thumbnail = vlog.av_thumbnail,
                error_message = vlog.error_message,
                updated_at = vlog.updated_at
            };
        }

        #endregion

        #region 素材整理

        /// <summary>玩家必須玩過這個劇本（或是劇本建立者）才能做 VLOG</summary>
        private async Task<(VisitorVlogDao.StoryRow story, VisitorVlogDao.SessionRow session)> LoadPlayAsync(int auId, int storyId)
        {
            VisitorVlogDao.StoryRow story = await _dao.GetStoryAsync(storyId)
                ?? throw new Exception($"找不到劇本 story_id={storyId}");

            VisitorVlogDao.SessionRow session = await _dao.GetSessionAsync(auId, storyId);
            if (session == null && story.au_id != auId)
                throw new Exception("你還沒有玩過這個劇本");

            return (story, session);
        }

        private class PlayMaterial
        {
            public List<VisitorVlogDao.SpotRow> rows;
            public List<VisitorVlogSpot> spots;
            public List<(VisitorVlogSpot spot, string url)> photos;
        }

        /// <summary>景點清單（隱藏節點只有玩家真的去過才算）＋ 這次遊玩的照片（去重、排除影片與錄音）</summary>
        private async Task<PlayMaterial> CollectAsync(int auId, int storyId)
        {
            List<VisitorVlogDao.SpotRow> rows = await _dao.GetSpotsAsync(auId, storyId);

            var spotByNode = new Dictionary<int, VisitorVlogSpot>();
            var spots = new List<VisitorVlogSpot>();
            foreach (var r in rows.Where(r => r.is_hidden != 1 || r.task_count > 0))
            {
                var spot = new VisitorVlogSpot
                {
                    order = r.sn_order,
                    spot_name = FirstNonEmpty(r.place_name, r.sn_title, r.location_codename) ?? "未知景點",
                    location_codename = r.location_codename,
                    node_title = r.sn_title,
                    address = r.p_address,
                    lat = (double?)r.lat,
                    lng = (double?)r.lng,
                    visit_time = r.first_at?.ToString("yyyy-MM-dd HH:mm"),
                    images = new List<string>()
                };
                spots.Add(spot);
                spotByNode[r.sn_id] = spot;
            }

            var seen = new HashSet<string>();
            var photos = new List<(VisitorVlogSpot spot, string url)>();
            foreach (var m in await _dao.GetMediaAsync(auId, storyId))
            {
                if (!VlogAiGateway.LooksLikeImage(m.url) || !seen.Add(m.url)) continue;
                spotByNode.TryGetValue(m.node_id ?? 0, out VisitorVlogSpot spot);
                photos.Add((spot, m.url));
            }

            foreach (var g in photos.Where(p => p.spot != null).GroupBy(p => p.spot))
                g.Key.photo_count = g.Count();

            return new PlayMaterial { rows = rows, spots = spots, photos = photos };
        }

        private static string FirstNonEmpty(params string[] values) =>
            values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));

        #endregion
    }
}
