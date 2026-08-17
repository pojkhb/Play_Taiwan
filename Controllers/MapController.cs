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