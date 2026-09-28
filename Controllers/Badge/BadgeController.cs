using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.utils;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 徽章相關 API（整併版）。
    /// 對應頁面：首頁總覽（徽章數）、過往紀錄、徽章圖鑑、收藏（徽章部分）。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class BadgeController : ControllerBase
    {
        private readonly ILogger<BadgeController> _logger;
        private readonly BadgeService _service;

        public BadgeController(ILogger<BadgeController> logger, BadgeService service)
        {
            _logger = logger;
            _service = service;
        }

        #region 取得徽章圖鑑（依系列分組 + 是否擁有）

        /// <summary>
        /// 取得系統所有徽章，依系列分組，並標示當前探員是否已擁有該徽章。
        /// </summary>
        /// <remarks>
        /// 整併原本 /api/Badge、/api/Favorite（徽章部分）、/api/Badge/Status 三支重複邏輯，
        /// 統一保留這一支，回傳格式依系列巢狀分組。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Badge/Status
        /// </remarks>
        [Authorize]
        [HttpGet]
        [Route("Status")]
        [ProducesResponseType(typeof(ResultViewModel<List<BadgeSeriesGroup>>), 200)]
        public IActionResult GetBadgeCatalog()
        {
            try
            {
                return Ok(new ResultViewModel<List<BadgeSeriesGroup>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetBadgeCatalog(User.GetAuId()),
                });
            }
            catch (Exception e)
            {
                return NotFound(new ResultViewModel<List<BadgeSeriesGroup>> { isSuccess = false, message = e.Message.ToString(), Result = null });
            }
        }

        #endregion

        #region 完成劇本後抽勳章

        /// <summary>
        /// 完成劇本後抽一枚勳章，一個劇本只能抽一次。
        /// </summary>
        /// <remarks>
        /// 可抽的類別依劇本內容決定，生成劇本時就會寫在 story_badge（劇本卡片「預計獲得」）：
        /// - 島嶼城市：劇本所在縣市的那一枚
        /// - 台灣印記：路線經過的地標（台北101、赤崁樓、日月潭…）
        /// - 台灣味：有地方美食型任務、餐廳、夜市/市場，或偏好選了「美食」
        /// - 島嶼生靈：經過動物園、國家公園、森林、步道、濕地等
        /// - 老台灣：經過老街、市場、眷村、古厝、故居等
        /// - 午夜台灣：只有夜間劇本能抽
        ///
        /// 先平均抽類別、再從類別裡抽一枚，已擁有的不會再抽到。
        /// 重複呼叫會回傳當初抽到的那枚（is_new = false）；可抽的都已收集時 badge 為 null。
        ///
        ///     POST /api/Badge/Draw
        ///     { "story_id": 1 }
        /// </remarks>
        [Authorize]
        [HttpPost]
        [Route("Draw")]
        [ProducesResponseType(typeof(ResultViewModel<BadgeDrawResponse>), 200)]
        public async Task<IActionResult> Draw([FromBody] BadgeDrawRequest request)
        {
            try
            {
                BadgeDrawResponse result = await _service.DrawAsync(User.GetAuId(), request.story_id);
                return Ok(new ResultViewModel<BadgeDrawResponse>
                {
                    isSuccess = true,
                    message = result.is_new ? "抽到新勳章！"
                            : result.badge != null ? "這個劇本已經抽過勳章"
                            : "這個劇本可抽的勳章都已收集",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "抽勳章失敗（story_id={StoryId}）", request?.story_id);
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion
    }
}