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
    // 模組設定 FrameFunction
    public class FrameFunctionController : ControllerBase
    {
        private readonly ILogger<FrameFunctionController> _logger;
        private readonly FrameFunctionService _service;

        public FrameFunctionController(ILogger<FrameFunctionController> logger, FrameFunctionService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 前端動態樣式
        [HttpGet]
        [Route("FrontendStyle")]
        // GET: api/FrameFunction/FrontendStyle
        public IActionResult Get_FrontendStyle()
        {
            try
            {
                return Ok(new ResultViewModel<List<FrontendStyleResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.Get_FrontendStyle(),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<FrontendStyleResponse>>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null,
                });
            }
        }
        #endregion

        #region 跑馬燈
        [HttpGet]
        [Route("Marquee")]
        // GET: api/FrameFunction/Marquee
        public IActionResult Get_Marquee()
        {
            try
            {
                return Ok(new ResultViewModel<List<MarqueeResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.Get_Marquee(),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<MarqueeResponse>>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null,
                });
            }
        }
        #endregion
    }
}
