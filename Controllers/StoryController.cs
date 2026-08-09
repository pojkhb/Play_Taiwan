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
    // 劇本產生 (對應畫面: 選擇/來點遊意思/輸入資訊/選擇劇情/劇情觀看更多)
    public class StoryController : ControllerBase
    {
        private readonly ILogger<StoryController> _logger;
        private readonly StoryService _service;

        public StoryController(ILogger<StoryController> logger, StoryService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 轉盤抽取地區 (來點遊意思)
        [HttpPost]
        [Route("Wheel/Spin")]
        // POST: api/Story/Wheel/Spin
        public IActionResult SpinWheel()
        {
            try
            {
                return Ok(new ResultViewModel<StoryWheelSpinResponse>
                {
                    isSuccess = true,
                    message = "抽取成功",
                    Result = _service.SpinWheel(),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<StoryWheelSpinResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion

        #region 產生劇本選項 (RAG+LLM)
        [HttpPost]
        [Route("Generate")]
        // POST: api/Story/Generate
        public IActionResult Generate([FromBody] StoryGenerateRequest req)
        {
            try
            {
                return Ok(new ResultViewModel<List<StoryOptionResponse>>
                {
                    isSuccess = true,
                    message = "產生成功",
                    Result = _service.GenerateStoryOptions(req),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<StoryOptionResponse>> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion

        #region 劇情觀看更多
        [HttpGet]
        [Route("{story_id}/Detail")]
        // GET: api/Story/{story_id}/Detail
        public IActionResult Detail(string story_id)
        {
            try
            {
                return Ok(new ResultViewModel<StoryDetailResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetStoryDetail(story_id),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<StoryDetailResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion

        #region 確認選卷 (確定要玩此劇本)
        [HttpPost]
        [Route("Confirm")]
        // POST: api/Story/Confirm
        public IActionResult Confirm([FromBody] StoryConfirmRequest req)
        {
            try
            {
                _service.ConfirmStory(req);
                return Ok(new ResultViewModel<string> { isSuccess = true, message = "確認選卷成功", Result = null });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<string> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion

        #region 劇本結束總結
        [HttpGet]
        [Route("{story_id}/Ending")]
        // GET: api/Story/{story_id}/Ending
        public IActionResult Ending(string story_id)
        {
            try
            {
                return Ok(new ResultViewModel<StoryEndingResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetEnding(story_id),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<StoryEndingResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion
    }
}