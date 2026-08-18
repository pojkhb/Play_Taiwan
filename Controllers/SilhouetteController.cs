using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Models;
using backend.Services;
using backend.ViewModels;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SilhouetteController : ControllerBase
    {
        private readonly ILogger<SilhouetteController> _logger;
        private readonly SilhouetteService _service;

        public SilhouetteController(
            ILogger<SilhouetteController> logger,
            SilhouetteService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 取得剪影清單

        [HttpGet]
        public IActionResult GetSilhouettes()
        {
            try
            {
                List<Silhouette> result = _service.GetSilhouettes();

                return Ok(new ResultViewModel<List<Silhouette>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "取得剪影清單失敗");

                return BadRequest(new ResultViewModel<List<Silhouette>>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 取得单一剪影

        [HttpGet("{silhouette_id}")]
        public IActionResult GetSilhouette(string silhouette_id)
        {
            try
            {
                Silhouette result = _service.GetSilhouetteById(silhouette_id);

                return Ok(new ResultViewModel<Silhouette>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (KeyNotFoundException e)
            {
                return NotFound(new ResultViewModel<Silhouette>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "取得剪影失敗，silhouette_id: {SilhouetteId}", silhouette_id);

                return BadRequest(new ResultViewModel<Silhouette>
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
