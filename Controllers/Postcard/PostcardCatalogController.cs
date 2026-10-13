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
using backend.util;
using backend.utils;


namespace backend.Controllers
{
    /// <summary>
    /// 明信片相關 API。
    /// 對應頁面：收藏館。新資料庫已把主檔與擁有紀錄合併成 postcard 一張表，
    /// 因此這裡回傳的就是「目前登入者自己的明信片」。
    /// 每張明信片有兩個網址：顯示網址（GET api/PostcardCatalog/{id}/image，打開就是圖片）
    /// 與下載網址（GET api/PostcardCatalog/{id}/download，打開會存成檔案）。
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

        /// <summary>顯示網址、下載網址組成完整網址</summary>
        private void WithPublicUrls(PostcardCatalogResponse postcard)
        {
            postcard.ImageUrl = PublicUrl.Of(Request, postcard.ImageUrl);
            postcard.DownloadUrl = PublicUrl.Of(Request, postcard.DownloadUrl);
        }


        #region 擴充功能 1：生成 AI 明信片


        /// <summary>
        /// 呼叫外部服務生成 AI 明信片並存入資料庫。
        /// </summary>
        /// <remarks>
        /// 接收前端上傳的照片與提示詞，轉發至外部 AI 服務生成明信片並存入資料表 `postcard`，
        /// 同時將此明信片綁定給發起請求的探員。**必須帶 Token**（後端會從 Token 解析 au_id）。
        ///
        /// **⚠️ 請求格式注意**：必須使用 `multipart/form-data`，不能使用一般的 JSON。
        ///
        /// **Request 範例**（multipart/form-data 欄位）：
        /// ```
        /// user_image: (檔案) taipei101.jpg
        /// spot_name: "台北101"
        /// user_prompt: "復古水墨風"
        /// story_id: 12
        /// node_id: 345
        /// ```
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "AI 明信片生成且儲存成功",
        ///   "Result": {
        ///     "p_id": 21,
        ///     "au_id": 7,
        ///     "s_id": 12,
        ///     "sn_id": 345,
        ///     "p_name": "台北101 專屬明信片",
        ///     "p_summary": "晨曦的秘密花園，夕陽的城市堡壘...",
        ///     "p_imag_url": "https://你的後端網址/api/PostcardCatalog/21/image",
        ///     "download_url": "https://你的後端網址/api/PostcardCatalog/21/download",
        ///     "is_night": 0
        ///   }
        /// }
        /// ```
        /// `p_imag_url` 是明信片圖片的顯示網址，直接拿來顯示（打開就是圖片）；`download_url` 是下載網址，「下載」按鈕打開它會存成檔案。
        /// 其他明信片 API 回傳的 `ImageUrl`、`DownloadUrl` 也是同樣的兩個網址。
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
                resultEntity.p_imag_url = PublicUrl.Of(Request, resultEntity.p_imag_url);
                resultEntity.download_url = PublicUrl.Of(Request, resultEntity.download_url);
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
        /// 傳入明信片 ID (postcard_id)，後端會把該張明信片的圖片內容（跟顯示用的是同一份）
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
        /// 取得目前登入者的所有明信片（最新的在前面）。
        /// </summary>
        /// <remarks>
        /// `ImageUrl` 是明信片圖片的顯示網址，直接拿來顯示（打開就是圖片）；`DownloadUrl` 是下載網址，「下載」按鈕打開它會存成檔案。
        ///
        /// **Request 範例**：
        /// ```
        /// GET /api/PostcardCatalog
        /// ```
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": [
        ///     {
        ///       "PostcardId": 21,
        ///       "StoryId": 12,
        ///       "NodeId": 345,
        ///       "PostcardName": "台北101 專屬明信片",
        ///       "Summary": "晨曦的秘密花園...",
        ///       "ImageUrl": "https://你的後端網址/api/PostcardCatalog/21/image",
        ///       "DownloadUrl": "https://你的後端網址/api/PostcardCatalog/21/download",
        ///       "IsNightEdition": false,
        ///       "CreatedAt": "2024-12-01T10:00:00",
        ///       "UpdatedAt": "2024-12-01T10:00:00"
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
                result.ForEach(WithPublicUrls);
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
        /// GET /api/PostcardCatalog/21
        /// ```
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": {
        ///     "PostcardId": 21,
        ///     "StoryId": 12,
        ///     "NodeId": 345,
        ///     "PostcardName": "台北101 專屬明信片",
        ///     "Summary": "晨曦的秘密花園...",
        ///     "ImageUrl": "https://你的後端網址/api/PostcardCatalog/21/image",
        ///     "DownloadUrl": "https://你的後端網址/api/PostcardCatalog/21/download",
        ///     "IsNightEdition": false
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
                WithPublicUrls(result);
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
        /// 同一個 story_id 可能生成過多次明信片，此 API 回傳全部清單（依建立時間排序）。
        /// 每一張的 `ImageUrl` 是圖片的顯示網址，直接拿來顯示；`DownloadUrl` 是下載網址。
        ///
        /// **Request 範例**：
        /// ```
        /// GET /api/PostcardCatalog/by-story/12
        /// ```
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": [
        ///     {
        ///       "PostcardId": 21,
        ///       "StoryId": 12,
        ///       "NodeId": 345,
        ///       "PostcardName": "台北101 專屬明信片",
        ///       "ImageUrl": "https://你的後端網址/api/PostcardCatalog/21/image",
        ///       "DownloadUrl": "https://你的後端網址/api/PostcardCatalog/21/download",
        ///       "CreatedAt": "2024-12-01T10:00:00"
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
                result.ForEach(WithPublicUrls);
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
        /// 【前端不用另外接：這就是明信片的圖片網址】依 postcard_id 顯示指定明信片的圖片。
        /// </summary>
        /// <remarks>
        /// 直接回傳圖片本身（image/png、image/jpeg…），打開就是顯示圖片，不會轉址、也不會變成下載檔案
        /// （要存成檔案請用 `{id}/download`）。
        /// 所有明信片 API 回傳的顯示網址（`ImageUrl`、`p_imag_url`、劇本回顧的 `image_url`、首頁卡片的 `hc_image`）都是這一支，
        /// 前端不用自己組，拿到就直接顯示：
        ///
        /// ```html
        /// <img src="https://你的後端網址/api/PostcardCatalog/21/image" />
        /// ```
        ///
        /// 圖片第一次會向 AI 服務拿，之後存在後端，所以 AI 服務暫時連不上也看得到。不需要帶 Token。
        ///
        /// **Request 範例**：
        /// ```
        /// GET /api/PostcardCatalog/21/image
        /// ```
        ///
        /// 查無資料時回傳 404 純文字：`找不到該明信片的圖片`；圖片暫時拿不到時回傳 502 純文字：`無法載入圖片`
        /// </remarks>
        /// <response code="200">圖片本身（不是 JSON）</response>
        [HttpGet]
        [Route("{id:int}/image")]
        [AllowAnonymous]
        public async Task<IActionResult> GetImageById(int id)
        {
            try
            {
                byte[] image = await _service.GetImageAsync(id);
                if (image == null) return NotFound("找不到該明信片的圖片");

                // 明信片生成後內容不會變，讓 App 與瀏覽器把圖片留著，不用每次重新拿
                Response.Headers.CacheControl = "public, max-age=86400";
                return File(image, PostcardCatalogService.ContentTypeOf(image) ?? "image/png");   // 沒有指定檔名：直接顯示，不是下載
            }
            catch (Exception ex)
            {
                // 圖片在 AI 服務那邊，拿不到多半是對方的問題
                _logger.LogError(ex, $"讀取明信片 {id} 圖片時發生錯誤");
                return StatusCode(502, "無法載入圖片");
            }
        }
        #endregion


