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
    /// [❌ 尚未完成 / 暫不使用]
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PostcardController : ControllerBase
    {
        private readonly ILogger<PostcardController> _logger;
        private readonly PostcardService _service;

        public PostcardController(ILogger<PostcardController> logger, PostcardService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 取得明信片詳情 (暫不使用)

        /// <summary>
        /// 【❌ 尚未完成 / 暫不使用】取得單張明信片的詳細內容。
        /// </summary>
        /// <remarks>
        /// 對應「明信片翻轉」頁面，顯示謎題解開後獲得的專屬記憶明信片。
        /// </remarks>
        [HttpGet]
        [Route("{postcard_id}")]
        [Obsolete("此 API 尚未完成，請勿使用")]
        public IActionResult GetPostcard(string postcard_id)
        {
            try
            {
                return Ok(new ResultViewModel<PostcardResponse>
                {
                    isSuccess = false,
                    message = "此 API 尚未完成",
                    Result = null,
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<PostcardResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 取得劇本所有明信片(明信片集結) (暫不使用)

        /// <summary>
        /// 【❌ 尚未完成 / 暫不使用】取得指定劇本已收集的所有明信片。
        /// </summary>
        /// <remarks>
        /// 對應「過往－明信片vlog」頁面的「明信片集結」區塊。
        /// </remarks>
        [HttpGet]
        [Route("Story/{story_id}")]
        [Obsolete("此 API 尚未完成，請勿使用")]
        public IActionResult GetByStory(string story_id)
        {
            try
            {
                return Ok(new ResultViewModel<List<PostcardResponse>>
                {
                    isSuccess = false,
                    message = "此 API 尚未完成",
                    Result = null,
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<PostcardResponse>> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 實體列印 (iBON) (暫不使用)

        /// <summary>
        /// 【❌ 尚未完成 / 暫不使用】送出明信片實體列印申請。
        /// </summary>
        /// <remarks>
        /// 對應「明信片翻轉」頁面的「實體列印」按鈕。
        /// </remarks>
        [HttpPost]
        [Route("Print")]
        [Obsolete("此 API 尚未完成，請勿使用")]
        public IActionResult Print([FromBody] PostcardPrintRequest req)
        {
            try
            {
                return Ok(new ResultViewModel<PostcardPrintResponse>
                {
                    isSuccess = false,
                    message = "此 API 尚未完成",
                    Result = null,
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<PostcardPrintResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 分享 (暫不使用)

        /// <summary>
        /// 【❌ 尚未完成 / 暫不使用】分享明信片至社群平台。
        /// </summary>
        /// <remarks>
        /// 對應「明信片翻轉」頁面的「分享」按鈕。
        /// </remarks>
        [HttpPost]
        [Route("Share")]
        [Obsolete("此 API 尚未完成，請勿使用")]
        public IActionResult Share([FromBody] PostcardShareRequest req)
        {
            try
            {
                return Ok(new ResultViewModel<string> { isSuccess = false, message = "此 API 尚未完成", Result = null });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<string> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion
    }
}