// 檔案路徑：System\Services\Postcard\PostcardCatalogService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using backend.dao;
using backend.utils;
using backend.Models;
using backend.ViewModels;


namespace backend.Services
{
    /// <summary>
    /// 明信片 (資料表 postcard) 服務層。
    /// 每張明信片給前端兩個網址，都由後端自己回傳圖片內容：
    /// - 顯示網址（ImagePath，GET api/PostcardCatalog/{id}/image）：打開就是圖片本身
    /// - 下載網址（DownloadPath，GET api/PostcardCatalog/{id}/download）：打開會存成檔案，檔名是明信片名稱
    /// 資料庫 p_imag_url 存的是 AI 服務給的檔案連結，只有後端自己拿圖片內容時用，不回傳給前端。
    /// </summary>
    public class PostcardCatalogService
    {
        private readonly PostcardCatalogDao _dao;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PostcardCatalogService> _logger;
        private readonly string _imageRoot;


        public PostcardCatalogService(PostcardCatalogDao dao, IConfiguration configuration, IHttpClientFactory httpClientFactory,
                                      IWebHostEnvironment environment, ILogger<PostcardCatalogService> logger)
        {
            _dao = dao;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            // 明信片圖片存放的資料夾，預設在 wwwroot 底下（測試時改到暫存資料夾）
            _imageRoot = string.IsNullOrWhiteSpace(configuration["Postcard:ImageRoot"])
                ? Path.Combine(environment.WebRootPath, "images", "postcards")
                : configuration["Postcard:ImageRoot"];
        }


        /// <summary>
        /// 明信片圖片的顯示網址（站內路徑，Controller 會組成完整網址）。
        /// 所有會回傳明信片的 API（收藏館、劇本回顧、首頁卡片、生成結果）都給這一個
        /// </summary>
        public static string ImagePath(int postcardId) => $"/api/PostcardCatalog/{postcardId}/image";

        /// <summary>明信片圖片的下載網址（站內路徑）：內容跟顯示網址一樣，只是打開會存成檔案</summary>
        public static string DownloadPath(int postcardId) => $"/api/PostcardCatalog/{postcardId}/download";


        #region 基本 CRUD 操作
        public async Task<List<PostcardCatalogResponse>> GetAllAsync(int auId)
        {
            var entities = await _dao.GetAllAsync(auId);
            return entities.Select(ToResponse).ToList();
        }


        public async Task<PostcardCatalogResponse> GetByIdAsync(int id)
        {
            var entity = await _dao.GetByIdAsync(id);
            return entity == null ? null : ToResponse(entity);
        }


        public async Task<List<PostcardCatalogResponse>> GetByStoryIdAsync(int storyId, int auId)
        {
            var entities = await _dao.GetByStoryIdAsync(storyId, auId);
            return entities.Select(ToResponse).ToList();
        }


        public async Task<bool> DeleteAsync(int id, int auId)
        {
            if (!await _dao.DeleteAsync(id, auId)) return false;

            try
            {
                string stored = StoredPath(id);   // 存在後端的圖片一起刪掉
                if (File.Exists(stored)) File.Delete(stored);
            }
            catch (IOException e)
            {
                _logger.LogWarning(e, "明信片 {PostcardId} 已刪除，但存在後端的圖片刪不掉", id);
            }

            return true;
        }


        private static PostcardCatalogResponse ToResponse(Models.PostcardCatalog e) => new PostcardCatalogResponse
        {
            PostcardId = e.p_id,
            StoryId = e.s_id,
            NodeId = e.sn_id,
            PostcardName = e.p_name,
            Summary = e.p_summary,
            ImageUrl = ImagePath(e.p_id),
            DownloadUrl = DownloadPath(e.p_id),
            IsNightEdition = e.is_night == 1,
            CreatedAt = e.created_at,
            UpdatedAt = e.updated_at
        };
        #endregion


