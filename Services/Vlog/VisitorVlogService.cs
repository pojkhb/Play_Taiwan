// 檔案路徑：System\Services\Vlog\VisitorVlogService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using backend.dao;
using backend.ViewModels;

namespace backend.Services
{
    public class VisitorVlogService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly VisitorVlogDao _dao;

        public VisitorVlogService(IHttpClientFactory httpClientFactory, VisitorVlogDao dao)
        {
            _httpClientFactory = httpClientFactory;
            _dao = dao;
        }

        #region 1. 生成旁白草稿預覽（自動組遊玩時長 + 景點清單）

        public async Task<VisitorVlogPreviewApiResponse> GetPreviewAsync(int auId, VisitorVlogPreviewRequest request)
        {
            var (playTime, spots) = await _dao.GetPlayResultAsync(auId, request.story_id);

            if (spots.Count == 0)
                throw new Exception($"找不到 story_id={request.story_id} 的遊玩紀錄，請確認任務已完成");

            var payload = new
            {
                spot_history = spots,
                player_play_time = playTime,
                game_tasks_completed = "完成解謎與尋寶任務"
            };

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(3);

            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://vlog.angelalala.com/api/visitor/vlog/preview", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                string errContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"外部 Vlog 預覽服務回應錯誤 (Status: {response.StatusCode}): {errContent}");
            }

            string responseString = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<VisitorVlogPreviewApiResponse>(responseString, options);
        }

        #endregion

        #region 2. 送出正式合成任務（依 story_id 自動抓素材、打包成 zip 再送出）

        public async Task<VlogCreateFinalApiResponse> CreateFinalVlogAsync(int auId, VisitorVlogCreateFinalRequest request)
        {
            var mediaUrls = await _dao.GetTaskMediaUrlsAsync(auId, request.story_id);
            if (mediaUrls.Count == 0)
                throw new Exception($"找不到 story_id={request.story_id} 對應的素材，請確認 record_media 是否已建立資料");

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(3);

            byte[] zipBytes = await BuildZipFromUrlsAsync(client, mediaUrls);

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(request.final_script), "final_script");

            if (!string.IsNullOrWhiteSpace(request.spot_meta_json))
            {
                form.Add(new StringContent(request.spot_meta_json), "spot_meta_json");
            }

            var zipContent = new ByteArrayContent(zipBytes);
            zipContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
            form.Add(zipContent, "image_zip", "images.zip");

            var response = await client.PostAsync("https://vlog.angelalala.com/api/visitor/vlog/create_final", form);

            if (!response.IsSuccessStatusCode)
            {
                string errContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"外部 Vlog 合成服務回應錯誤 (Status: {response.StatusCode}): {errContent}");
            }

            string responseString = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<VlogCreateFinalApiResponse>(responseString, options);
        }

        private static async Task<byte[]> BuildZipFromUrlsAsync(HttpClient client, List<string> mediaUrls)
        {
            using var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
            {
                int index = 0;
                foreach (var url in mediaUrls)
                {
                    index++;
                    byte[] bytes;
                    try
                    {
                        bytes = await client.GetByteArrayAsync(url);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"下載素材失敗: {url}，原因: {ex.Message}");
                    }

                    string ext = Path.GetExtension(url);
                    if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
                    string entryName = $"image_{index}{ext}";

                    var entry = archive.CreateEntry(entryName);
                    using var entryStream = entry.Open();
                    await entryStream.WriteAsync(bytes, 0, bytes.Length);
                }
            }
            return zipStream.ToArray();
        }

        #endregion

        #region 3. 輪詢任務狀態，完成時寫入 au_vlog

        public async Task<VlogTaskStatusApiResponse> CheckStatusAsync(string taskId, int auId, int storyId)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var response = await client.GetAsync($"https://vlog.angelalala.com/api/check_status/{taskId}");

            if (!response.IsSuccessStatusCode)
            {
                string errContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"查詢任務狀態失敗 (Status: {response.StatusCode}): {errContent}");
            }

            string responseString = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<VlogTaskStatusApiResponse>(responseString, options);

            if (result != null && !string.IsNullOrWhiteSpace(result.download_url))
            {
                await _dao.SaveVlogAsync(auId, storyId, result.download_url);
            }

            return result;
        }

        #endregion
    }
}