// 檔案路徑：System\Services\Merchant\MerchantVlogService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using backend.dao;
using backend.Models;
using backend.ViewModels;
using backend.utils;

namespace backend.Services
{
    /// <summary>
    /// 商家 VLOG：
    /// 1. Preview：店家名稱、營業時間、地址、敘事語氣、推廣資訊 → AI 旁白草稿、推薦配文、TAG；照片存進 merchant_media_asset。
    /// 2. CreateFinal：商家確認旁白後，把專案照片打包成 zip（+ 背景音樂）送去合成影片。
    /// 3. Status：輪詢合成進度，完成時寫回影片網址。
    /// </summary>
    public class MerchantVlogService
    {
        private const string PreviewPath = "/api/merchant/vlog/preview";
        public const int MaxImages = 30;
        public const long MaxImageBytes = 20 * 1024 * 1024;

        private readonly IConfiguration _configuration;
        private readonly VlogDao _dao;
        private readonly VlogAiGateway _ai;

        public MerchantVlogService(IConfiguration configuration, VlogDao dao, VlogAiGateway ai)
        {
            _configuration = configuration;
            _dao = dao;
            _ai = ai;
        }

        public Task<List<NarrativeToneItem>> GetTonesAsync() => _dao.GetActiveTonesAsync();

        #region 1. 產生草稿

        /// <param name="siteBaseUrl">本站根網址（組照片網址用），例如 https://host</param>
        public async Task<MerchantVlogPreviewResponse> PreviewAsync(int auId, MerchantVlogPreviewRequest req, string siteBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(req.promo_text))
                throw new Exception("請填寫推廣資訊 (promo_text)");

            List<IFormFile> images = (req.images ?? new List<IFormFile>()).Where(f => f != null && f.Length > 0).ToList();
            ValidateImages(images);

            MerchantMedia media = null;
            if (req.mm_id.HasValue)
            {
                media = await _dao.GetMediaAsync(req.mm_id.Value, auId) ?? throw new Exception("找不到此影音專案");
                if (media.mm_status == 2) throw new Exception("影片合成中，暫時不能修改");
            }
            else if (images.Count == 0)
            {
                throw new Exception("請至少上傳一張照片");
            }

            VlogDao.StoreRow store = await _dao.GetStoreAsync(auId);
            string storeName = FirstNonEmpty(req.store_name, store?.store_name) ?? throw new Exception("請填寫店家名稱 (store_name)");
            string address = FirstNonEmpty(req.address, store?.full_address);
            string openTime = FirstNonEmpty(req.open_time);

            NarrativeToneItem tone = null;
            if (req.nt_id.HasValue)
                tone = await _dao.GetToneAsync(req.nt_id.Value) ?? throw new Exception("找不到此敘事語氣 (nt_id)");

            // 先拿到 AI 草稿才寫資料庫，AI 失敗就不留半套專案
            MerchantDraftPreview draft = await RequestPreviewAsync(storeName, BuildPromoFocus(req.promo_text, openTime, address, tone));

            string title = $"{storeName}｜{Truncate(req.promo_text.Trim(), 20)}";
            List<string> hashtags = VlogAiGateway.ToHashtags(draft.seo_keywords);
            string hashtagText = string.Join(",", hashtags);

            int mmId;
            if (media == null)
            {
                mmId = await _dao.CreateDraftAsync(auId, tone?.nt_id, title, req.promo_text, draft.promo_copy, hashtagText);
            }
            else
            {
                mmId = media.mm_id;
                await _dao.UpdateDraftAsync(mmId, tone?.nt_id, title, req.promo_text, draft.promo_copy, hashtagText);
            }

            if (images.Count > 0)
            {
                List<string> urls = await SaveImagesAsync(mmId, images, siteBaseUrl);
                List<string> replaced = await _dao.ReplaceAssetsAsync(auId, mmId, urls);
                DeleteLocalFiles(replaced);
            }

