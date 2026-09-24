// 檔案路徑：System\Controllers\Route\RouteController.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.utils;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 劇本交通路線：步行、腳踏車、機車、汽車、公車、捷運合併規劃，含去程與返程。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class RouteController : ControllerBase
    {
        private readonly ILogger<RouteController> _logger;
        private readonly RoutePlanService _service;

        public RouteController(ILogger<RouteController> logger, RoutePlanService service)
        {
            _logger = logger;
            _service = service;
        }


        /// <summary>
        /// 查詢各交通方式在這一帶能不能用（前端用來決定勾選框要不要反灰）。
        /// </summary>
        /// <remarks>
        /// 步行、腳踏車、機車、汽車一律可用。
        /// 公車：起點或任一節點 600 公尺內有公車站牌（且已匯入該縣市公車資料）才可用。
        /// 捷運：至少兩個地點步行 15 分鐘（1 公里）內有捷運站才可用；只查單一座標時該點附近有站即可。
        ///
        /// 帶 story_id 時會一併檢查劇本所有節點；lat/lng 是使用者目前位置（起點），可不帶。
        ///
        /// **Request 範例**：
        ///
        ///     GET /api/Route/Availability?lat=24.1372&amp;lng=120.6869&amp;story_id=12
        /// </remarks>
        /// <param name="lat">起點緯度（可不帶）</param>
        /// <param name="lng">起點經度（可不帶）</param>
        /// <param name="story_id">劇本代號（可不帶）</param>
        [HttpGet("Availability")]
        [ProducesResponseType(typeof(ResultViewModel<List<TransportAvailability>>), 200)]
        public async Task<IActionResult> Availability([FromQuery] double lat, [FromQuery] double lng, [FromQuery] int? story_id)
        {
            try
            {
                if ((lat == 0 || lng == 0) && !story_id.HasValue)
                    return BadRequest(new ResultViewModel<List<TransportAvailability>> { isSuccess = false, message = "請提供 lat/lng 或 story_id" });

                var result = await _service.GetAvailabilityAsync(lat, lng, story_id);
                return Ok(new ResultViewModel<List<TransportAvailability>> { isSuccess = true, message = "查詢成功", Result = result });
            }
            catch (ArgumentException e)
            {
                return BadRequest(new ResultViewModel<List<TransportAvailability>> { isSuccess = false, message = e.Message });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢交通方式可用性失敗");
                return StatusCode(500, new ResultViewModel<List<TransportAvailability>> { isSuccess = false, message = e.Message });
            }
        }


        /// <summary>
        /// 查詢各交通方式在這些地點能不能用（自選地點、沒有劇本時用）。
        /// </summary>
        /// <remarks>
        /// 規則同 GET /api/Route/Availability，body 放起點與要去的所有點。
        ///
        /// **Request 範例**：
        /// ```json
        /// [
        ///   { "lat": 24.1372, "lng": 120.6869, "name": "臺中車站" },
        ///   { "lat": 24.1386, "lng": 120.6784, "name": "臺中州廳" }
        /// ]
        /// ```
        /// </remarks>
        [HttpPost("Availability")]
        [ProducesResponseType(typeof(ResultViewModel<List<TransportAvailability>>), 200)]
        public async Task<IActionResult> AvailabilityForPoints([FromBody] List<RoutePoint> points)
        {
            try
            {
                if (points == null || points.Count == 0)
                    return BadRequest(new ResultViewModel<List<TransportAvailability>> { isSuccess = false, message = "請提供至少 1 個地點" });

                var result = await _service.GetAvailabilityAsync(points);
                return Ok(new ResultViewModel<List<TransportAvailability>> { isSuccess = true, message = "查詢成功", Result = result });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢交通方式可用性失敗");
                return StatusCode(500, new ResultViewModel<List<TransportAvailability>> { isSuccess = false, message = e.Message });
            }
        }


        /// <summary>
        /// 規劃劇本交通路線：起點 → 依序經過每個節點（去程）→ 回到起點（返程）。
        /// </summary>
        /// <remarks>
        /// 每一段會把勾選的交通方式都算一次，採用最快的方案（公車/捷運含步行到站與候車時間），
        /// 其他方案放在 options 讓使用者切換。
        ///
        /// - 步行、腳踏車、機車、汽車：實際道路路線（腳踏車優先走自行車道）
        /// - 公車：起訖點附近站牌之間的直達路線，含上下車站、經過的站牌
        /// - 捷運：可轉乘，含上下車站、轉乘站、經過的車站，線條用該路線代表色
        ///
        /// segments 的 coordinates 已依行進方向排序，前端沿線畫箭頭即可表示方向；
        /// direction 是「去程」或「返程」。在這一帶不能用的公車/捷運會自動略過並放進 warnings。
        ///
        /// **Request 範例（劇本）**：
        /// ```json
        /// {
        ///   "story_id": 12,
        ///   "start": { "lat": 24.1372, "lng": 120.6869, "name": "臺中車站" },
        ///   "transportation": ["步行", "腳踏車", "公車", "捷運"],
        ///   "include_return": true
        /// }
        /// ```
        ///
        /// **Request 範例（自選地點）**：
        /// ```json
        /// {
        ///   "start": { "lat": 24.1372, "lng": 120.6869, "name": "臺中車站" },
        ///   "points": [
        ///     { "lat": 24.1386, "lng": 120.6784, "name": "臺中州廳" },
        ///     { "lat": 24.1415, "lng": 120.6633, "name": "國立臺灣美術館" }
        ///   ],
        ///   "transportation": ["步行", "公車"]
        /// }
        /// ```
        /// </remarks>
        [HttpPost("Plan")]
        [ProducesResponseType(typeof(ResultViewModel<RoutePlanResponse>), 200)]
        public async Task<IActionResult> Plan([FromBody] RoutePlanRequest req)
        {
            try
            {
                var result = await _service.PlanAsync(req);
                return Ok(new ResultViewModel<RoutePlanResponse>
                {
                    isSuccess = true,
                    message = $"規劃完成：{result.legs.Count} 段，去程約 {Math.Ceiling(result.outbound_minutes)} 分、返程約 {Math.Ceiling(result.return_minutes)} 分",
                    Result = result
                });
            }
            catch (ArgumentException e)
            {
                return BadRequest(new ResultViewModel<RoutePlanResponse> { isSuccess = false, message = e.Message });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "劇本交通路線規劃失敗");
                return StatusCode(500, new ResultViewModel<RoutePlanResponse> { isSuccess = false, message = e.Message });
            }
        }


        /// <summary>
        /// 取得可以規劃交通路線的劇本（含節點座標與建議起點）。
        /// </summary>
        /// <remarks>
        /// 回傳自己的劇本（新的在前）；自己還沒有劇本時回傳最近的劇本，方便用測試帳號展示。
        ///
        /// **Request 範例**：
        ///
        ///     GET /api/Route/Stories
        /// </remarks>
        [HttpGet("Stories")]
        [ProducesResponseType(typeof(ResultViewModel<List<RouteStoryItem>>), 200)]
        public async Task<IActionResult> Stories()
        {
            try
            {
                var result = await _service.GetStoriesAsync(User.GetAuId());
                return Ok(new ResultViewModel<List<RouteStoryItem>> { isSuccess = true, message = $"查詢成功，共 {result.Count} 個劇本", Result = result });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢劇本清單失敗");
                return StatusCode(500, new ResultViewModel<List<RouteStoryItem>> { isSuccess = false, message = e.Message });
            }
        }
    }
}