        #region AI 生成明信片
        /// <summary>
        /// 1. 呼叫外部 API 生成明信片
        /// 2. 外部服務回傳的檔案連結 (download_url) 存入 MySQL 的 p_imag_url，並馬上把圖片抓回後端存起來
        /// 3. 回傳的 p_imag_url 換成顯示網址（ImagePath）、download_url 是下載網址（DownloadPath）
        /// </summary>
        public async Task<Models.PostcardCatalog> GenerateAiPostcardAsync(AiPostcardGenerateRequest request, int auId)
        {
            var client = _httpClientFactory.CreateClient();
            using var content = new MultipartFormDataContent();


            if (request.user_image != null)
            {
                var streamContent = new StreamContent(request.user_image.OpenReadStream());
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(request.user_image.ContentType);
                content.Add(streamContent, "user_image", request.user_image.FileName);
            }


            content.Add(new StringContent(request.spot_name ?? ""), "spot_name");
            content.Add(new StringContent(request.user_prompt ?? ""), "user_prompt");


            var apiUrl = $"{AiServiceConfig.BaseUrl}/api/postcard/create_ai";
            var response = await client.PostAsync(apiUrl, content);


            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"AI API 呼叫失敗，狀態碼: {response.StatusCode}");
            }


            var responseString = await response.Content.ReadAsStringAsync();
            var aiResult = JsonSerializer.Deserialize<AiPostcardApiResponse>(responseString);


            if (aiResult?.Status != "success" || string.IsNullOrEmpty(aiResult.DownloadUrl))
            {
                throw new Exception("外部 API 生成失敗或未回傳 download_url");
            }


            var newEntity = new Models.PostcardCatalog
            {
                au_id = auId,
                s_id = request.story_id,
                sn_id = request.node_id,
                p_name = $"{request.spot_name} 專屬明信片",
                p_summary = aiResult.PostcardIntroduction,
                p_imag_url = VlogAiGateway.ResolveUrl(aiResult.DownloadUrl),   // AI 給的可能是相對路徑，補成完整網址
                is_night = request.is_night_edition ? 1 : 0
            };

            // postcard 已含 au_id，寫入即完成歸戶，不需要再綁定一次。
            newEntity.p_id = await _dao.CreateAsync(newEntity);