        #region 依明信片 ID 下載圖片


        /// <summary>
        /// 【前端不用另外接：這就是明信片的下載網址】依 postcard_id 下載指定明信片的圖片。
        /// </summary>
        /// <remarks>
        /// 內容跟顯示用的 `{id}/image` 是同一張圖，差別是打開會存成檔案（Content-Disposition: attachment），
        /// 檔名是明信片名稱，例如 `台北101 專屬明信片.png`。
        /// 明信片 API 回傳的 `DownloadUrl`（生成結果、劇本回顧是 `download_url`）就是這一支，「下載」按鈕直接打開它。
        /// 不需要帶 Token。
        ///
        /// **Request 範例**：
        /// ```
        /// GET /api/PostcardCatalog/21/download
        /// ```
        ///
        /// 查無資料時回傳 404 純文字：`找不到該明信片的圖片`；圖片暫時拿不到時回傳 502 純文字：`無法下載圖片`
        /// </remarks>
        /// <response code="200">圖片檔案（不是 JSON）</response>
        [HttpGet]
        [Route("{id:int}/download")]
        [AllowAnonymous]
        public async Task<IActionResult> DownloadImageById(int id)
        {
            try
            {
                var download = await _service.GetDownloadAsync(id);
                if (download == null) return NotFound("找不到該明信片的圖片");

                var (bytes, fileName) = download.Value;
                return File(bytes, PostcardCatalogService.ContentTypeOf(bytes) ?? "image/png", fileName);   // 有指定檔名：存成檔案
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"下載明信片 {id} 圖片時發生錯誤");
                return StatusCode(502, "無法下載圖片");
            }
        }
        #endregion
    }
}