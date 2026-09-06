// 檔案路徑：System\Controllers\PostcardCatalogController.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.ViewModels;
using backend.Models;

namespace backend.Controllers
{
    /// <summary>
    /// 供前端透過 story_id 執行列印或分享的專屬請求模型
    /// </summary>
    public class StoryActionRequest
    {
        /// <summary>劇本的唯一識別碼</summary>
        public string story_id { get; set; }
        
    }
    public class PrintPostcardRequest
    {
        /// <summary>明信片的唯一識別碼 (例如: ai_f676ec15)</summary>
        public string postcard_id { get; set; }
    }
    // 給分享用的
    public class StoryShareRequest
    {
        public string story_id { get; set; }
        public string platform { get; set; }
    }

    /// <summary>
    /// 明信片主檔相關 API。
    /// 對應頁面：收藏館。負責「明信片長什麼樣子」，與紀錄探員實際獲得情況的 PostcardController 互補。
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
        /// 接收前端上傳的圖片與提示詞，轉發至 vlog.angelalala.com 生成 AI 明信片，並由後端轉換為 Base64 圖片編碼，自動將資料寫入 `md_postcard` 資料表。
        /// 
        /// **⚠️ 請求格式注意**：
        /// 必須使用 `multipart/form-data`，不能使用一般的 JSON。
        /// 
        /// **Request 欄位說明**：
        /// - `user_image` (必填, File): 玩家拍攝的照片檔案。
        /// - `spot_name` (選填, String): 景點名稱，例如 "台北101"。
        /// - `user_prompt` (選填, String): 畫風提示詞，例如 "復古水墨風"。
        /// - `story_id` (選填, String): 關聯的劇本代號，例如 "story_001"。
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
        ///     "imageUrl": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUg...", 
        ///     "category": "AI Generate"
        ///   }
        /// }
        /// ```
        /// *(註：因後端已改為直接將 Base64 存入 DB，前端不需再抓取此處的 imageUrl，請直接使用 Story/{story_id}/Image API 顯示圖片)*
        /// </remarks>
        [HttpPost]
        [Route("GenerateAi")]
        [Authorize] // 鎖住此 API，必須帶 Token
        public async Task<IActionResult> GenerateAi([FromForm] AiPostcardGenerateRequest request)
        {
            // 從 Token 抓取探員 ID
            var epId = User.FindFirst("ep_id")?.Value ?? User.Identity?.Name;
            if (string.IsNullOrEmpty(epId))
            {
                return Unauthorized(new ResultViewModel<string> { isSuccess = false, message = "無法驗證探員身分，請重新登入" });
            }

            if (request.user_image == null || request.user_image.Length == 0)
            {
                return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供圖片檔案" });
            }

            try
            {
                var resultEntity = await _service.GenerateAiPostcardAsync(request, epId);
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
                return StatusCode(500, new ResultViewModel<string>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 擴充功能 4：用 Story_Id 直接顯示圖片 API (解決 Base64 過長問題)

        /// <summary>
        /// 將該劇本最新的一張明信片，解碼為真實圖片檔案回傳。
        /// </summary>
        /// <remarks>
        /// 因同一個 story_id 可能會生成多次明信片，此 API 會自動抓取「最新建立」的那一張圖片。
        /// 前端不需處理 Base64，直接將此 API 當作 HTML 圖片網址使用：
        /// 
        /// `&lt;img src="https://你的後端網址/api/PostcardCatalog/Story/{story_id}/Image" /&gt;`
        /// </remarks>
        [HttpGet]
        [Route("Story/{storyId}/Image")]
        [AllowAnonymous] // 必須開放匿名，因為前端 <img> 標籤發送請求時無法夾帶 Token
        public async Task<IActionResult> GetImageByStoryId(string storyId)
        {
            try
            {
                // Service 會自動幫我們找這個劇本最新的一張，並轉回二進位位元組 (byte[])
                byte[] imageBytes = await _service.GetImageBytesByStoryAsync(storyId);
                
                if (imageBytes == null)
                {
                    return NotFound("找不到該劇本的圖片");
                }

                // 關鍵：用 File() 回傳二進位資料，並指定 Content-Type，瀏覽器就會完美把它當成圖片渲染
                return File(imageBytes, "image/png");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"讀取劇本 {storyId} 明信片圖片時發生錯誤");
                return StatusCode(500, "無法載入圖片");
            }
        }

        #endregion

        #region 擴充功能 2：請求 ibon 列印

        /// <summary>
        /// 透過 story_id 送出明信片至 ibon 列印，取得取件碼。
        /// </summary>
        /// <remarks>
        /// 傳入劇本 ID (story_id)，後端會自動撈取該劇本「最新的一張」圖片的 Base64 編碼，
        /// 並透過本機背景執行的 Python 微服務 (`127.0.0.1:9000`) 爬蟲上傳至 ibon，最後回傳真實的 10 碼取件碼。
        /// 
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "story_id": "story_001"
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
        ///     "qrcode_base64": "..."
        ///   }
        /// }
        /// ```
        /// </remarks>
        [HttpPost]
        [Route("Print")]
        public async Task<IActionResult> PrintIbon([FromBody] PrintPostcardRequest request) // 👈 參數改成新的 Request
        {
            if (string.IsNullOrEmpty(request.postcard_id))
            {
                 return BadRequest(new ResultViewModel<string> { isSuccess = false, message = "請提供明信片 ID (postcard_id)" });
            }

            try
            {
                // 👈 呼叫 Service 新的方法
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
                return StatusCode(500, new ResultViewModel<string>
                {
                    isSuccess = false,
                    message = ex.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 擴充功能 3：紀錄社群分享

        /// <summary>
        /// 紀錄使用者已將該劇本的明信片分享至社群平台。
        /// </summary>
        /// <remarks>
        /// **技術說明**：實際將圖片貼上 IG 或 FB 的動作，必須由前端呼叫原生的 Share Intent 完成。此 API 僅提供前端在「完成分享動作後」呼叫，供後端紀錄分享次數或發放獎勵。
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
        [HttpPost]
        [Route("Share")]
        public IActionResult RecordShare([FromBody] StoryShareRequest request)
        {
            if (string.IsNullOrEmpty(request.story_id))
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
        [HttpGet]
        [Route("")]
        public async Task<IActionResult> GetAll([FromQuery] string category = null)
        {
            try
            {
                var result = await _service.GetAllAsync(category);
                return Ok(new ResultViewModel<List<PostcardCatalogResponse>>
                {
                    isSuccess = true, message = "查詢成功", Result = result,
                });
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
        [HttpGet]
        [Route("{id}")]
        public async Task<IActionResult> GetById(string id)
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
        [HttpGet]
        [Route("by-story/{storyId}")]
        public async Task<IActionResult> GetByStoryId(string storyId)
        {
            try
            {
                var result = await _service.GetByStoryIdAsync(storyId);
                return Ok(new ResultViewModel<List<PostcardCatalogResponse>> { isSuccess = true, message = "查詢成功", Result = result });
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"取得劇本 {storyId} 明信片清單時發生錯誤");
                return StatusCode(500, new ResultViewModel<List<PostcardCatalogResponse>> { isSuccess = false, message = "系統發生未預期錯誤", Result = null });
            }
        }
        #endregion

        #region 新增明信片主檔 (測試用)
        /// <summary>
        /// 【⚠️ 測試/維護用】手動新增明信片主檔。
        /// </summary>
        /// <remarks>
        /// **注意**：此 API 僅供後台開發或資料庫測試使用，正式流程中明信片會透過 AI 生成自動建立。
        /// </remarks>
        [HttpPost]
        [Route("")]
        [Obsolete("此 API 僅供測試使用")]
        public async Task<IActionResult> Create([FromBody] PostcardCatalogRequest request)
        {
            try
            {
                await _service.CreateAsync(request);
                return Ok(new ResultViewModel<string> { isSuccess = true, message = "新增成功", Result = request.PostcardId });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "新增明信片時發生錯誤");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = "系統發生未預期錯誤", Result = null });
            }
        }
        #endregion

        #region 更新明信片主檔 (測試用)
        /// <summary>
        /// 【⚠️ 測試/維護用】更新明信片主檔。
        /// </summary>
        /// <remarks>
        /// **注意**：此 API 僅供後台開發或資料庫測試使用。
        /// </remarks>
        [HttpPost]
        [Route("{id}")]
        [Obsolete("此 API 僅供測試使用")]
        public async Task<IActionResult> Update(string id, [FromBody] PostcardCatalogRequest request)
        {
            try
            {
                var success = await _service.UpdateAsync(id, request);
                if (!success) return NotFound(new ResultViewModel<bool> { isSuccess = false, message = "查無此資料", Result = false });
                return Ok(new ResultViewModel<bool> { isSuccess = true, message = "更新成功", Result = true });
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"更新明信片 {id} 時發生錯誤");
                return StatusCode(500, new ResultViewModel<bool> { isSuccess = false, message = "系統發生未預期錯誤", Result = false });
            }
        }
        #endregion

        #region 刪除明信片主檔
        /// <summary>
        /// 刪除明信片主檔。
        /// </summary>
        [HttpPost]
        [Route("{id}/Delete")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                var success = await _service.DeleteAsync(id);
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
    }
}