            try
            {
                await GetImageAsync(newEntity.p_id);   // 先把圖片抓回後端，之後顯示不用再等 AI 服務
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "明信片 {PostcardId} 生成了，但圖片還沒抓回後端（第一次顯示時會再試）", newEntity.p_id);
            }

            newEntity.p_imag_url = ImagePath(newEntity.p_id);
            newEntity.download_url = DownloadPath(newEntity.p_id);
            return newEntity;
        }
        #endregion


        #region 明信片的圖片內容（顯示與 ibon 列印共用）

        /// <summary>
        /// 明信片的圖片內容。第一次向 AI 服務拿，之後存在後端（_imageRoot），
        /// 所以 AI 服務連不上或把檔案清掉了也照樣顯示。查無明信片或沒有圖片時回傳 null；拿不到或不是圖片時丟出錯誤
        /// </summary>
        public async Task<byte[]> GetImageAsync(int postcardId)
        {
            var postcard = await _dao.GetByIdAsync(postcardId);
            if (postcard == null || string.IsNullOrWhiteSpace(postcard.p_imag_url)) return null;

            string stored = StoredPath(postcardId);
            if (File.Exists(stored)) return await File.ReadAllBytesAsync(stored);

            var client = _httpClientFactory.CreateClient();
            byte[] bytes = await client.GetByteArrayAsync(VlogAiGateway.ResolveUrl(postcard.p_imag_url));
            if (ContentTypeOf(bytes) == null)
                throw new Exception($"明信片 (ID: {postcardId}) 的圖片連結回傳的不是圖片");

            try
            {
                // 先寫到暫存檔再改名，同時有人來讀也不會讀到寫到一半的檔案
                Directory.CreateDirectory(_imageRoot);
                string temp = $"{stored}.{Guid.NewGuid():N}.tmp";
                await File.WriteAllBytesAsync(temp, bytes);
                File.Move(temp, stored, overwrite: true);
            }
            catch (IOException e)
            {
                _logger.LogWarning(e, "明信片 {PostcardId} 的圖片存不進後端（下次顯示會再向 AI 服務拿）", postcardId);
            }

            return bytes;
        }

        private string StoredPath(int postcardId) => Path.Combine(_imageRoot, $"{postcardId}.img");

        /// <summary>下載用：圖片內容加上存檔的檔名（明信片名稱＋副檔名）。查無明信片或沒有圖片時回傳 null</summary>
        public async Task<(byte[] bytes, string fileName)?> GetDownloadAsync(int postcardId)
        {
            byte[] bytes = await GetImageAsync(postcardId);
            if (bytes == null) return null;

            var postcard = await _dao.GetByIdAsync(postcardId);
            return (bytes, DownloadFileName(postcard?.p_name, postcardId, ContentTypeOf(bytes)));
        }

        /// <summary>存檔的檔名：明信片名稱去掉不能當檔名的字元，沒有名稱時用 postcard-{id}；副檔名依圖片格式</summary>
        internal static string DownloadFileName(string postcardName, int postcardId, string contentType)
        {
            // 不看後端跑在哪個作業系統，Windows 與手機上都不能用的字元一律拿掉
            char[] invalid = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
            string name = new string((postcardName ?? "").Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim().TrimEnd('.');
            if (name == "") name = $"postcard-{postcardId}";

            string extension = contentType switch
            {
                "image/jpeg" => ".jpg",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                _ => ".png"
            };

            return name + extension;
        }

        /// <summary>依檔案開頭判斷圖片格式（PNG、JPEG、WebP、GIF）；不是圖片回傳 null</summary>
        internal static string ContentTypeOf(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4) return null;
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "image/png";
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38) return "image/gif";
            if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return "image/webp";
            return null;
        }

        #endregion


        #region ibon 列印 (透過 Postcard_Id)
        /// <summary>
        /// 透過 postcard_id 找到指定的明信片，把圖片內容發送給 ibon 微服務。
        /// </summary>
        public async Task<PostcardPrintResponse> PrintToIbonByPostcardIdAsync(int postcardId)
        {
            // 跟顯示用同一份圖片內容
            byte[] imageBytes = await GetImageAsync(postcardId);


            if (imageBytes == null)
                throw new Exception($"查無此明信片 (ID: {postcardId}) 的圖片內容，無法列印");


            var httpClient = _httpClientFactory.CreateClient();
            using var form = new MultipartFormDataContent();


            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
            form.Add(fileContent, "file", "test_postcard.png");


            var ibonApiUrl = _configuration["IbonPrinterSettings:ApiUrl"] ?? "http://127.0.0.1:9000/upload";
            var response = await httpClient.PostAsync(ibonApiUrl, form);


            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync();
                throw new Exception($"上傳至 ibon 失敗，狀態碼: {response.StatusCode}，錯誤內容: {errorMsg}");
            }


            var responseString = await response.Content.ReadAsStringAsync();


            string pinCode = "", deadline = "", qrCodeBase64 = "";
            using (var jsonDoc = JsonDocument.Parse(responseString))
            {
                var root = jsonDoc.RootElement;
                if (root.TryGetProperty("pincode", out var pin)) pinCode = pin.GetString();
                if (root.TryGetProperty("deadline", out var dl)) deadline = dl.GetString();
                if (root.TryGetProperty("qrcode_base64", out var qr)) qrCodeBase64 = qr.GetString();
            }


            return new PostcardPrintResponse
            {
                ibon_pickup_code = pinCode,
                pdf_url = "Base64 Image Data",
                deadline = deadline,
                qrcode_base64 = qrCodeBase64
            };
        }
        #endregion
    }
}