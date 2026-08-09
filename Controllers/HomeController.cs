using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // 首頁 (對應畫面: 首頁 目前總覽)
    public class HomeController : ControllerBase
    {
        private readonly ILogger<HomeController> _logger;
        private readonly HomeService _service;

        public HomeController(ILogger<HomeController> logger, HomeService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 首頁目前總覽
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
                    Result = _service.GetOverview(),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<HomeOverviewResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion
    }
}