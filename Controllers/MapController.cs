using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // 地圖 / 節點 / 導航 (對應畫面: 地圖, 遊戲劇情, 任務)
    public class MapController : ControllerBase
    {
        private readonly ILogger<MapController> _logger;
        private readonly MapService _service;

        public MapController(ILogger<MapController> logger, MapService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 取得地圖(探索進度/節點/雲霧)
        [HttpGet]
        [Route("{story_id}")]
        // GET: api/Map/{story_id}
        public IActionResult GetMap(string story_id)
        {
            try
            {
                return Ok(new ResultViewModel<MapResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetMap(story_id),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<MapResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion

        #region GPS 確認抵達 解鎖節點
        [HttpPost]
        [Route("Node/{node_id}/Arrive")]
        // POST: api/Map/Node/{node_id}/Arrive
        public IActionResult Arrive(string node_id, [FromQuery] double lat, [FromQuery] double lng)
        {
            try
            {
                return Ok(new ResultViewModel<NodeDetailResponse>
                {
                    isSuccess = true,
                    message = "解鎖成功",
                    Result = _service.ArriveNode(node_id, lat, lng),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<NodeDetailResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion

        #region 取得節點詳情
        [HttpGet]
        [Route("Node/{node_id}")]
        // GET: api/Map/Node/{node_id}
        public IActionResult GetNode(string node_id)
        {
            try
            {
                return Ok(new ResultViewModel<NodeDetailResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetNodeDetail(node_id),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<NodeDetailResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion

        #region NPC 隨機互動
        [HttpGet]
        [Route("Node/{node_id}/Interact")]
        // GET: api/Map/Node/{node_id}/Interact
        public IActionResult Interact(string node_id)
        {
            try
            {
                return Ok(new ResultViewModel<NpcInteractionResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetNpcInteraction(node_id),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<NpcInteractionResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion

        #region 導航到景點 (呼叫 Google Maps)
        [HttpPost]
        [Route("Navigate")]
        // POST: api/Map/Navigate
        public IActionResult Navigate([FromBody] NavigationRequest req)
        {
            try
            {
                return Ok(new ResultViewModel<NavigationResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetNavigation(req),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<NavigationResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion

        #region 周邊好去
        [HttpGet]
        [Route("{story_id}/Nearby")]
        // GET: api/Map/{story_id}/Nearby
        public IActionResult Nearby(string story_id, [FromQuery] string category)
        {
            try
            {
                return Ok(new ResultViewModel<System.Collections.Generic.List<NearbyPlaceResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetNearbyPlaces(story_id, category),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<System.Collections.Generic.List<NearbyPlaceResponse>> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }
        #endregion
    }
}