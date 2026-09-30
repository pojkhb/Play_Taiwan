using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.utils;
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
                _service.UpdateStoreName(User.GetAuId(), req.store_name);

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
                var files = _service.GetMerchantFiles(User.GetAuId());

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
    }
}