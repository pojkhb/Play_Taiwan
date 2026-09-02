using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration; // ★ 引入設定檔命名空間
using backend.dao;
using backend.Models;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>明信片主檔 (md_postcard) 服務層。</summary>
    public class PostcardCatalogService
    {
        private readonly PostcardCatalogDao _dao;
        private readonly IConfiguration _configuration; // ★ 宣告設定檔變數

        // ★ 透過建構子注入 IConfiguration
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
        /// 1. 呼叫外部 API (vlog.angelalala) 生成明信片
        /// 2. 統一產生整齊的 PostcardId 格式
        /// 3. 將回傳結果存入 MySQL
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

            // 統一 ID 格式：強制轉換為 "ai_" + 8碼乾淨亂數，保持資料庫整潔
            string unifiedPostcardId = "ai_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            // 3. 寫入自己的資料庫
            var newEntity = new Models.PostcardCatalog
            {
                PostcardId = unifiedPostcardId, 
                StoryId = request.story_id ?? "Custom_AI", 
                PostcardName = $"{request.spot_name} 專屬明信片",
                Summary = aiResult.PostcardIntroduction,
                ImageUrl = aiResult.DownloadUrl, 
                IsNightEditionDefault = false,
                Category = "AI Generate",
                SortOrder = 1,
                IsActive = true
            };

            await _dao.CreateAsync(newEntity);

            // 4. 將生成的明信片綁定給該名探員 (寫入 ep_postcard)
            await _dao.BindPostcardToUserAsync(epId, newEntity.PostcardId);

            return newEntity;
        }
        #endregion

        #region ibon 列印
        /// <summary>
        /// 處理 ibon 列印請求 (實際呼叫 Python 微服務 API)
        /// </summary>
        public async Task<PostcardPrintResponse> PrintToIbonAsync(PostcardPrintRequest request)
        {
            string targetImageUrl = "";

            if (request.postcard_id.StartsWith("http://") || request.postcard_id.StartsWith("https://"))
            {
                targetImageUrl = request.postcard_id;
            }
            else
            {
                var postcard = await _dao.GetByIdAsync(request.postcard_id);
                if (postcard == null) throw new Exception("查無此明信片");
                if (string.IsNullOrEmpty(postcard.ImageUrl)) throw new Exception("此明信片無圖片網址，無法列印");
                
                targetImageUrl = postcard.ImageUrl;
            }

            using var httpClient = new HttpClient();

            // 1. 從網址下載圖片
            var imageBytes = await httpClient.GetByteArrayAsync(targetImageUrl);

            // 2. 將圖片傳送至微服務 API
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(imageBytes);
            
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
            form.Add(fileContent, "file", "test_postcard.png");
            
            // ★ 動態讀取 appsettings.json 中的 ibon 微服務網址 (預設回退到 10.10.0.174)
            var ibonApiUrl = _configuration["IbonPrinterSettings:ApiUrl"] ?? "https://python-api.kajdslfjads.uk/upload";
            var response = await httpClient.PostAsync(ibonApiUrl, form);

            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync();
                throw new Exception($"上傳至 ibon 失敗，狀態碼: {response.StatusCode}，錯誤內容: {errorMsg}");
            }

            // 3. 解析 Python API 回傳的 JSON (pincode, deadline, qrcode_base64)
            var responseString = await response.Content.ReadAsStringAsync();
            
            string pinCode = "";
            string deadline = "";
            string qrCodeBase64 = "";
            
            using (var jsonDoc = JsonDocument.Parse(responseString))
            {
                var root = jsonDoc.RootElement;
                
                if (root.TryGetProperty("pincode", out var pin))
                    pinCode = pin.GetString();

                if (root.TryGetProperty("deadline", out var dl))
                    deadline = dl.GetString();

                if (root.TryGetProperty("qrcode_base64", out var qr))
                    qrCodeBase64 = qr.GetString();
            }

            return new PostcardPrintResponse
            {
                ibon_pickup_code = pinCode, 
                pdf_url = targetImageUrl,       
                deadline = deadline,            
                qrcode_base64 = qrCodeBase64    
            };
        }
        #endregion
    }
}