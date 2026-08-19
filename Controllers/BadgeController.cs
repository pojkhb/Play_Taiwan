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
    /// 徽章相關 API。
    /// 對應頁面：首頁總覽（徽章數）、過往紀錄。
    /// </summary>

    [ApiController]
    [Route("api/[controller]")]
    // 徽章
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

        /// <summary>
        /// 取得目前探員已獲得的所有徽章。
        /// </summary>
        /// <remarks>
        /// 對應「首頁－目前總覽」與「過往－收藏館」頁面的徽章清單。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Badge
        /// </remarks>
        /// <returns>目前探員已獲得的徽章清單。</returns>
        // API：取得我的所有徽章（GetMyBadges）－回傳目前探員已獲得的徽章清單
        [HttpGet]
        [Route("")]
        // GET: api/Badge
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
    }
}