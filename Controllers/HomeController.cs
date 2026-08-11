using System;
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
    public class HomeController : ControllerBase
    {
        private readonly ILogger<HomeController> _logger;
        private readonly HomeService _service;

        public HomeController(
            ILogger<HomeController> logger,
            HomeService service
        )
        {
            _logger = logger;
            _service = service;
        }

        #region 首頁目前總覽

        [Authorize]
        [HttpGet]
        [Route("Overview")]
        // GET: api/Home/Overview
        public IActionResult Overview()
        {
            try
            {
                return Ok(new ResultViewModel<HomeOverviewResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetOverview()
                });
            }
            catch (UnauthorizedAccessException e)
            {
                return Unauthorized(new ResultViewModel<HomeOverviewResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "首頁總覽查詢失敗");

                return StatusCode(500, new ResultViewModel<HomeOverviewResponse>
                {
                    isSuccess = false,
                    message = "首頁總覽查詢失敗：" + e.Message,
                    Result = null
                });
            }
        }

        #endregion
    }
}