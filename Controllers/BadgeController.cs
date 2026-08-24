using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.dao; // 為了 BadgeResponse
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 徽章相關 API。
    /// 對應頁面：首頁總覽（徽章數）、過往紀錄、徽章圖鑑。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class BadgeController : ControllerBase
    {
        private readonly ILogger<BadgeController> _logger;
        private readonly BadgeService _service;

        public BadgeController(ILogger<BadgeController> logger, BadgeService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 取得我的所有徽章
        [Authorize]
        [HttpGet]
        [Route("")]
        public IActionResult GetMyBadges()
        {
            try
            {
                return Ok(new ResultViewModel<List<BadgeResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetMyBadges(),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<BadgeResponse>> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion

        #region 取得徽章圖鑑狀態 (所有徽章與是否擁有)
        /// <summary>
        /// 取得系統所有徽章，並標示當前探員是否已擁有該徽章。
        /// 適用於前端「成就館」頁面，未擁有的可顯示為灰階 (is_owned = false)。
        /// </summary>
        [Authorize]
        [HttpGet]
        [Route("Status")]
        public IActionResult GetAllBadgeStatus()
        {
            try
            {
                return Ok(new ResultViewModel<List<BadgeResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetAllBadgeStatus(),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<BadgeResponse>> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion
    }
}