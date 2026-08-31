using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Models;
using backend.Services;
using backend.ViewModels;
using System.IO;

namespace backend.Controllers
{
    /// <summary>
    /// 剪影圖片相關 API。
    /// 對應頁面：明信片翻轉（隱藏版剪影明信片）。
     /// [❌ 尚未完成]
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

        #region 取得剪影清單

        /// <summary>
        /// 取得所有剪影圖片清單。
        /// </summary>
        /// <remarks>
        /// 對應隱藏版剪影明信片的來源素材清單。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Silhouette
        /// </remarks>
        /// <returns>剪影圖片清單。</returns>
        // API：取得剪影清單（GetSilhouettes）－回傳所有剪影圖片清單
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

        #region 取得單一剪影

        /// <summary>
        /// 取得指定剪影圖片的詳細內容。
        /// </summary>
        /// <remarks>
        /// 對應單張隱藏版剪影明信片的原始素材資訊。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Silhouette/{silhouette_id}
        /// </remarks>
        /// <param name="silhouette_id">剪影圖片代號。</param>
        /// <returns>指定剪影圖片的詳細內容。</returns>
        // API：取得單一剪影（GetSilhouette）－回傳指定剪影圖片的詳細內容
        [HttpGet("{silhouette_id}")]
        public IActionResult GetSilhouette(string silhouette_id)
        {
            try
            {
                Silhouette result = _service.GetSilhouetteById(silhouette_id);

                return Ok(new ResultViewModel<Silhouette>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (KeyNotFoundException e)
            {
                return NotFound(new ResultViewModel<Silhouette>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "取得剪影失敗，silhouette_id: {SilhouetteId}", silhouette_id);

                return BadRequest(new ResultViewModel<Silhouette>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 產生亮度閾值剪影

        /// <summary>
        /// 依指定亮度閾值產生剪影圖片。
        /// </summary>
        /// <remarks>
        /// 依 threshold 參數將原始圖片轉換為黑白剪影，讓明信片內容不會過早被辨識出來。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Silhouette/{silhouette_id}/Generate?threshold=150
        /// </remarks>
        /// <param name="silhouette_id">剪影圖片代號。</param>
        /// <param name="threshold">亮度閾值，預設為 150。</param>
        /// <returns>產生後的剪影圖片網址。</returns>
        // API：產生亮度閾值剪影（GenerateSilhouette）－依亮度閾值將圖片轉換成剪影並回傳圖片網址
        [HttpPost("{silhouette_id}/Generate")]
        public IActionResult GenerateSilhouette(
            string silhouette_id,
            [FromQuery] int threshold = 150)
        {
            try
            {
                string imageUrl = _service.GenerateSilhouette(
                    silhouette_id,
                    threshold);

                return Ok(new ResultViewModel<string>
                {
                    isSuccess = true,
                    message = "剪影產生成功",
                    Result = imageUrl
                });
            }
            catch (KeyNotFoundException e)
            {
                return NotFound(new ResultViewModel<string>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
            catch (ArgumentException e)
            {
                return BadRequest(new ResultViewModel<string>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
            catch (FileNotFoundException e)
            {
                return NotFound(new ResultViewModel<string>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    "產生剪影失敗，silhouette_id: {SilhouetteId}",
                    silhouette_id);

                return BadRequest(new ResultViewModel<string>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion
    }
}