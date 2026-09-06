using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 商家專屬後台 API。
    /// 提供商家修改店名、取得近期檔案列表(依時間排序)、生成影音及查看 Reels 完成畫面等功能。
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class MerchantController : ControllerBase
    {
        private readonly ILogger<MerchantController> _logger;
        private readonly MerchantService _service;

        public MerchantController(ILogger<MerchantController> logger, MerchantService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 1. 商家名稱修改
        public class UpdateStoreNameRequest
        {
            /// <summary>新的店家名稱</summary>
            public string store_name { get; set; }
        }

        /// <summary>
        /// 修改登入商家的「店家名稱」。
        /// </summary>
        /// <remarks>
        /// 對應設定頁面中的「預設資訊－店家名稱」修改與儲存功能。
        /// 
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "store_name": "日式復古串燒居酒屋"
        /// }
        /// ```
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "店家名稱修改成功",
        ///   "Result": null
        /// }
        /// ```
        /// </remarks>
        /// <param name="req">包含新店名名稱的物件。</param>
        /// <returns>修改成功與否的狀態訊息。</returns>
        [HttpPost]
        [Route("StoreName")]
        public IActionResult UpdateStoreName([FromBody] UpdateStoreNameRequest req)
        {
            try
            {
                string epId = User.FindFirst("ep_id")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(epId)) 
                    return Unauthorized(new ResultViewModel<string> { isSuccess = false, message = "無法驗證身分" });

                _service.UpdateStoreName(epId, req.store_name);

                return Ok(new ResultViewModel<string>
                {
                    isSuccess = true,
                    message = "店家名稱修改成功",
                    Result = null
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "修改店家名稱失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion

        #region 2. 已經生成檔案列表 (依編輯時間新到舊排序)
        /// <summary>
        /// 取得商家已生成的檔案清單。
        /// </summary>
        /// <remarks>
        /// 對應商家首頁的「近期檔案」列表，後端會自動依**編輯時間 (updated_at) 從新到舊**排序回傳。
        /// 
        /// **Request 範例**：
        /// 
        ///     GET /api/Merchant/Files
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": [
        ///     {
        ///       "vlog_id": "VLOG_12345678",
        ///       "title": "日式串燒限時特惠活動",
        ///       "video_url": "[https://example.com/video.mp4](https://example.com/video.mp4)",
        ///       "updated_at": "2026-09-07 12:30"
        ///     }
        ///   ]
        /// }
        /// ```
        /// </remarks>
        /// <returns>依編輯時間新到舊排序的檔案清單陣列。</returns>
        [HttpGet]
        [Route("Files")]
        public IActionResult GetMerchantFiles()
        {
            try
            {
                string epId = User.FindFirst("ep_id")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(epId)) 
                    return Unauthorized(new ResultViewModel<string> { isSuccess = false, message = "無法驗證身分" });

                var files = _service.GetMerchantFiles(epId);

                return Ok(new ResultViewModel<object>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = files
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "取得商家檔案清單失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion

        #region 3. 商家生成影音
        /// <summary>
        /// 商家送出影音生成任務。
        /// </summary>
        /// <remarks>
        /// 對應「生成」頁面，讓商家輸入故事語氣、推廣資訊與上傳素材後點擊生成。
        /// 
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "tone": "質感專業",
        ///   "promotion_text": "下班後的微醺，是你應得的獎賞。",
        ///   "media_url": "[https://example.com/material.mp4](https://example.com/material.mp4)"
        /// }
        /// ```
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "影音生成任務已啟動",
        ///   "Result": {
        ///     "vlog_id": "VLOG_A1B2C3D4"
        ///   }
        /// }
        /// ```
        /// </remarks>
        /// <param name="req">包含故事語氣、推廣資訊與素材網址的請求物件。</param>
        /// <returns>新產生的影音任務識別碼 (vlog_id)。</returns>
        [HttpPost]
        [Route("GenerateVlog")]
        public async Task<IActionResult> GenerateVlog([FromBody] GenerateVlogRequest req)
        {
            try
            {
                string epId = User.FindFirst("ep_id")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(epId)) 
                    return Unauthorized(new ResultViewModel<string> { isSuccess = false, message = "無法驗證身分" });

                string vlogId = await _service.CreateVlogTaskAsync(epId, req);

                return Ok(new ResultViewModel<object>
                {
                    isSuccess = true,
                    message = "影音生成任務已啟動",
                    Result = new { vlog_id = vlogId }
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "商家生成影音失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion

        #region 4. 商家最後生成畫面 (取得 Reels 影音與推薦文/標籤)
        /// <summary>
        /// 取得指定影音的最終生成結果畫面資料。
        /// </summary>
        /// <remarks>
        /// 對應最後的「完成」頁面與 Reels 影音預覽畫面，提供影音網址、推薦配文 (`caption`)、推薦標籤與分享狀態。
        /// 
        /// **Request 範例**：
        /// 
        ///     GET /api/Merchant/VlogResult/VLOG_A1B2C3D4
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": {
        ///     "vlog_id": "VLOG_A1B2C3D4",
        ///     "title": "日式串燒限時特惠活動",
        ///     "caption": "下班後的微醺，是你應得的獎賞...",
        ///     "video_url": "[https://example.com/video.mp4](https://example.com/video.mp4)",
        ///     "hashtags": ["#居酒屋推薦", "#巷弄美食"],
        ///     "status": "COMPLETED"
        ///   }
        /// }
        /// ```
        /// </remarks>
        /// <param name="vlog_id">影音檔案的唯一識別碼 (vlog_id)。</param>
        /// <returns>Reels 影音播放連結、推薦配文與標籤陣列。</returns>
        [HttpGet]
        [Route("VlogResult/{vlog_id}")]
        public IActionResult GetVlogResult(string vlog_id)
        {
            try
            {
                var result = _service.GetVlogResult(vlog_id);

                return Ok(new ResultViewModel<object>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "取得影音生成結果失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }
        #endregion
    }
}