            return new MerchantVlogPreviewResponse
            {
                mm_id = mmId,
                store_name = storeName,
                open_time = openTime,
                address = address,
                tone_name = tone?.nt_name,
                script = draft.tw_script,
                caption = draft.promo_copy,
                hashtags = hashtags,
                target_audience = draft.target_audience ?? new List<string>(),
                image_urls = await _dao.GetAssetUrlsAsync(mmId)
            };
        }

        /// <summary>AI 預覽端點只收 merchant_name 與 promo_focus，營業時間、地址、語氣都併進 promo_focus</summary>
        private static string BuildPromoFocus(string promoText, string openTime, string address, NarrativeToneItem tone)
        {
            var sb = new StringBuilder(promoText.Trim());
            if (!string.IsNullOrWhiteSpace(openTime)) sb.Append($"\n營業時間：{openTime.Trim()}");
            if (!string.IsNullOrWhiteSpace(address)) sb.Append($"\n地址：{address.Trim()}");
            if (tone != null)
            {
                sb.Append($"\n敘事語氣：{tone.nt_name}");
                if (!string.IsNullOrWhiteSpace(tone.nt_prompt)) sb.Append($"（{tone.nt_prompt.Trim()}）");
            }
            return sb.ToString();
        }

        private async Task<MerchantDraftPreview> RequestPreviewAsync(string storeName, string promoFocus)
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(storeName), "merchant_name" },
                { new StringContent(promoFocus), "promo_focus" }
            };

            using HttpClient client = _ai.CreateClient(TimeSpan.FromMinutes(3));
            using HttpResponseMessage response = await client.PostAsync(AiServiceConfig.Url(PreviewPath), form);
            var result = await VlogAiGateway.ReadJsonAsync<MerchantVlogPreviewApiResponse>(response, "產生商家 VLOG 草稿");

            if (result?.status != "success" || result.draft_preview == null)
                throw new Exception($"AI 服務沒有回傳草稿（status={result?.status}）");

            return result.draft_preview;
        }

        private static void ValidateImages(List<IFormFile> images)
        {
            if (images.Count > MaxImages)
                throw new Exception($"照片最多 {MaxImages} 張");

            foreach (IFormFile image in images)
            {
                string ext = Path.GetExtension(image.FileName);
                bool isImage = VlogAiGateway.ImageExtensions.Contains(ext)
                               || (image.ContentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase);
                if (!isImage)
                    throw new Exception($"「{image.FileName}」不是照片，僅接受 jpg/png/webp/heic");
                if (image.Length > MaxImageBytes)
                    throw new Exception($"「{image.FileName}」超過 {MaxImageBytes / 1024 / 1024}MB");
            }
        }

        /// <summary>照片存到 wwwroot/uploads/merchant_vlog/{mm_id}/，回傳可公開存取的網址</summary>
        private static async Task<List<string>> SaveImagesAsync(int mmId, List<IFormFile> images, string siteBaseUrl)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "merchant_vlog", mmId.ToString());
            Directory.CreateDirectory(folder);

            var urls = new List<string>();
            foreach (IFormFile image in images)
            {
                string ext = VlogAiGateway.ImageExtension(image.FileName);
                string fileName = Guid.NewGuid().ToString("N") + ext;

                using (var stream = new FileStream(Path.Combine(folder, fileName), FileMode.Create))
                {
                    await image.CopyToAsync(stream);
                }
                urls.Add($"{siteBaseUrl}/uploads/merchant_vlog/{mmId}/{fileName}");
            }
            return urls;
        }

        private static void DeleteLocalFiles(IEnumerable<string> urls)
        {
            foreach (string url in urls)
            {
                string path = VlogAiGateway.LocalUploadPath(url);
                if (path == null) continue;
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        #endregion

        #region 2. 送出影片合成

        public async Task<MerchantVlogTaskResponse> CreateFinalAsync(int auId, MerchantVlogCreateFinalRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.final_script))
                throw new Exception("請提供最終旁白 (final_script)");

            MerchantMedia media = await _dao.GetMediaAsync(req.mm_id, auId) ?? throw new Exception("找不到此影音專案");
            if (media.mm_status == 2)
                throw new Exception("影片已在合成中，請用 Status 查詢進度");

            List<string> urls = await _dao.GetAssetUrlsAsync(media.mm_id);
            if (urls.Count == 0)
                throw new Exception("這個專案還沒有照片，請先在 Preview 上傳");

            string caption = req.caption ?? media.mm_text;
            string hashtags = req.hashtags != null ? string.Join(",", VlogAiGateway.ToHashtags(req.hashtags)) : media.mm_hashtage;

            VlogCreateFinalApiResponse task;
            try
            {
                byte[] zip = await _ai.BuildZipAsync(urls.Select((url, i) => ($"{i + 1:D2}{VlogAiGateway.ImageExtension(url)}", url)));
                task = await _ai.CreateFinalAsync(req.final_script.Trim(), zip, LoadBgm(), null);
            }
            catch (Exception e)
            {
                await _dao.MarkFailedAsync(media.mm_id, e.Message);
                throw;
            }

            await _dao.MarkProcessingAsync(media.mm_id, task.task_id, caption, hashtags);

            return new MerchantVlogTaskResponse
            {
                mm_id = media.mm_id,
                task_id = task.task_id,
                status = 2,
                status_text = VlogAiGateway.StatusText(2)
            };
        }

        /// <summary>預設背景音樂；檔案不存在就不附（由 AI 服務決定配樂）</summary>
        private byte[] LoadBgm()
        {
            string bgmPath = _configuration["MerchantVlogSettings:DefaultBgmPath"]
                             ?? Path.Combine("wwwroot", "bgm", "default_bgm.mp3");
            return File.Exists(bgmPath) ? File.ReadAllBytes(bgmPath) : null;
        }

        #endregion

        #region 3. 查詢進度

        /// <summary>處理中的專案會順便向 AI 查進度，完成或失敗就寫回資料庫</summary>
        public async Task<MerchantVlogStatusResponse> GetStatusAsync(int auId, int mmId)
        {
            MerchantMedia media = await _dao.GetMediaAsync(mmId, auId) ?? throw new Exception("找不到此影音專案");

            if (media.mm_status == 2 && !string.IsNullOrWhiteSpace(media.mm_task_id))
            {
                VlogTaskStatusApiResponse r = await _ai.CheckStatusAsync(media.mm_task_id);
                switch (VlogAiGateway.ParseState(r))
                {
                    case VlogTaskState.Ready:
                        media.mm_video_url = VlogAiGateway.ResolveUrl(r.download_url);
                        media.mm_status = 3;
                        media.error_message = null;
                        await _dao.MarkCompletedAsync(mmId, media.mm_video_url);
                        break;
                    case VlogTaskState.Failed:
                        media.mm_status = 4;
                        media.error_message = VlogAiGateway.FailureMessage(r);
                        await _dao.MarkFailedAsync(mmId, media.error_message);
                        break;
                }
            }

            return new MerchantVlogStatusResponse
            {
                mm_id = media.mm_id,
                status = media.mm_status,
                status_text = VlogAiGateway.StatusText(media.mm_status),
                title = media.mm_title,
                caption = media.mm_text,
                hashtags = VlogAiGateway.SplitList(media.mm_hashtage),
                video_url = media.mm_video_url,
                thumbnail = media.mm_thumbnail,
                error_message = media.error_message,
                updated_at = media.updated_at
            };
        }

        #endregion

        private static string FirstNonEmpty(params string[] values) =>
            values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));

        private static string Truncate(string s, int max) => s.Length > max ? s.Substring(0, max) + "…" : s;
    }
}
