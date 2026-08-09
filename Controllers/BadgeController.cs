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