using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;

namespace backend.Controllers
{
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