// 檔案路徑：System\Services\Postcard\PostcardCatalogService.cs
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
    /// <summary>明信片 (資料表 postcard) 服務層。</summary>
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
            return await _dao.DeleteAsync(id, auId);
        }


        private static PostcardCatalogResponse ToResponse(Models.PostcardCatalog e) => new PostcardCatalogResponse
        {
            PostcardId = e.p_id,
            StoryId = e.s_id,
            NodeId = e.sn_id,
            PostcardName = e.p_name,
            Summary = e.p_summary,
            ImageUrl = e.p_imag_url,
            IsNightEdition = e.is_night == 1,
            CreatedAt = e.created_at,
            UpdatedAt = e.updated_at
        };
        #endregion


        #region AI 生成明信片
        /// <summary>
        /// 1. 呼叫外部 API 生成明信片
        /// 2. 直接把外部服務回傳的圖片網址 (download_url) 存入 MySQL 的 image_url 欄位
        ///    （不再下載圖片位元組、不再轉 Base64），前端後續可直接使用該網址顯示圖片
        /// </summary>
        public async Task<Models.PostcardCatalog> GenerateAiPostcardAsync(AiPostcardGenerateRequest request, int auId)
        {
            using var client = new HttpClient();
            using var content = new MultipartFormDataContent();


            if (request.user_image != null)
            {
                var streamContent = new StreamContent(request.user_image.OpenReadStream());
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(request.user_image.ContentType);
                content.Add(streamContent, "user_image", request.user_image.FileName);
            }


            content.Add(new StringContent(request.spot_name ?? ""), "spot_name");
            content.Add(new StringContent(request.user_prompt ?? ""), "user_prompt");


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


            var newEntity = new Models.PostcardCatalog
            {
                au_id = auId,
                s_id = request.story_id,
                sn_id = request.node_id,
                p_name = $"{request.spot_name} 專屬明信片",
                p_summary = aiResult.PostcardIntroduction,
                p_imag_url = aiResult.DownloadUrl,
                is_night = request.is_night_edition ? 1 : 2
            };

            // postcard 已含 au_id，寫入即完成歸戶，不需要再綁定一次。
            newEntity.p_id = await _dao.CreateAsync(newEntity);

            return newEntity;
        }
        #endregion


        #region 🌟 取出指定明信片的圖片網址 / 實體位元組 (供 Controller 顯示與 ibon 列印共用)
        /// <summary>
        /// 依 postcard_id 撈出該張明信片的圖片網址，供 Controller 直接轉址 (Redirect) 使用。
        /// </summary>
        public async Task<string> GetImageUrlByPostcardIdAsync(int postcardId)
        {
            var postcard = await _dao.GetByIdAsync(postcardId);
            return postcard?.p_imag_url;
        }


        /// <summary>
        /// 依 postcard_id 撈出該張明信片的圖片網址，並下載為實體位元組回傳。
        /// 僅供需要真正檔案內容的場景使用（例如 ibon 列印上傳）。
        /// </summary>
        public async Task<byte[]> GetImageBytesByPostcardIdAsync(int postcardId)
        {
            var postcard = await _dao.GetByIdAsync(postcardId);
            if (postcard == null || string.IsNullOrEmpty(postcard.p_imag_url)) return null;

            using var client = new HttpClient();
            return await client.GetByteArrayAsync(postcard.p_imag_url);
        }
        #endregion


        #region ibon 列印 (透過 Postcard_Id)
        /// <summary>
        /// 透過 postcard_id 找到指定的明信片，下載圖片網址內容並轉換為圖片發送給 ibon 微服務。
        /// </summary>
        public async Task<PostcardPrintResponse> PrintToIbonByPostcardIdAsync(int postcardId)
        {
            // 統一呼叫共用的取圖方法，不再自己重複下載邏輯
            byte[] imageBytes = await GetImageBytesByPostcardIdAsync(postcardId);


            if (imageBytes == null)
                throw new Exception($"查無此明信片 (ID: {postcardId}) 的圖片內容，無法列印");


            using var httpClient = new HttpClient();
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