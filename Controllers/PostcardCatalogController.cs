using System;
using System.Collections.Generic;
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
    // 明信片主檔
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
        /// <remarks>
        /// 對應「過往－收藏館」頁面的明信片清單，可依系列分類篩選。
        ///
        /// Request 範例：
        ///
        ///     GET /api/PostcardCatalog?category=台灣古籍系列
        /// </remarks>
        /// <param name="category">系列分類，選填。</param>
        /// <returns>明信片主檔清單。</returns>
        // API：取得所有明信片主檔（GetAll）－回傳明信片主檔清單
        [HttpGet]
        [Route("")]
        // GET: api/PostcardCatalog
        public IActionResult GetAll([FromQuery] string category = null)
        {
            try
            {
                return Ok(new ResultViewModel<List<PostcardCatalogResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetAll(category),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<PostcardCatalogResponse>> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 依識別碼取得單一明信片主檔

        /// <summary>
        /// 依明信片識別碼取得單一明信片主檔資料。
        /// </summary>
        /// <remarks>
        /// Request 範例：
        ///
        ///     GET /api/PostcardCatalog/postcard_001
        /// </remarks>
        /// <param name="id">明信片識別碼 (postcard_id)。</param>
        /// <returns>明信片主檔資料。</returns>
        // API：依識別碼取得單一明信片主檔（GetById）－回傳明信片主檔資料
        [HttpGet]
        [Route("{id}")]
        // GET: api/PostcardCatalog/{id}
        public IActionResult GetById(string id)
        {
            try
            {
                var result = _service.GetById(id);
                return Ok(new ResultViewModel<PostcardCatalogResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result,
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<PostcardCatalogResponse> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 依劇本取得明信片主檔

        /// <summary>
        /// 取得指定劇本所擁有的所有明信片主檔。
        /// </summary>
        /// <remarks>
        /// Request 範例：
        ///
        ///     GET /api/PostcardCatalog/by-story/story_tainan_001
        /// </remarks>
        /// <param name="storyId">劇本識別碼 (story_id)。</param>
        /// <returns>該劇本可獲得的明信片主檔清單。</returns>
        // API：依劇本取得明信片主檔（GetByStoryId）－回傳明信片主檔清單
        [HttpGet]
        [Route("by-story/{storyId}")]
        // GET: api/PostcardCatalog/by-story/{storyId}
        public IActionResult GetByStoryId(string storyId)
        {
            try
            {
                return Ok(new ResultViewModel<List<PostcardCatalogResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetByStoryId(storyId),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<PostcardCatalogResponse>> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 新增明信片主檔

        /// <summary>
        /// 新增明信片主檔。
        /// </summary>
        /// <remarks>
        /// Request 範例：
        ///
        ///     POST /api/PostcardCatalog
        /// </remarks>
        /// <param name="request">欲新增的明信片主檔資料。</param>
        /// <returns>新增結果。</returns>
        // API：新增明信片主檔（Create）－寫入一筆明信片主檔
        [HttpPost]
        [Route("")]
        // POST: api/PostcardCatalog
        public IActionResult Create([FromBody] PostcardCatalogRequest request)
        {
            try
            {
                _service.Create(request);
                return Ok(new ResultViewModel<string> { isSuccess = true, message = "新增成功", Result = request.PostcardId });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<string> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 更新明信片主檔

        /// <summary>
        /// 更新明信片主檔。
        /// </summary>
        /// <remarks>
        /// Request 範例：
        ///
        ///     POST /api/PostcardCatalog/postcard_001
        /// </remarks>
        /// <param name="id">明信片識別碼 (postcard_id)。</param>
        /// <param name="request">欲更新的內容。</param>
        /// <returns>更新結果。</returns>
        // API：更新明信片主檔（Update）－修改一筆明信片主檔（用 POST 取代 PUT）
        [HttpPost]
        [Route("{id}")]
        // POST: api/PostcardCatalog/{id}
        public IActionResult Update(string id, [FromBody] PostcardCatalogRequest request)
        {
            try
            {
                var success = _service.Update(id, request);
                return Ok(new ResultViewModel<bool> { isSuccess = success, message = success ? "更新成功" : "查無此資料", Result = success });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<bool> { isSuccess = false, message = e.Message.ToString(), Result = false });
            }
        }

        #endregion

        #region 刪除明信片主檔

        /// <summary>
        /// 刪除明信片主檔。
        /// </summary>
        /// <remarks>
        /// Request 範例：
        ///
        ///     POST /api/PostcardCatalog/postcard_001/Delete
        /// </remarks>
        /// <param name="id">明信片識別碼 (postcard_id)。</param>
        /// <returns>刪除結果。</returns>
        // API：刪除明信片主檔（Delete）－用 POST 取代 DELETE
        [HttpPost]
        [Route("{id}/Delete")]
        // POST: api/PostcardCatalog/{id}/Delete
        public IActionResult Delete(string id)
        {
            try
            {
                var success = _service.Delete(id);
                return Ok(new ResultViewModel<bool> { isSuccess = success, message = success ? "刪除成功" : "查無此資料", Result = success });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<bool> { isSuccess = false, message = e.Message.ToString(), Result = false });
            }
        }

        #endregion
    }
}