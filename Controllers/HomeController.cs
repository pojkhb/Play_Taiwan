using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 首頁相關 API。
    /// 對應頁面：首頁。
    /// </summary>

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

        /// <summary>
        /// 取得首頁目前總覽資訊。
        /// </summary>
        /// <remarks>
        /// 對應「首頁」頁面的目前總覽卡片，包含已完成探索數、明信片、徽章、Vlog 等統計數字。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Home/Overview
        /// </remarks>
        /// <returns>首頁目前總覽的統計資訊。</returns>
        // API：首頁目前總覽（Overview）－回傳已完成探索數、明信片、徽章、Vlog 等統計數字
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