// 檔案路徑：System\Services\MerchantVlogService.cs
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using backend.dao;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>
    /// 商家端 Vlog 服務層：負責呼叫外部 AI 服務產生行銷腳本預覽，
    /// 以及打包照片＋固定背景音樂，送出正式影片合成任務。
    /// 影片真正完成時才寫入 ep_vlog（因 completed_at 為 not null，商家端此欄位 story_id 固定存 null）。
    /// </summary>
    public class MerchantVlogService
    {
        private readonly IConfiguration _configuration;
        private readonly VlogDao _dao;
        private const string BaseUrl = "https://vlog.angelalala.com";

        public MerchantVlogService(IConfiguration configuration, VlogDao dao)
        {
            _configuration = configuration;
            _dao = dao;
        }

        #region 1. 產生行銷腳本預覽

        public async Task<MerchantVlogPreviewApiResponse> GetPreviewAsync(MerchantVlogPreviewRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.merchant_name))
                throw new Exception("請提供店家名稱 (merchant_name)");

            if (string.IsNullOrWhiteSpace(request.promo_focus))
                throw new Exception("請提供推薦資訊 (promo_focus)");

            string combinedPromoFocus = request.promo_focus;
            if (!string.IsNullOrWhiteSpace(request.narration_tone))
            {
                combinedPromoFocus = $"{request.promo_focus}（敘事語氣：{request.narration_tone}）";
            }

            using var client = new HttpClient();
            using var form = new MultipartFormDataContent
            {
                { new StringContent(request.merchant_name), "merchant_name" },
                { new StringContent(combinedPromoFocus), "promo_focus" }
            };

            var response = await client.PostAsync($"{BaseUrl}/api/merchant/vlog/preview", form);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"商家 Vlog 預覽 API 呼叫失敗，狀態碼: {response.StatusCode}，內容: {errorBody}");
            }

            var responseString = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<MerchantVlogPreviewApiResponse>(responseString,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.status != "success")
            {
                throw new Exception("外部 API 未成功回傳預覽腳本");
            }

            return result;
        }

        #endregion

        #region 2. 正式合成影片

        /// <summary>
        /// 送出正式影片合成任務。不立即寫入資料庫（completed_at 不可為空），
        /// 只回傳 task_id，等 Status 查到 ready 才真正寫入完成紀錄。
        /// </summary>
        public async Task<VlogCreateFinalApiResponse> CreateFinalVlogAsync(MerchantVlogCreateFinalRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.final_script))
                throw new Exception("請提供最終旁白文字 (final_script)");

            if (request.images == null || request.images.Count == 0)
                throw new Exception("請至少上傳一張照片");

            byte[] zipBytes = await BuildImageZipAsync(request.images);
            byte[] bgmBytes = GetDefaultBgmBytes();

            using var client = new HttpClient();
            using var form = new MultipartFormDataContent
            {
                { new StringContent(request.final_script), "final_script" }
            };

            var zipContent = new ByteArrayContent(zipBytes);
            zipContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
            form.Add(zipContent, "image_zip", "images.zip");

            var bgmContent = new ByteArrayContent(bgmBytes);
            bgmContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/mpeg");
            form.Add(bgmContent, "bgm_file", "default_bgm.mp3");

            if (!string.IsNullOrWhiteSpace(request.spot_meta_json))
            {
                form.Add(new StringContent(request.spot_meta_json), "spot_meta_json");
            }

            var response = await client.PostAsync($"{BaseUrl}/api/visitor/vlog/create_final", form);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"影片合成 API 呼叫失敗，狀態碼: {response.StatusCode}，內容: {errorBody}");
            }

            var responseString = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<VlogCreateFinalApiResponse>(responseString,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.status != "processing")
            {
                throw new Exception("外部 API 未成功建立影片合成任務");
            }

            return result;
        }

        private static async Task<byte[]> BuildImageZipAsync(System.Collections.Generic.List<Microsoft.AspNetCore.Http.IFormFile> images)
        {
            using var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
            {
                int index = 1;
                foreach (var image in images)
                {
                    if (image == null || image.Length == 0) continue;

                    string extension = Path.GetExtension(image.FileName);
                    if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";

                    var entry = archive.CreateEntry($"{index:D2}{extension}", CompressionLevel.Fastest);
                    using var entryStream = entry.Open();
                    using var sourceStream = image.OpenReadStream();
                    await sourceStream.CopyToAsync(entryStream);
                    index++;
                }
            }

            return zipStream.ToArray();
        }

        private byte[] GetDefaultBgmBytes()
        {
            string bgmPath = _configuration["MerchantVlogSettings:DefaultBgmPath"]
                              ?? Path.Combine("wwwroot", "bgm", "default_bgm.mp3");

            if (!File.Exists(bgmPath))
            {
                throw new Exception($"找不到預設背景音樂檔案，請確認路徑: {bgmPath}");
            }

            return File.ReadAllBytes(bgmPath);
        }

        #endregion

        #region 3. 查詢任務狀態（輪詢），完成時才寫入 ep_vlog

        /// <summary>
        /// 查詢外部合成進度。若狀態為 ready 且尚無此 vlog_id 的紀錄，才寫入一筆完成紀錄。
        /// 商家端無對應劇本，story_id 固定存 null。
        /// </summary>
        public async Task<VlogTaskStatusApiResponse> CheckStatusAsync(string taskId, string epId)
        {
            if (string.IsNullOrWhiteSpace(taskId))
                throw new Exception("請提供任務 ID (task_id)");

            using var client = new HttpClient();
            var response = await client.GetAsync($"{BaseUrl}/api/check_status/{taskId}");

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"查詢任務狀態失敗，狀態碼: {response.StatusCode}");
            }

            var responseString = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<VlogTaskStatusApiResponse>(responseString,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.status == "ready")
            {
                var existing = await _dao.GetByVlogIdAsync(taskId);
                if (existing == null)
                {
                    await _dao.CreateCompletedRecordAsync(epId, taskId, storyId: null, videoUrl: result.download_url, thumbnailUrl: null);
                }
            }

            return result;
        }

        #endregion
    }
}