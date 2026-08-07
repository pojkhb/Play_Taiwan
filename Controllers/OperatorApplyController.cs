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
    // 使用者資料維護 OperatorApply
    public class OperatorApplyController : ControllerBase
    {
        private readonly ILogger<OperatorApplyController> _logger;
        private readonly OperatorApplyService _service;
        private readonly SharedFunctionService _shareservice;

        public OperatorApplyController(ILogger<OperatorApplyController> logger, OperatorApplyService service, SharedFunctionService shareservie)
        {
            _logger = logger;
            _service = service;
            _shareservice = shareservie;
        }

        #region 帳號申請-申請功能
        [HttpPost]
        [Route("Apply")]
        // POST: api/OperatorApply/Apply
        public IActionResult Apply([FromBody] ApplyReq req)
        {
            try
            {
                string message = _service.Insert_OperatorApply(req);
                if(message == "申請已通過，請前往登入" || message == "已成功申請，請等待審核")
                    return Ok(new ResultViewModel<string>
                    {
                        isSuccess = true,
                        message = message,
                        Result = null,
                    });
                return NotFound(new ResultViewModel<string>
                {
                    isSuccess = true,
                    message = message,
                    Result = null,
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<string>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null,
                });
            }
        }
        #endregion

        #region 帳號申請-審核清單
        [HttpGet]
        [Route("List")]
        // GET: api/OperatorApply/List?is_checked=? (0=> 審查中, 1 =>通過, 2=>未通過)
        public IActionResult List(int is_checked)
        {
            try
            {
                return Ok(new ResultViewModel<List<ListRes>>
                {
                    isSuccess = true,
                    message = string.Empty,
                    Result = _service.Get_OperatorApply(is_checked),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<ListRes>>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null,
                });
            }
        }
        #endregion

        #region 帳號申請-確認審核
        [HttpPost]
        [Route("Check")]
        // POST: api/OperatorApply/Check
        public IActionResult Check([FromBody] CheckRep req)
        {
            try
            {
                string message = _service.Update_OperatorApply(req);

                return Ok(new ResultViewModel<string>
                {
                    isSuccess = true,
                    message = message,
                    Result = null,
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<string>
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
