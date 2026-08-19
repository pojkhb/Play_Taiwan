using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 明信片相關 API。
    /// 對應頁面：明信片翻轉、過往－明信片vlog、收藏。
    /// </summary>

    [ApiController]
    [Route("api/[controller]")]
    // 明信片 (對應畫面: 明信片翻轉, 過往-明信片vlog, 收藏)
    public class PostcardController : ControllerBase
    {
        private readonly ILogger<PostcardController> _logger;
        private readonly PostcardService _service;

        public PostcardController(ILogger<PostcardController> logger, PostcardService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 取得明信片詳情

        /// <summary>
        /// 取得單張明信片的詳細內容。
        /// </summary>
        /// <remarks>
        /// 對應「明信片翻轉」頁面，顯示謎題解開後獲得的專屬記憶明信片。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Postcard/{postcard_id}
        /// </remarks>
        /// <param name="postcard_id">明信片代號。</param>
        /// <returns>明信片詳細內容。</returns>
        // API：取得明信片詳情（GetPostcard）－回傳指定明信片的詳細內容
        [HttpGet]
        [Route("{postcard_id}")]
        // GET: api/Postcard/{postcard_id}
        public IActionResult GetPostcard(string postcard_id)
        {
            try
            {
                return Ok(new ResultViewModel<PostcardResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetPostcard(postcard_id),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<PostcardResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 取得劇本所有明信片(明信片集結)

        /// <summary>
        /// 取得指定劇本已收集的所有明信片。
        /// </summary>
        /// <remarks>
        /// 對應「過往－明信片vlog」頁面的「明信片集結」區塊。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Postcard/Story/{story_id}
        /// </remarks>
        /// <param name="story_id">劇本代號。</param>
        /// <returns>該劇本已收集的明信片清單。</returns>
        // API：取得劇本所有明信片（GetByStory）－回傳指定劇本已收集的明信片清單
        [HttpGet]
        [Route("Story/{story_id}")]
        // GET: api/Postcard/Story/{story_id}
        public IActionResult GetByStory(string story_id)
        {
            try
            {
                return Ok(new ResultViewModel<List<PostcardResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetPostcardsByStory(story_id),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<PostcardResponse>> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 實體列印 (iBON)

        /// <summary>
        /// 送出明信片實體列印申請。
        /// </summary>
        /// <remarks>
        /// 對應「明信片翻轉」頁面的「實體列印」按鈕，送出後至 7-11 iBON 機台取件。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Postcard/Print
        ///     {
        ///       "postcard_id": "PC001"
        ///     }
        /// </remarks>
        /// <param name="req">列印申請資料。</param>
        /// <returns>列印申請結果，含 iBON 取件編號。</returns>
        // API：實體列印（Print）－送出明信片列印申請並回傳 iBON 取件編號
        [HttpPost]
        [Route("Print")]
        // POST: api/Postcard/Print
        public IActionResult Print([FromBody] PostcardPrintRequest req)
        {
            try
            {
                return Ok(new ResultViewModel<PostcardPrintResponse>
                {
                    isSuccess = true,
                    message = "上傳成功，請至7-11 iBON機台輸入取件編號",
                    Result = _service.PrintPostcard(req),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<PostcardPrintResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 分享

        /// <summary>
        /// 分享明信片至社群平台。
        /// </summary>
        /// <remarks>
        /// 對應「明信片翻轉」頁面的「分享」按鈕。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Postcard/Share
        ///     {
        ///       "postcard_id": "PC001"
        ///     }
        /// </remarks>
        /// <param name="req">分享請求資料。</param>
        /// <returns>分享執行結果。</returns>
        // API：分享（Share）－將明信片分享至社群平台
        [HttpPost]
        [Route("Share")]
        // POST: api/Postcard/Share
        public IActionResult Share([FromBody] PostcardShareRequest req)
        {
            try
            {
                _service.SharePostcard(req);
                return Ok(new ResultViewModel<string> { isSuccess = true, message = "分享成功", Result = null });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<string> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion
    }
}