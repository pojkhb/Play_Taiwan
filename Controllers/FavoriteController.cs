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