using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 客群分析與 AI 商家經營策略。
    /// <list type="bullet">
    /// <item>GET /api/VisitorAnalysis — MySQL visitor_analysis 儀表板全量</item>
    /// <item>GET /api/VisitorAnalysis/Attractions — Neo4j 景點清單（經 FastAPI）</item>
    /// <item>POST /api/VisitorAnalysis/Strategy/Chat — AI 經營策略對話（經 FastAPI）</item>
    /// </list>
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class VisitorAnalysisController : ControllerBase
    {
        private readonly ILogger<VisitorAnalysisController> _logger;
        private readonly VisitorAnalysisService _service;

        public VisitorAnalysisController(
            ILogger<VisitorAnalysisController> logger,
            VisitorAnalysisService service
        )
        {
            _logger = logger;
            _service = service;
        }

        #region 客群分析儀表板（MySQL）

        /// <summary>
        /// 取得客群分析儀表板所需的全部資料。
        /// </summary>
        /// <remarks>
        /// 對應客群分析儀表板：一次回傳 visitor_analysis 裡所有景點，以及各自的國內／來臺分析。
        ///
        /// Request 範例：
        ///
        ///     GET /api/VisitorAnalysis
        /// </remarks>
        [AllowAnonymous]
        [HttpGet]
        [Route("")]
        public IActionResult GetDashboard()
        {
            try
            {
                return Ok(new ResultViewModel<VisitorAnalysisDashboardResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetDashboard()
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "客群分析儀表板查詢失敗");

                return StatusCode(500, new ResultViewModel<VisitorAnalysisDashboardResponse>
                {
                    isSuccess = false,
                    message = "客群分析儀表板查詢失敗：" + e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 景點清單（Neo4j via FastAPI）

        /// <summary>
        /// 取得景點清單（名稱關鍵字搜尋、分頁；同名同地址去重，保留 uids）。
        /// </summary>
        /// <remarks>
        /// Request 範例：
        ///
        ///     GET /api/VisitorAnalysis/Attractions?keyword=勤美&amp;page=1&amp;pageSize=20
        /// </remarks>
        [AllowAnonymous]
        [HttpGet("Attractions")]
        public async Task<IActionResult> GetAttractions(
            [FromQuery] string keyword = "",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                VisitorAttractionListResponse result =
                    await _service.GetAttractionsAsync(keyword, page, pageSize);

                return Ok(new ResultViewModel<VisitorAttractionListResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = result
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "景點清單查詢失敗");

                return StatusCode(500, new ResultViewModel<VisitorAttractionListResponse>
                {
                    isSuccess = false,
                    message = "景點清單查詢失敗：" + e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region AI 商家經營策略對話

        /// <summary>
        /// AI 商家經營策略對話（不儲存聊天紀錄）。
        /// 前端只送 attraction_uid + question；system prompt／RAG／客群由 FastAPI 組裝後呼叫 Ollama。
        /// </summary>
        /// <remarks>
        /// LLM 未就緒時 Result.status = not_ready；找不到景點 = attraction_not_found。
        /// 成功時 Result.reply 為聊天回覆文字。RAG 未就緒仍可僅依客群回答。
        ///
        /// Request 範例：
        ///
        ///     POST /api/VisitorAnalysis/Strategy/Chat
        ///     {
        ///       "attraction_uid": "...",
        ///       "question": "如何吸引 30-39 歲客群？"
        ///     }
        /// </remarks>
        [AllowAnonymous]
        [HttpPost("Strategy/Chat")]
        public async Task<IActionResult> StrategyChat(
            [FromBody] VisitorStrategyChatRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.attraction_uid))
                {
                    return BadRequest(new ResultViewModel<VisitorStrategyChatResponse>
                    {
                        isSuccess = false,
                        message = "attraction_uid 不可為空白。",
                        Result = null
                    });
                }

                VisitorStrategyChatResponse result =
                    await _service.ChatStrategyAsync(request);

                return Ok(new ResultViewModel<VisitorStrategyChatResponse>
                {
                    isSuccess = true,
                    message = result?.message ?? "完成",
                    Result = result
                });
            }
            catch (ArgumentException e)
            {
                return BadRequest(new ResultViewModel<VisitorStrategyChatResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "經營策略對話失敗");

                return StatusCode(500, new ResultViewModel<VisitorStrategyChatResponse>
                {
                    isSuccess = false,
                    message = "經營策略對話失敗：" + e.Message,
                    Result = null
                });
            }
        }

        #endregion
    }
}
