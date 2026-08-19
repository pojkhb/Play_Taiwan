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
    /// 收藏相關 API。
    /// 對應頁面：收藏。
    /// </summary>

    [ApiController]
    [Route("api/[controller]")]
    // 收藏
    public class FavoriteController : ControllerBase
    {
        private readonly ILogger<FavoriteController> _logger;
        private readonly FavoriteService _service;

        public FavoriteController(ILogger<FavoriteController> logger, FavoriteService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 取得收藏清單

        /// <summary>
        /// 取得目前探員的收藏清單。
        /// </summary>
        /// <remarks>
        /// 對應「收藏」頁面，回傳台灣古籍系列、山海寶島系列等已收藏項目。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Favorite
        /// </remarks>
        /// <returns>目前探員的收藏項目清單。</returns>
        // API：取得收藏清單（GetFavorites）－回傳目前探員的收藏項目清單
        [HttpGet]
        [Route("")]
        // GET: api/Favorite
        public IActionResult GetFavorites()
        {
            try
            {
                return Ok(new ResultViewModel<List<FavoriteItemResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetFavorites(),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<FavoriteItemResponse>> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion
    }
}