// 檔案路徑：System\Controllers\SilhouetteController.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using backend.Models;
using backend.Services;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 剪影圖片相關 API。
    /// 對應頁面：明信片翻轉（隱藏版剪影明信片、盲盒探索）。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SilhouetteController : ControllerBase
    {
        private readonly ILogger<SilhouetteController> _logger;
        private readonly SilhouetteService _service;

        public SilhouetteController(
            ILogger<SilhouetteController> logger,
            SilhouetteService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 1. 取得所有剪影清單

        /// <summary>
        /// 取得所有剪影圖片清單。
        /// </summary>
        /// <remarks>
        /// 對應隱藏版剪影明信片的素材選擇清單。
        /// 
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": [
        ///     {
        ///       "silhouette_id": "SIL_001",
        ///       "name": "台北101剪影",
        ///       "image_url": "data:image/png;base64,...",
        ///       "city": "臺北市",
        ///       "category": "地標",
        ///       "is_active": true,
        ///       "sort_order": 1
        ///     }
        ///   ]
        /// }
        /// ```
        /// </remarks>
        [HttpGet]
        public IActionResult GetSilhouettes()
        {
            try
            {
                List<Silhouette> result = _service.GetSilhouettes();

                return Ok(new ResultViewModel<List<Silhouette>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "取得剪影清單失敗");

                return BadRequest(new ResultViewModel<List<Silhouette>>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 2. 依指定地點與亮度閾值動態生成剪影圖片串流

        /// <summary>
        /// 依抽中的地區/地點名稱與亮度閾值，從 Neo4j 圖譜抓取圖片並即時轉為剪影圖片輸出。
        /// </summary>
        /// <remarks>
        /// **前端使用方式**：
        /// 當玩家透過轉盤或現在揪出發抽中特定地點後，可將地點名稱與亮度閾值帶入此 API，
        /// 前端可直接將此網址作為 `<img>` 來源，即時呈現神秘的隱藏版剪影：
        /// 
        /// `&lt;img src="https://你的後端網址/api/Silhouette/dynamic-image?place_name=臺南孔廟&amp;threshold=150" alt="動態剪影" /&gt;`
        /// </remarks>
        /// <param name="place_name">地點名稱 (例如 "臺南孔廟" 或 "台北101")。</param>
        /// <param name="threshold">亮度閾值 (0 ~ 255，預設 150)。</param>
        /// <returns>二進位圖片串流 (Image/PNG)。</returns>
        [HttpGet]
        [Route("dynamic-image")]
        [AllowAnonymous] // 開放匿名訪問，確保前端 <img> 標籤可以直接載入
        public async Task<IActionResult> GetDynamicSilhouetteImage(
            [FromQuery] string place_name,
            [FromQuery] int threshold = 150)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(place_name))
                {
                    return BadRequest("請提供地點名稱 (place_name)");
                }

                if (threshold < 0 || threshold > 255)
                {
                    return BadRequest("threshold 必須介於 0 到 255 之間");
                }

                // 透過 Service 打 Neo4j 抓圖並即時轉成剪影位元組
                byte[] imageBytes = await _service.GenerateSilhouetteFromNeo4jAsync(place_name, threshold);

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    return NotFound($"找不到地點 [{place_name}] 的圖譜圖片資料");
                }

                return File(imageBytes, "image/png");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"動態產生剪影圖片失敗，地點: {place_name}");
                return StatusCode(500, "無法產生剪影圖片");
            }
        }

        #endregion

        #region 3. 依資料庫代號取得剪影圖片串流

        /// <summary>
        /// 取得指定剪影代號的真實圖片檔案。
        /// </summary>
        /// <remarks>
        /// `&lt;img src="https://你的後端網址/api/Silhouette/{silhouette_id}/Image" /&gt;`
        /// </remarks>
        [HttpGet]
        [Route("{silhouette_id}/Image")]
        [AllowAnonymous]
        public IActionResult GetSilhouetteImage(string silhouette_id)
        {
            try
            {
                byte[] imageBytes = _service.GetSilhouetteImageBytes(silhouette_id);

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    return NotFound("找不到該剪影的圖片資料");
                }

                return File(imageBytes, "image/png");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"讀取剪影圖片串流失敗，ID: {silhouette_id}");
                return StatusCode(500, "無法載入圖片");
            }
        }

        #endregion
    }
}