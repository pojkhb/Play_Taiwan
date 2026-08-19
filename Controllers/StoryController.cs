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
    /// <summary>
    /// 劇本產生相關 API。
    /// 對應頁面：選擇、來點遊意思、選擇劇情、劇情觀看更多。
    /// </summary>

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

        /// <summary>
        /// 轉動命運轉盤，隨機抽取一個地區。
        /// </summary>
        /// <remarks>
        /// 對應「來點遊意思」頁面的轉盤抽取功能。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Story/Wheel/Spin
        /// </remarks>
        /// <returns>抽取到的地區資訊。</returns>
        // API：轉盤抽取地區（SpinWheel）－隨機抽取一個地區
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

        /// <summary>
        /// 取得可選擇的地區清單。
        /// </summary>
        /// <remarks>
        /// 對應「選擇」頁面「現在揪出發」輸入地區時的可選清單。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Story/Regions?mode=NOW
        /// </remarks>
        /// <param name="mode">查詢模式，預設為 NOW（目前定位）。</param>
        /// <returns>可選擇的地區清單。</returns>
        // API：地區清單（Regions）－回傳可選擇的地區清單
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

        /// <summary>
        /// 依地區與偏好產生可選擇的劇本選項清單。
        /// </summary>
        /// <remarks>
        /// 對應「選擇劇情」頁面的「劇本檔案館」列表。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Story/Generate
        ///     {
        ///       "region": "台南市"
        ///     }
        /// </remarks>
        /// <param name="req">產生劇本選項的請求資料，包含地區等條件。</param>
        /// <returns>符合條件的劇本選項清單。</returns>
        // API：產生劇本選項（Generate）－依地區與偏好回傳可選擇的劇本清單
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

        /// <summary>
        /// 取得指定劇本的詳細內容。
        /// </summary>
        /// <remarks>
        /// 對應「劇情觀看更多」頁面，顯示劇本前傳與探索總覽。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Story/{story_id}/Detail
        /// </remarks>
        /// <param name="story_id">劇本代號。</param>
        /// <returns>劇本詳細內容。</returns>
        // API：劇情觀看更多（Detail）－回傳指定劇本的前傳與探索總覽
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

        /// <summary>
        /// 確認選擇指定劇本卷。
        /// </summary>
        /// <remarks>
        /// 對應「劇情觀看更多」頁面的「確認此卷」按鈕，確認後即進入探索地圖。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Story/Confirm
        ///     {
        ///       "story_id": "S001"
        ///     }
        /// </remarks>
        /// <param name="req">確認選卷的請求資料。</param>
        /// <returns>確認後的劇本與節點資訊。</returns>
        // API：確認選卷（Confirm）－確認選擇劇本卷並回傳劇本與節點資訊
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