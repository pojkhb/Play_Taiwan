// 檔案路徑：System\Controllers\Postcard\PostcardCatalogController.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.ViewModels;
using backend.Models;
using backend.utils;


namespace backend.Controllers
{
    /// <summary>
    /// 明信片相關 API。
    /// 對應頁面：收藏館。新資料庫已把主檔與擁有紀錄合併成 postcard 一張表，
    /// 因此這裡回傳的就是「目前登入者自己的明信片」。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PostcardCatalogController : ControllerBase
    {
        private readonly ILogger<PostcardCatalogController> _logger;
        private readonly PostcardCatalogService _service;


        public PostcardCatalogController(ILogger<PostcardCatalogController> logger, PostcardCatalogService service)
        {
            _logger = logger;
            _service = service;
        }


        #region 擴充功能 1：生成 AI 明信片


        /// <summary>
        /// 呼叫外部服務生成 AI 明信片並存入資料庫。
        /// </summary>
        /// <remarks>
        /// 接收前端上傳的照片與提示詞，轉發至外部 AI 服務生成明信片，
        /// 直接把外部服務回傳的圖片「網址」存入 `md_postcard.image_url`（不再下載轉 Base64），
        /// 同時將此明信片綁定給發起請求的探員。**必須帶 Token**（後端會從 Token 解析 ep_id）。
        /// 
        /// **⚠️ 請求格式注意**：必須使用 `multipart/form-data`，不能使用一般的 JSON。
        /// 
        /// **Request 範例**（multipart/form-data 欄位）：
        /// ```
        /// user_image: (檔案) taipei101.jpg
        /// spot_name: "台北101"
        /// user_prompt: "復古水墨風"
        /// story_id: "story_001"
        /// ```
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "AI 明信片生成且儲存成功",
        ///   "Result": {
        ///     "postcardId": "ai_d1863bc6",
        ///     "storyId": "story_001",
        ///     "postcardName": "台北101 專屬明信片",
        ///     "summary": "晨曦的秘密花園，夕陽的城市堡壘...",
        ///     "imageUrl": "https://external-ai-service.com/generated/xxx.png",
        ///     "category": "AI Generate",
        ///     "isNightEditionDefault": false,
        ///     "sortOrder": 1,
        ///     "isActive": true
        ///   }
        /// }
        /// ```
        /// *(註：`imageUrl` 現在是外部服務的圖片連結，前端可直接使用，
        /// 也可以改用 `by-story/{story_id}` 取得清單後，用 `{postcard_id}/image` 顯示圖片，
        /// 該端點會自動轉址至真正的圖片網址)*
        /// </remarks>
        [HttpPost]
        [Route("GenerateAi")]
        [Authorize]
        [ProducesResponseType(typeof(ResultViewModel<PostcardCatalog>), 200)]
        public async Task<IActionResult> GenerateAi([FromForm] AiPostcardGenerateRequest request)
        {
            if (request.user_image == null || request.user_image.Length == 0)
            {
                return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供圖片檔案" });
            }


            try
            {
                var resultEntity = await _service.GenerateAiPostcardAsync(request, User.GetAuId());
                return Ok(new ResultViewModel<PostcardCatalog>
                {
                    isSuccess = true,
                    message = "AI 明信片生成且儲存成功",
                    Result = resultEntity
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "生成 AI 明信片時發生錯誤");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion


        #region 擴充功能 2：請求 ibon 列印


        /// <summary>
        /// 透過 postcard_id 送出明信片至 ibon 列印，取得取件碼。
        /// </summary>
        /// <remarks>
        /// 傳入明信片 ID (postcard_id)，後端會撈取該張明信片的圖片網址並下載實際位元組，
        /// 透過本機背景執行的 Python 微服務 (`127.0.0.1:9000`) 上傳至 ibon，回傳真實的取件碼與 QR Code。
        /// 
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "postcard_id": "ai_d1863bc6"
        /// }
        /// ```
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "已成功取得 ibon 取件碼",
        ///   "Result": {
        ///     "ibon_pickup_code": "1234567890",
        ///     "pdf_url": "Base64 Image Data",
        ///     "deadline": "2024-12-31 23:59:59",
        ///     "qrcode_base64": "iVBORw0KGgoAAAANSUhEUg..."
        ///   }
        /// }
        /// ```
        /// </remarks>
        [HttpPost]
        [Route("Print")]
        [ProducesResponseType(typeof(ResultViewModel<PostcardPrintResponse>), 200)]
        public async Task<IActionResult> PrintIbon([FromBody] PrintPostcardRequest request)
        {
            if (request.postcard_id <= 0)
            {
                return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供明信片 ID (postcard_id)" });
            }


            try
            {
                var printResponse = await _service.PrintToIbonByPostcardIdAsync(request.postcard_id);
                return Ok(new ResultViewModel<PostcardPrintResponse>
                {
                    isSuccess = true,
                    message = "已成功取得 ibon 取件碼",
                    Result = printResponse
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ibon 列印請求發生錯誤");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = ex.Message, Result = null });
            }
        }
        #endregion


        #region 擴充功能 3：紀錄社群分享


        /// <summary>
        /// 紀錄使用者已將該劇本的明信片分享至社群平台。
        /// </summary>
        /// <remarks>
        /// **技術說明**：實際將圖片貼上 IG 或 FB 的動作，必須由前端呼叫原生的 Share Intent 完成。
        /// 此 API 僅提供前端在「完成分享動作後」呼叫，供後端紀錄分享次數或發放獎勵。
        /// 
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "story_id": "story_001",
        ///   "platform": "IG"
        /// }
        /// ```
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "已紀錄分享至 IG",
        ///   "Result": "Success"
        /// }
        /// ```
        /// </remarks>
        /// <response code="200">分享紀錄成功；Result 固定為 "Success"</response>
        [HttpPost]
        [Route("Share")]
        [ProducesResponseType(typeof(ResultViewModel<string>), 200)]
        public IActionResult RecordShare([FromBody] StoryShareRequest request)
        {
            if (request.story_id <= 0)
            {
                return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供劇本 ID (story_id)" });
            }


            _logger.LogInformation($"探員將劇本 {request.story_id} 的明信片分享至 {request.platform}");


            return Ok(new ResultViewModel<string>
            {
                isSuccess = true,
                message = $"已紀錄分享至 {request.platform}",
                Result = "Success"
            });
        }
        #endregion


        // =========================================================================
        // 以下為既有的 CRUD API
        // =========================================================================


        #region 取得所有明信片主檔


        /// <summary>
        /// 取得所有明信片主檔清單。
        /// </summary>
        /// <remarks>
        /// 依 `sort_order` 排序，回傳所有 `is_active = 1` 的明信片主檔。可用 `category` 篩選分類。
        /// 
        /// **Request 範例**：
        /// ```
        /// GET /api/PostcardCatalog?category=AI Generate
        /// ```
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": [
        ///     {
        ///       "postcardId": "ai_d1863bc6",
        ///       "storyId": "story_001",
        ///       "postcardName": "台北101 專屬明信片",
        ///       "summary": "晨曦的秘密花園...",
        ///       "imageUrl": "https://external-ai-service.com/generated/xxx.png",
        ///       "isNightEditionDefault": false,
        ///       "category": "AI Generate",
        ///       "sortOrder": 1,
        ///       "isActive": true,
        ///       "createdAt": "2024-12-01T10:00:00",
        ///       "updatedAt": "2024-12-01T10:00:00"
        ///     }
        ///   ]
        /// }
        /// ```
        /// </remarks>
        [Authorize]
        [HttpGet]
        [Route("")]
        [ProducesResponseType(typeof(ResultViewModel<List<PostcardCatalogResponse>>), 200)]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var result = await _service.GetAllAsync(User.GetAuId());
                return Ok(new ResultViewModel<List<PostcardCatalogResponse>> { isSuccess = true, message = "查詢成功", Result = result });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "取得明信片清單時發生錯誤");
                return StatusCode(500, new ResultViewModel<List<PostcardCatalogResponse>> { isSuccess = false, message = "系統發生未預期錯誤", Result = null });
            }
        }
        #endregion


        #region 依識別碼取得單一明信片主檔


        /// <summary>
        /// 依明信片識別碼 (postcard_id) 取得單一明信片主檔資料。
        /// </summary>
        /// <remarks>
        /// **Request 範例**：
        /// ```
        /// GET /api/PostcardCatalog/ai_d1863bc6
        /// ```
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": {
        ///     "postcardId": "ai_d1863bc6",
        ///     "storyId": "story_001",
        ///     "postcardName": "台北101 專屬明信片",
        ///     "summary": "晨曦的秘密花園...",
        ///     "imageUrl": "https://external-ai-service.com/generated/xxx.png",
        ///     "category": "AI Generate",
        ///     "isActive": true
        ///   }
        /// }
        /// ```
        /// 
        /// 查無資料時回傳 404：
        /// ```json
        /// {
        ///   "isSuccess": false,
        ///   "message": "查無此資料",
        ///   "Result": null
        /// }
        /// ```
        /// </remarks>
        [HttpGet]
        [Route("{id:int}")]
        [ProducesResponseType(typeof(ResultViewModel<PostcardCatalogResponse>), 200)]
        public async Task<IActionResult> GetById(int id)
        {
            try
            {
                var result = await _service.GetByIdAsync(id);
                if (result == null) return NotFound(new ResultViewModel<PostcardCatalogResponse> { isSuccess = false, message = "查無此資料", Result = null });
                return Ok(new ResultViewModel<PostcardCatalogResponse> { isSuccess = true, message = "查詢成功", Result = result });
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"取得明信片 {id} 時發生錯誤");
                return StatusCode(500, new ResultViewModel<PostcardCatalogResponse> { isSuccess = false, message = "系統發生未預期錯誤", Result = null });
            }
        }
        #endregion


        #region 依劇本取得明信片主檔清單


        /// <summary>
        /// 取得指定劇本 (story_id) 所擁有的「所有」明信片主檔。
        /// </summary>
        /// <remarks>
        /// 同一個 story_id 可能生成過多次明信片，此 API 回傳全部清單（依 sort_order 排序）。
        /// 要顯示某一張的圖片時，從回傳結果取出 `postcardId`，改打 `{postcardId}/image` 顯示。
        /// 
        /// **Request 範例**：
        /// ```
        /// GET /api/PostcardCatalog/by-story/story_001
        /// ```
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": [
        ///     {
        ///       "postcardId": "ai_d1863bc6",
        ///       "storyId": "story_001",
        ///       "postcardName": "台北101 專屬明信片",
        ///       "category": "AI Generate",
        ///       "createdAt": "2024-12-01T10:00:00"
        ///     }
        ///   ]
        /// }
        /// ```
        /// </remarks>
        [Authorize]
        [HttpGet]
        [Route("by-story/{storyId:int}")]
        [ProducesResponseType(typeof(ResultViewModel<List<PostcardCatalogResponse>>), 200)]
        public async Task<IActionResult> GetByStoryId(int storyId)
        {
            try
            {
                var result = await _service.GetByStoryIdAsync(storyId, User.GetAuId());
                return Ok(new ResultViewModel<List<PostcardCatalogResponse>> { isSuccess = true, message = "查詢成功", Result = result });
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"取得劇本 {storyId} 明信片清單時發生錯誤");
                return StatusCode(500, new ResultViewModel<List<PostcardCatalogResponse>> { isSuccess = false, message = "系統發生未預期錯誤", Result = null });
            }
        }
        #endregion


        #region 刪除明信片主檔


        /// <summary>
        /// 刪除明信片主檔。
        /// </summary>
        /// <remarks>
        /// 依 postcard_id 從 `postcard` 刪除該筆資料，只能刪自己的明信片。此操作為實體刪除，無法復原。
        /// 
        /// **Request 範例**：
        /// ```
        /// POST /api/PostcardCatalog/ai_d1863bc6/Delete
        /// ```
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "刪除成功",
        ///   "Result": true
        /// }
        /// ```
        /// </remarks>
        /// <response code="200">刪除成功時 Result = true</response>
        [Authorize]
        [HttpPost]
        [Route("{id:int}/Delete")]
        [ProducesResponseType(typeof(ResultViewModel<bool>), 200)]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var success = await _service.DeleteAsync(id, User.GetAuId());
                if (!success) return NotFound(new ResultViewModel<bool> { isSuccess = false, message = "查無此資料", Result = false });
                return Ok(new ResultViewModel<bool> { isSuccess = true, message = "刪除成功", Result = true });
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"刪除明信片 {id} 時發生錯誤");
                return StatusCode(500, new ResultViewModel<bool> { isSuccess = false, message = "系統發生未預期錯誤", Result = false });
            }
        }
        #endregion


        #region 依明信片 ID 顯示圖片


        /// <summary>
        /// 【前端不用接：直接當圖片網址】依 postcard_id 取得指定明信片的圖片，轉址 (302 Redirect) 至實際圖片網址。
        /// </summary>
        /// <remarks>
        /// 資料庫現在直接儲存圖片的外部連結（不再是 Base64），此 API 會查出該連結後直接轉址，
        /// 瀏覽器/前端會自動跟隨轉址載入真正的圖片，後端不再代理下載與回傳位元組。
        /// 
        /// 前端一樣可以把此 API 當作 HTML 圖片網址使用：
        /// 
        /// ```html
        /// <img src="https://你的後端網址/api/PostcardCatalog/ai_d1863bc6/image" />
        /// ```
        /// 
        /// **Request 範例**：
        /// ```
        /// GET /api/PostcardCatalog/ai_d1863bc6/image
        /// ```
        /// 
        /// **Response**：302 Redirect 至真實圖片網址。
        /// 查無資料時回傳 404 純文字：`找不到該明信片的圖片`
        /// </remarks>
        /// <response code="200">302 轉址到實際圖片網址（不是 JSON）</response>
        [HttpGet]
        [Route("{id:int}/image")]
        [AllowAnonymous]
        public async Task<IActionResult> GetImageById(int id)
        {
            try
            {
                var imageUrl = await _service.GetImageUrlByPostcardIdAsync(id);
                if (string.IsNullOrEmpty(imageUrl)) return NotFound("找不到該明信片的圖片");
                return Redirect(imageUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"讀取明信片 {id} 圖片時發生錯誤");
                return StatusCode(500, "無法載入圖片");
            }
        }
        #endregion
    }
}