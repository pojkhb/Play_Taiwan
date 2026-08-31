using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Models;
using backend.Services;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 地圖、節點導航與周邊探索相關 API。
    /// 對應頁面：地圖、周邊好去、任務。
    /// </summary>

    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class MapController : ControllerBase
    {
        private readonly ILogger<MapController> _logger;
        private readonly MapService _service;

        public MapController(
            ILogger<MapController> logger,
            MapService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 取得地圖

        /// <summary>
        /// 取得指定劇本的地圖資訊。
        /// </summary>
        /// <remarks>
        /// 對應「地圖」頁面，顯示地點探索度、已收集節點與路線。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Map/{story_id}
        /// </remarks>
        /// <param name="story_id">劇本代號。</param>
        /// <returns>地圖節點與探索進度資訊。</returns>
        // API：取得地圖（GetMap）－回傳指定劇本的地圖節點與探索進度
        [HttpGet("{story_id}")]
        public IActionResult GetMap(string story_id)
        {
            try
            {
                MapResponse result = _service.GetMap(story_id, User);

                return Ok(new ResultViewModel<MapResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (UnauthorizedAccessException e)
            {
                return Unauthorized(new ResultViewModel<MapResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "取得地圖失敗，story_id: {StoryId}", story_id);

                return BadRequest(new ResultViewModel<MapResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region GPS 確認抵達

        /// <summary>
        /// 使用 GPS 座標確認探員已抵達指定節點。
        /// </summary>
        /// <remarks>
        /// 對應「地圖」頁面點擊節點後的抵達解鎖流程，成功後解鎖該節點詳情。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Map/Node/{node_id}/Arrive?lat=25.0330&amp;lng=121.5654
        /// </remarks>
        /// <param name="node_id">節點代號。</param>
        /// <param name="lat">目前緯度。</param>
        /// <param name="lng">目前經度。</param>
        /// <returns>解鎖成功後的節點詳情。</returns>
        ///[❌]
        // API：GPS 確認抵達（Arrive）－驗證座標後解鎖指定節點
        [HttpPost("Node/{node_id}/Arrive")]
        public IActionResult Arrive(
            string node_id,
            [FromQuery] double lat,
            [FromQuery] double lng)
        {
            try
            {
                NodeDetailResponse result =
                    _service.ArriveNode(node_id, lat, lng, User);

                return Ok(new ResultViewModel<NodeDetailResponse>
                {
                    isSuccess = true,
                    message = "解鎖成功",
                    Result = result
                });
            }
            catch (UnauthorizedAccessException e)
            {
                return Unauthorized(new ResultViewModel<NodeDetailResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
            catch (InvalidOperationException e)
            {
                return BadRequest(new ResultViewModel<NodeDetailResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "節點抵達失敗，node_id: {NodeId}", node_id);

                return BadRequest(new ResultViewModel<NodeDetailResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 取得節點詳情

        /// <summary>
        /// 取得指定節點的詳細內容。
        /// </summary>
        /// <remarks>
        /// 對應「地圖」頁面節點彈窗（如臺南孔廟）的詳情內容。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Map/Node/{node_id}
        /// </remarks>
        /// <param name="node_id">節點代號。</param>
        /// <returns>節點詳細內容。</returns>
        // API：取得節點詳情（GetNode）－回傳指定節點的詳細內容
        [HttpGet("Node/{node_id}")]
        public IActionResult GetNode(string node_id)
        {
            try
            {
                NodeDetailResponse result =
                    _service.GetNodeDetail(node_id);

                return Ok(new ResultViewModel<NodeDetailResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "取得節點詳情失敗，node_id: {NodeId}", node_id);

                return BadRequest(new ResultViewModel<NodeDetailResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region NPC 隨機互動

        /// <summary>
        /// 取得指定節點的 NPC 隨機互動內容。
        /// </summary>
        /// <remarks>
        /// 對應「地圖」頁面節點彈窗中「回顧劇情」等 NPC 對話內容。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Map/Node/{node_id}/Interact
        /// </remarks>
        /// <param name="node_id">節點代號。</param>
        /// <returns>NPC 互動內容。</returns>
        // API：NPC 隨機互動（Interact）－回傳指定節點的 NPC 對話內容
        [HttpGet("Node/{node_id}/Interact")]
        public IActionResult Interact(string node_id)
        {
            try
            {
                NpcInteractionResponse result =
                    _service.GetNpcInteraction(node_id);

                return Ok(new ResultViewModel<NpcInteractionResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "NPC 互動失敗，node_id: {NodeId}", node_id);

                return BadRequest(new ResultViewModel<NpcInteractionResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 導航

        /// <summary>
        /// 取得前往指定節點的導航資訊。
        /// </summary>
        /// <remarks>
        /// 對應「地圖」頁面節點彈窗的「開始導航」按鈕。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Map/Navigate
        ///     {
        ///       "node_id": "N001"
        ///     }
        /// </remarks>
        /// <param name="req">導航請求，包含目標節點代號。</param>
        /// <returns>導航路線資訊。</returns>
        // API：導航（Navigate）－回傳前往指定節點的路線資訊
        [HttpPost("Navigate")]
        public IActionResult Navigate([FromBody] NavigationRequest req)
        {
            try
            {
                NavigationResponse result =
                    _service.GetNavigation(req);

                return Ok(new ResultViewModel<NavigationResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "導航失敗，node_id: {NodeId}", req?.node_id);

                return BadRequest(new ResultViewModel<NavigationResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 周邊好去

        /// <summary>
        /// 取得指定劇本周邊的推薦去處。
        /// </summary>
        /// <remarks>
        /// 對應「周邊好去」頁面，依「飲食」「其他」等分類回傳附近推薦地點。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Map/{story_id}/Nearby?category=飲食
        /// </remarks>
        /// <param name="story_id">劇本代號。</param>
        /// <param name="category">分類篩選條件（如：飲食、其他）。</param>
        /// <returns>周邊推薦地點清單。</returns>
        // API：周邊好去（Nearby）－回傳指定劇本周邊依分類篩選的推薦地點
        [HttpGet("{story_id}/Nearby")]
        public IActionResult Nearby(
            string story_id,
            [FromQuery] string category)
        {
            try
            {
                List<NearbyPlaceResponse> result =
                    _service.GetNearbyPlaces(story_id, category);

                return Ok(new ResultViewModel<List<NearbyPlaceResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "周邊好去查詢失敗，story_id: {StoryId}", story_id);

                return BadRequest(new ResultViewModel<List<NearbyPlaceResponse>>
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