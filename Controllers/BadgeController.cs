using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 徽章相關 API（整併版）。
    /// 對應頁面：首頁總覽（徽章數）、過往紀錄、徽章圖鑑、收藏（徽章部分）。
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

        #region 取得徽章圖鑑（依系列分組 + 是否擁有）

        /// <summary>
        /// 取得系統所有徽章，依系列分組，並標示當前探員是否已擁有該徽章。
        /// </summary>
        /// <remarks>
        /// 整併原本 /api/Badge、/api/Favorite（徽章部分）、/api/Badge/Status 三支重複邏輯，
        /// 統一保留這一支，回傳格式依系列巢狀分組。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Badge/Status
        /// </remarks>
        [Authorize]
        [HttpGet]
        [Route("Status")]
        public IActionResult GetBadgeCatalog()
        {
            try
            {
                string epId = User.FindFirst("ep_id")?.Value
                    ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                return Ok(new ResultViewModel<List<BadgeSeriesGroup>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetBadgeCatalog(epId),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<BadgeSeriesGroup>> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion
    }
}