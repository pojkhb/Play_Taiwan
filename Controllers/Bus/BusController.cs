// 檔案路徑：System\Controllers\Bus\BusController.cs
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
    /// 公車 / 台灣好行 API。
    /// 資料來源：交通部 TDX 運輸資料流通服務，每天自動同步（appsettings.json 的 BusSync 設定）。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BusController : ControllerBase
    {
        private readonly ILogger<BusController> _logger;
        private readonly BusService _service;

        public BusController(ILogger<BusController> logger, BusService service)
        {
            _logger = logger;
            _service = service;
        }


        #region 查詢

        /// <summary>
        /// 查詢座標附近的公車站牌，以及每個站牌經過的路線（含台灣好行）。
        /// </summary>
        /// <remarks>
        /// **Request 範例**：
        ///
        ///     GET /api/Bus/Nearby?lat=24.1477&amp;lng=120.6736&amp;radius=500
        /// </remarks>
        /// <param name="lat">緯度</param>
        /// <param name="lng">經度</param>
        /// <param name="radius">搜尋半徑（公尺），預設 500，範圍 50 ~ 2000</param>
        [HttpGet("Nearby")]
        [ProducesResponseType(typeof(ResultViewModel<List<BusStopNearbyItem>>), 200)]
        public async Task<IActionResult> Nearby([FromQuery] double lat, [FromQuery] double lng, [FromQuery] int radius = 500)
        {
            try
            {
                var result = await _service.GetNearbyStopsAsync(lat, lng, radius);
                return Ok(new ResultViewModel<List<BusStopNearbyItem>> { isSuccess = true, message = $"查詢成功，共 {result.Count} 個站牌", Result = result });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢附近站牌失敗");
                return StatusCode(500, new ResultViewModel<List<BusStopNearbyItem>> { isSuccess = false, message = e.Message });
            }
        }


        /// <summary>
        /// 取得公車路線詳情：去程/返程的完整站牌、路線線形、起站發車時間。
        /// </summary>
        /// <remarks>
        /// br_id 可從 Nearby、TaiwanTrip 或劇本的公車方案取得。
        ///
        /// **Request 範例**：
        ///
        ///     GET /api/Bus/Route/12
        /// </remarks>
        [HttpGet("Route/{br_id:int}")]
        [ProducesResponseType(typeof(ResultViewModel<BusRouteDetail>), 200)]
        public async Task<IActionResult> RouteDetail(int br_id)
        {
            try
            {
                var result = await _service.GetRouteDetailAsync(br_id);
                if (result == null)
                    return NotFound(new ResultViewModel<BusRouteDetail> { isSuccess = false, message = "查無此路線" });
                return Ok(new ResultViewModel<BusRouteDetail> { isSuccess = true, message = "查詢成功", Result = result });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢公車路線失敗");
                return StatusCode(500, new ResultViewModel<BusRouteDetail> { isSuccess = false, message = e.Message });
            }
        }


        /// <summary>
        /// 取得全台營運中的台灣好行路線列表。
        /// </summary>
        /// <remarks>
        /// **Request 範例**：
        ///
        ///     GET /api/Bus/TaiwanTrip
        /// </remarks>
        [HttpGet("TaiwanTrip")]
        [ProducesResponseType(typeof(ResultViewModel<List<TaiwanTripperRouteItem>>), 200)]
        public async Task<IActionResult> TaiwanTrip()
        {
            try
            {
                var result = await _service.GetTaiwanTripRoutesAsync();
                return Ok(new ResultViewModel<List<TaiwanTripperRouteItem>> { isSuccess = true, message = $"查詢成功，共 {result.Count} 條好行路線", Result = result });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢台灣好行路線失敗");
                return StatusCode(500, new ResultViewModel<List<TaiwanTripperRouteItem>> { isSuccess = false, message = e.Message });
            }
        }


        /// <summary>
        /// 查詢站牌的即時到站時間（每條路線還有幾分鐘到）。
        /// </summary>
        /// <remarks>
        /// 直接查詢 TDX 即時資料，不存資料庫，前端要顯示時再呼叫即可（建議 30 秒以上更新一次）。
        ///
        /// **Request 範例**：
        ///
        ///     GET /api/Bus/Stop/123/Arrivals
        /// </remarks>
        [HttpGet("Stop/{bs_id:int}/Arrivals")]
        [ProducesResponseType(typeof(ResultViewModel<List<BusArrivalItem>>), 200)]
        public async Task<IActionResult> Arrivals(int bs_id)
        {
            try
            {
                var result = await _service.GetArrivalsAsync(bs_id);
                if (result == null)
                    return NotFound(new ResultViewModel<List<BusArrivalItem>> { isSuccess = false, message = "查無此站牌" });
                return Ok(new ResultViewModel<List<BusArrivalItem>> { isSuccess = true, message = "查詢成功", Result = result });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢即時到站失敗");
                return StatusCode(500, new ResultViewModel<List<BusArrivalItem>> { isSuccess = false, message = e.Message });
            }
        }

        #endregion


        #region 同步（管理員）

        /// <summary>
        /// 【前端不用接】（管理員）從 TDX 同步某縣市的市區公車。
        /// </summary>
        /// <remarks>
        /// **前端不用接**：系統每天會自動同步，這支是管理員手動補跑用，約需 1 分鐘。
        ///
        /// city 可傳中文（臺中市、台中市）或 TDX 參數（Taichung）。
        ///
        ///     POST /api/Bus/Sync/City/臺中市
        /// </remarks>
        [Authorize(Roles = "Admin")]
        [HttpPost("Sync/City/{city}")]
        [ProducesResponseType(typeof(ResultViewModel<BusSyncResult>), 200)]
        public async Task<IActionResult> SyncCity(string city)
        {
            try
            {
                var result = await _service.SyncCityAsync(city);
                return Ok(new ResultViewModel<BusSyncResult> { isSuccess = true, message = "同步完成", Result = result });
            }
            catch (ArgumentException e)
            {
                return BadRequest(new ResultViewModel<BusSyncResult> { isSuccess = false, message = e.Message });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "同步市區公車失敗");
                return StatusCode(500, new ResultViewModel<BusSyncResult> { isSuccess = false, message = e.Message });
            }
        }


        /// <summary>
        /// 【前端不用接】（管理員）從 TDX 同步全台台灣好行。
        /// </summary>
        /// <remarks>
        /// **前端不用接**：系統每天會自動同步，這支是管理員手動補跑用。
        ///
        ///     POST /api/Bus/Sync/TaiwanTrip
        /// </remarks>
        [Authorize(Roles = "Admin")]
        [HttpPost("Sync/TaiwanTrip")]
        [ProducesResponseType(typeof(ResultViewModel<BusSyncResult>), 200)]
        public async Task<IActionResult> SyncTaiwanTrip()
        {
            try
            {
                var result = await _service.SyncTaiwanTripAsync();
                return Ok(new ResultViewModel<BusSyncResult> { isSuccess = true, message = "同步完成", Result = result });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "同步台灣好行失敗");
                return StatusCode(500, new ResultViewModel<BusSyncResult> { isSuccess = false, message = e.Message });
            }
        }


        /// <summary>
        /// 【前端不用接】（管理員）重算景點附近的公車站牌（生成劇本規劃公車用）。
        /// </summary>
        /// <remarks>
        /// **前端不用接**：系統每天同步完會自動重算。
        ///
        /// scope 傳縣市（臺中市）只算該縣市站牌；傳「台灣好行」只算好行沿線站牌。
        /// 需要 AI service（Neo4j 景點資料）可以連線；步行距離優先用 Valhalla 計算。
        ///
        ///     POST /api/Bus/PlaceStops/Build?scope=臺中市
        /// </remarks>
        [Authorize(Roles = "Admin")]
        [HttpPost("PlaceStops/Build")]
        [ProducesResponseType(typeof(ResultViewModel<PlaceBusStopBuildResult>), 200)]
        public async Task<IActionResult> BuildPlaceStops([FromQuery] string scope)
        {
            try
            {
                var result = await _service.BuildPlaceBusStopsAsync(scope);
                return Ok(new ResultViewModel<PlaceBusStopBuildResult> { isSuccess = true, message = "計算完成", Result = result });
            }
            catch (ArgumentException e)
            {
                return BadRequest(new ResultViewModel<PlaceBusStopBuildResult> { isSuccess = false, message = e.Message });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "計算景點附近站牌失敗");
                return StatusCode(500, new ResultViewModel<PlaceBusStopBuildResult> { isSuccess = false, message = e.Message });
            }
        }

        #endregion
    }
}
