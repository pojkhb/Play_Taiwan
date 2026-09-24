// 檔案路徑：System\Controllers\Metro\MetroController.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 捷運 / 輕軌 API。資料來源：交通部 TDX，每週自動同步（appsettings.json 的 MetroSync 設定）。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MetroController : ControllerBase
    {
        private readonly ILogger<MetroController> _logger;
        private readonly MetroService _service;

        public MetroController(ILogger<MetroController> logger, MetroService service)
        {
            _logger = logger;
            _service = service;
        }


        /// <summary>
        /// 取得座標附近的捷運路線（含車站與線形），在地圖上畫捷運路網用。
        /// </summary>
        /// <remarks>
        /// 只要路線有任一車站在 radius_km 內就會回傳整條路線。
        ///
        /// **Request 範例**：
        ///
        ///     GET /api/Metro/Lines?lat=24.1635&amp;lng=120.6474&amp;radius_km=10
        /// </remarks>
        /// <param name="lat">緯度</param>
        /// <param name="lng">經度</param>
        /// <param name="radius_km">搜尋半徑（公里），預設 10，範圍 1 ~ 50</param>
        [HttpGet("Lines")]
        [ProducesResponseType(typeof(ResultViewModel<List<MetroLineItem>>), 200)]
        public async Task<IActionResult> Lines([FromQuery] double lat, [FromQuery] double lng, [FromQuery] double radius_km = 10)
        {
            try
            {
                var result = await _service.GetLinesNearAsync(lat, lng, Math.Clamp(radius_km, 1, 50));
                return Ok(new ResultViewModel<List<MetroLineItem>> { isSuccess = true, message = $"查詢成功，共 {result.Count} 條路線", Result = result });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢捷運路線失敗");
                return StatusCode(500, new ResultViewModel<List<MetroLineItem>> { isSuccess = false, message = e.Message });
            }
        }


        /// <summary>
        /// 【前端不用接】（管理員）從 TDX 同步一個捷運系統。
        /// </summary>
        /// <remarks>
        /// **前端不用接**：系統每週會自動同步，這支是管理員手動補跑用。
        ///
        /// system：TRTC=臺北捷運、NTMC=新北捷運、TYMC=桃園機場捷運、TMRT=臺中捷運、
        /// KRTC=高雄捷運、KLRT=高雄輕軌、NTDLRT=淡海輕軌、NTALRT=安坑輕軌
        ///
        ///     POST /api/Metro/Sync/TMRT
        /// </remarks>
        [Authorize(Roles = "Admin")]
        [HttpPost("Sync/{system}")]
        [ProducesResponseType(typeof(ResultViewModel<MetroSyncResult>), 200)]
        public async Task<IActionResult> Sync(string system)
        {
            try
            {
                var result = await _service.SyncAsync(system);
                return Ok(new ResultViewModel<MetroSyncResult> { isSuccess = true, message = "同步完成", Result = result });
            }
            catch (ArgumentException e)
            {
                return BadRequest(new ResultViewModel<MetroSyncResult> { isSuccess = false, message = e.Message });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "同步捷運失敗");
                return StatusCode(500, new ResultViewModel<MetroSyncResult> { isSuccess = false, message = e.Message });
            }
        }
    }
}
