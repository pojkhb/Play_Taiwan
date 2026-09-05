// 檔案路徑：System\Services\PostcardCatalogService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using backend.dao;
using backend.Models;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>明信片主檔 (md_postcard) 服務層。</summary>
    public class PostcardCatalogService
    {
        private readonly PostcardCatalogDao _dao;
        private readonly IConfiguration _configuration;

        public PostcardCatalogService(PostcardCatalogDao dao, IConfiguration configuration)
        {
            _dao = dao;
            _configuration = configuration;
        }

        #region 基本 CRUD 操作
        public async Task<List<PostcardCatalogResponse>> GetAllAsync(string category = null)
        {
            var entities = await _dao.GetAllAsync(category);
            return entities.Select(ToResponse).ToList();
        }

        public async Task<PostcardCatalogResponse> GetByIdAsync(string id)
        {
            var entity = await _dao.GetByIdAsync(id);
            return entity == null ? null : ToResponse(entity);
        }

        public async Task<List<PostcardCatalogResponse>> GetByStoryIdAsync(string storyId)
        {
            var entities = await _dao.GetByStoryIdAsync(storyId);
            return entities.Select(ToResponse).ToList();
        }

        public async Task CreateAsync(PostcardCatalogRequest request)
        {
            await _dao.CreateAsync(new Models.PostcardCatalog
            {
                PostcardId = request.PostcardId,
                StoryId = request.StoryId,
                PostcardName = request.PostcardName,
                Summary = request.Summary,
                ImageUrl = request.ImageUrl,
                IsNightEditionDefault = request.IsNightEditionDefault,
                Category = request.Category,
                SortOrder = request.SortOrder,
                IsActive = request.IsActive
            });
        }

        public async Task<bool> UpdateAsync(string id, PostcardCatalogRequest request)
        {
            return await _dao.UpdateAsync(new Models.PostcardCatalog
            {
                PostcardId = id,
                StoryId = request.StoryId,
                PostcardName = request.PostcardName,
                Summary = request.Summary,
                ImageUrl = request.ImageUrl,
                IsNightEditionDefault = request.IsNightEditionDefault,
                Category = request.Category,
                SortOrder = request.SortOrder,
                IsActive = request.IsActive
            });
        }

        public async Task<bool> DeleteAsync(string id)
        {
            return await _dao.DeleteAsync(id);
        }

        private static PostcardCatalogResponse ToResponse(Models.PostcardCatalog e) => new PostcardCatalogResponse
        {
            PostcardId = e.PostcardId,
            StoryId = e.StoryId,
            PostcardName = e.PostcardName,
            Summary = e.Summary,
            ImageUrl = e.ImageUrl,
            IsNightEditionDefault = e.IsNightEditionDefault,
            Category = e.Category,
            SortOrder = e.SortOrder,
            IsActive = e.IsActive,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt
        };
        #endregion

        #region AI 生成明信片
        /// <summary>
        /// 1. 呼叫外部 API 生成明信片
        /// 2. 將回傳的下載網址，轉化為 Base64 圖片編碼
        /// 3. 將 Base64 存入 MySQL，讓前端後續可直接抓取代碼顯示，免除下載困擾
        /// </summary>
        public async Task<Models.PostcardCatalog> GenerateAiPostcardAsync(AiPostcardGenerateRequest request, string epId)
        {
            using var client = new HttpClient();
            using var content = new MultipartFormDataContent();

            // 1. 準備上傳的圖片與參數
            if (request.user_image != null)
            {
                var streamContent = new StreamContent(request.user_image.OpenReadStream());
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(request.user_image.ContentType);
                content.Add(streamContent, "user_image", request.user_image.FileName);
            }
            
            content.Add(new StringContent(request.spot_name ?? ""), "spot_name");
            content.Add(new StringContent(request.user_prompt ?? ""), "user_prompt");

            // 2. 發送 POST 請求至外部 AI API
            var apiUrl = "https://vlog.angelalala.com/api/postcard/create_ai";
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

            // 🌟🌟 關鍵修改：立刻從網址下載圖片，並打包成 Base64 底層編碼 🌟🌟
            var imageBytes = await client.GetByteArrayAsync(aiResult.DownloadUrl);
            string base64ImageCode = $"data:image/png;base64,{Convert.ToBase64String(imageBytes)}";

            string unifiedPostcardId = "ai_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            // 3. 寫入自己的資料庫
            var newEntity = new Models.PostcardCatalog
            {
                PostcardId = unifiedPostcardId, 
                StoryId = request.story_id ?? "Custom_AI", 
                PostcardName = $"{request.spot_name} 專屬明信片",
                Summary = aiResult.PostcardIntroduction,
                
                // ★ 這裡存進去的是 Base64 代碼，而非外部網址
                ImageUrl = base64ImageCode, 
                
                IsNightEditionDefault = request.is_night_edition,
                Category = "AI Generate",
                SortOrder = 1,
                IsActive = true
            };

            await _dao.CreateAsync(newEntity);

            // 4. 將生成的明信片綁定給該名探員
            await _dao.BindPostcardToUserAsync(epId, newEntity.PostcardId);

            return newEntity;
        }
        #endregion

        #region 🌟 取出該劇本最新的一張實體圖片位元組 (供 Controller 顯示用)
        /// <summary>
        /// 透過 story_id 撈出該劇本中「最新建立」的一張明信片，並將其 Base64 解析為實體位元組回傳。
        /// </summary>
        public async Task<byte[]> GetImageBytesByStoryAsync(string storyId)
        {
            // 抓出這個劇本下的「所有」明信片
            var postcards = await _dao.GetByStoryIdAsync(storyId);
            if (postcards == null || postcards.Count == 0) return null;

            // 💡 關鍵邏輯：因為同一個 story_id 可能被反覆生成多次明信片，在此我們抓取「最新建立」的那張
            var latestPostcard = postcards.OrderByDescending(p => p.CreatedAt).FirstOrDefault();

            if (string.IsNullOrEmpty(latestPostcard.ImageUrl)) return null;

            // 判斷資料庫存的是否為 Base64 編碼
            if (latestPostcard.ImageUrl.StartsWith("data:image"))
            {
                // 把 "data:image/png;base64," 之後的純代碼切出來解碼成位元組
                var base64Data = latestPostcard.ImageUrl.Substring(latestPostcard.ImageUrl.IndexOf(",") + 1);
                return Convert.FromBase64String(base64Data);
            }
            else
            {
                // 為了相容以前舊資料庫存的外部網址
                using var client = new HttpClient();
                return await client.GetByteArrayAsync(latestPostcard.ImageUrl);
            }
        }
        #endregion

        #region ibon 列印 (改為透過 Story_Id)
        /// <summary>
        /// 透過 story_id 找到最新的明信片，解析 Base64 並轉換為圖片發送給 ibon 微服務。
        /// </summary>
        public async Task<PostcardPrintResponse> PrintToIbonByStoryAsync(string storyId)
        {
            if (string.IsNullOrEmpty(storyId)) 
                throw new Exception("請提供劇本 ID");

            // 直接呼叫我們寫好的取圖邏輯，保證拿到最新的一張
            byte[] imageBytes = await GetImageBytesByStoryAsync(storyId);
            
            if (imageBytes == null) 
                throw new Exception("查無此劇本的明信片圖片內容，無法列印");

            using var httpClient = new HttpClient();
            using var form = new MultipartFormDataContent();
            
            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
            form.Add(fileContent, "file", "test_postcard.png");
            
            var ibonApiUrl = _configuration["IbonPrinterSettings:ApiUrl"] ?? "https://python-api.kajdslfjads.uk/upload";
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
                pdf_url = "Base64 Image Data", // 因底層已轉換為 Base64，不再回傳實體網址
                deadline = deadline,            
                qrcode_base64 = qrCodeBase64    
            };
        }
        #endregion
    }
}