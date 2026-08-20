using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 明信片主檔相關 API。
    /// 對應頁面：收藏館。負責「明信片長什麼樣子」，與紀錄探員實際獲得情況的 PostcardController 互補。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PostcardCatalogController : ControllerBase
    {
        private readonly ILogger<PostcardCatalogController> _logger;
        private readonly PostcardCatalogService _service;

        public PostcardCatalogController(ILogger<PostcardCatalogController> logger, PostcardCatalogService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 取得所有明信片主檔

        /// <summary>
        /// 取得所有明信片主檔清單。
        /// </summary>
        [HttpGet]
        [Route("")]
        public async Task<IActionResult> GetAll([FromQuery] string category = null)
        {
            try
            {
                var result = await _service.GetAllAsync(category);
                return Ok(new ResultViewModel<List<PostcardCatalogResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result,
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "取得明信片清單時發生錯誤");
                return StatusCode(500, new ResultViewModel<List<PostcardCatalogResponse>> 
                { 
                    isSuccess = false, 
                    message = "系統發生未預期錯誤", 
                    Result = null 
                });
            }
        }

        #endregion

        #region 依識別碼取得單一明信片主檔

        /// <summary>
        /// 依明信片識別碼取得單一明信片主檔資料。
        /// </summary>
        [HttpGet]
        [Route("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            try
            {
                var result = await _service.GetByIdAsync(id);
                if (result == null)
                {
                    return NotFound(new ResultViewModel<PostcardCatalogResponse> 
                    { 
                        isSuccess = false, 
                        message = "查無此資料", 
                        Result = null 
                    });
                }

                return Ok(new ResultViewModel<PostcardCatalogResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result,
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"取得明信片 {id} 時發生錯誤");
                return StatusCode(500, new ResultViewModel<PostcardCatalogResponse> 
                { 
                    isSuccess = false, 
                    message = "系統發生未預期錯誤", 
                    Result = null 
                });
            }
        }

        #endregion

        #region 依劇本取得明信片主檔

        /// <summary>
        /// 取得指定劇本所擁有的所有明信片主檔。
        /// </summary>
        [HttpGet]
        [Route("by-story/{storyId}")]
        public async Task<IActionResult> GetByStoryId(string storyId)
        {
            try
            {
                var result = await _service.GetByStoryIdAsync(storyId);
                return Ok(new ResultViewModel<List<PostcardCatalogResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result,
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"取得劇本 {storyId} 明信片清單時發生錯誤");
                return StatusCode(500, new ResultViewModel<List<PostcardCatalogResponse>> 
                { 
                    isSuccess = false, 
                    message = "系統發生未預期錯誤", 
                    Result = null 
                });
            }
        }

        #endregion

        #region 新增明信片主檔

        /// <summary>
        /// 新增明信片主檔。
        /// </summary>
        [HttpPost]
        [Route("")]
        public async Task<IActionResult> Create([FromBody] PostcardCatalogRequest request)
        {
            try
            {
                await _service.CreateAsync(request);
                return Ok(new ResultViewModel<string> 
                { 
                    isSuccess = true, 
                    message = "新增成功", 
                    Result = request.PostcardId 
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "新增明信片時發生錯誤");
                return StatusCode(500, new ResultViewModel<string> 
                { 
                    isSuccess = false, 
                    message = "系統發生未預期錯誤", 
                    Result = null 
                });
            }
        }

        #endregion

        #region 更新明信片主檔

        /// <summary>
        /// 更新明信片主檔。
        /// </summary>
        [HttpPost]
        [Route("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] PostcardCatalogRequest request)
        {
            try
            {
                var success = await _service.UpdateAsync(id, request);
                if (!success)
                {
                    return NotFound(new ResultViewModel<bool> 
                    { 
                        isSuccess = false, 
                        message = "查無此資料", 
                        Result = false 
                    });
                }

                return Ok(new ResultViewModel<bool> 
                { 
                    isSuccess = true, 
                    message = "更新成功", 
                    Result = true 
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"更新明信片 {id} 時發生錯誤");
                return StatusCode(500, new ResultViewModel<bool> 
                { 
                    isSuccess = false, 
                    message = "系統發生未預期錯誤", 
                    Result = false 
                });
            }
        }

        #endregion

        #region 刪除明信片主檔

        /// <summary>
        /// 刪除明信片主檔。
        /// </summary>
        [HttpPost]
        [Route("{id}/Delete")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                var success = await _service.DeleteAsync(id);
                if (!success)
                {
                    return NotFound(new ResultViewModel<bool> 
                    { 
                        isSuccess = false, 
                        message = "查無此資料", 
                        Result = false 
                    });
                }

                return Ok(new ResultViewModel<bool> 
                { 
                    isSuccess = true, 
                    message = "刪除成功", 
                    Result = true 
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"刪除明信片 {id} 時發生錯誤");
                return StatusCode(500, new ResultViewModel<bool> 
                { 
                    isSuccess = false, 
                    message = "系統發生未預期錯誤", 
                    Result = false 
                });
            }
        }

        #endregion
    }
}