using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // 劇本產生
    public class StoryController : ControllerBase
    {
        private readonly ILogger<StoryController> _logger;
        private readonly StoryService _service;

        public StoryController(
            ILogger<StoryController> logger,
            StoryService service
        )
        {
            _logger = logger;
            _service = service;
        }

        #region 來點遊意思：轉盤抽取地區

        [Authorize]
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
                    Result = _service.WheelSpin()
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "轉盤抽取地區失敗");

                return StatusCode(500, new ResultViewModel<StoryWheelSpinResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 現在揪出發：地區清單

        [Authorize]
        [HttpGet]
        [Route("Regions")]
        // GET: api/Story/Regions?mode=NOW
        public IActionResult Regions(
            [FromQuery] string mode = "NOW"
        )
        {
            try
            {
                return Ok(new ResultViewModel<List<StoryWheelSpinResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetRegions(mode)
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢地區清單失敗");

                return StatusCode(
                    500,
                    new ResultViewModel<List<StoryWheelSpinResponse>>
                    {
                        isSuccess = false,
                        message = e.Message,
                        Result = null
                    }
                );
            }
        }

        #endregion

        #region 產生劇本選項

        [Authorize]
        [HttpPost]
        [Route("Generate")]
        // POST: api/Story/Generate
        public IActionResult Generate(
            [FromBody] StoryGenerateRequest req
        )
        {
            try
            {
                return Ok(new ResultViewModel<List<StoryOptionResponse>>
                {
                    isSuccess = true,
                    message = "產生成功",
                    Result = _service.GenerateOptions(req)
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "產生劇本選項失敗");

                return StatusCode(
                    500,
                    new ResultViewModel<List<StoryOptionResponse>>
                    {
                        isSuccess = false,
                        message = e.Message,
                        Result = null
                    }
                );
            }
        }

        #endregion

        #region 劇情觀看更多

        [Authorize]
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
                    Result = _service.GetDetail(story_id)
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢劇本詳情失敗");

                return StatusCode(500, new ResultViewModel<StoryDetailResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 確認選卷

        [Authorize]
        [HttpPost]
        [Route("Confirm")]
        // POST: api/Story/Confirm
        public IActionResult Confirm(
            [FromBody] StoryConfirmRequest req
        )
        {
            try
            {
                // 目前只回傳劇本與節點資訊
                // 之後再補 ep_story_progress 寫入資料庫
                StoryDetailResponse detail =
                    _service.ConfirmStory(req);

                return Ok(new ResultViewModel<StoryDetailResponse>
                {
                    isSuccess = true,
                    message = "確認選卷成功，即將進入探索地圖",
                    Result = detail
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "確認選卷失敗");

                return StatusCode(500, new ResultViewModel<StoryDetailResponse>
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