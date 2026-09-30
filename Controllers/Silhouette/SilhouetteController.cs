// 檔案路徑：System\Controllers\Silhouette\SilhouetteController.cs
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
        ///       "si_id": 1,
        ///       "si_name": "台北101剪影",
        ///       "si_type": "地標",
        ///       "si_silhouette_image": "https://.../silhouette.png",
        ///       "si_image_url": "data:image/png;base64,...",
        ///       "si_hint": "城市裡最高的那根針"
        ///     }
        ///   ]
        /// }
        /// ```
        /// </remarks>
        [HttpGet]
        [ProducesResponseType(typeof(ResultViewModel<List<Silhouette>>), 200)]
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

        #region 2. 為劇本節點產生剪影

        /// <summary>
        /// 為劇本每個節點產生地圖用的剪影（劇本生成後會自動在背景執行，這支用來補做或查看結果）。
        /// </summary>
        /// <remarks>
        /// 用景點照片（地圖解鎖後顯示的同一張）做去背實心剪影：背景透明、建築物填成深色，
        /// 存成靜態檔 `/images/silhouettes/generated/xxx.png`，並連到節點。
        /// 地圖 `GET /api/Map/{story_id}` 的每個節點會帶 `silhouette_image_url`，前端直接接在後端網址後面顯示。
        ///
        /// 同一張照片的剪影跨劇本重用；節點已經有剪影就略過，可以重複呼叫。
        /// `area_ratio` 接近 1 代表照片沒有天空（例如空拍），剪影會是一整塊。
        ///
        ///     POST /api/Silhouette/Story/1/Generate
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("Story/{story_id:int}/Generate")]
        [ProducesResponseType(typeof(ResultViewModel<SilhouetteGenerateResult>), 200)]
        public async Task<IActionResult> GenerateForStory(int story_id)
        {
            try
            {
                SilhouetteGenerateResult result = await _service.GenerateForStoryAsync(story_id);
                return Ok(new ResultViewModel<SilhouetteGenerateResult>
                {
                    isSuccess = true,
                    message = $"新產生 {result.generated} 張、沿用 {result.reused} 張剪影",
                    Result = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "產生劇本剪影失敗，story_id: {StoryId}", story_id);
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = ex.Message, Result = null });
            }
        }

        #endregion
    }
}