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
    // 下拉選單 Dropdown
    public class DropdownController : ControllerBase
    {
        private readonly ILogger<DropdownController> _logger;
        private readonly SharedFunctionService _service;

        public DropdownController(ILogger<DropdownController> logger, SharedFunctionService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 下拉選單-所有角色
        [HttpGet]
        [Route("Role")]
        // GET: api/Dropdown/Role
        public IActionResult Role()
        {
            try
            {
                return Ok(new ResultViewModel<List<RoleDropdownResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.Get_DropdownRole(),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<RoleDropdownResponse>>
                {
                    isSuccess = false,
                    message = e.Message.ToString(),
                    Result = null,
                });
            }
        }
        #endregion

        #region 下拉選單-所有功能權限
        [HttpGet]
        [Route("Module")]
        // GET: api/Dropdown/Module
        public IActionResult Module()
        {
            try
            {
                return Ok(new ResultViewModel<List<ModuleDropdownResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.Get_DropdownModule(),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<ModuleDropdownResponse>>
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
