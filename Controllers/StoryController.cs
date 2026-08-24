using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using backend.Services;
using backend.Models;
using backend.ViewModels;
using backend.dao; // 確保有引用 DAO 以使用 MediaJobDao

namespace backend.Controllers
{
    /// <summary>
    /// 劇本產生相關 API。
    /// 對應頁面：選擇、來點遊意思、選擇劇情、劇情觀看更多。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    // 劇本產生
    public class StoryController : ControllerBase
    {
        private readonly ILogger<StoryController> _logger;
        private readonly StoryService _service;
        private readonly IVlogAiClient _aiClient;
        private readonly MediaJobDao _jobDao;

        // 💡 注入了 AI 客戶端與 Job DAO
        public StoryController(
            ILogger<StoryController> logger,
            StoryService service,
            IVlogAiClient aiClient,
            MediaJobDao jobDao
        )
        {
            _logger = logger;
            _service = service;
            _aiClient = aiClient;
            _jobDao = jobDao;
        }

        #region 來點遊意思：轉盤抽取地區

        /// <summary>
        /// 轉動命運轉盤，隨機抽取一個地區。
        /// </summary>
        /// <remarks>
        /// 對應「來點遊意思」頁面的轉盤抽取功能。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Story/Wheel/Spin
        /// </remarks>
        /// <returns>抽取到的地區資訊。</returns>
        // API：轉盤抽取地區（SpinWheel）－隨機抽取一個地區
        [Authorize]
        [HttpPost]
        [Route("Wheel/Spin")]
        // POST: api/Story/Wheel/Spin
        public IActionResult SpinWheel()
        {
            try
            {
                return Ok(new ResultViewModel<StoryWheelSpinResponse>
                {
                    isSuccess = true,
                    message = "抽取成功",
                    Result = _service.WheelSpin()
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "轉盤抽取地區失敗");

                return StatusCode(500, new ResultViewModel<StoryWheelSpinResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 現在揪出發：地區清單

        /// <summary>
        /// 取得可選擇的地區清單。
        /// </summary>
        /// <remarks>
        /// 對應「選擇」頁面「現在揪出發」輸入地區時的可選清單。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Story/Regions?mode=NOW
        /// </remarks>
        /// <param name="mode">查詢模式，預設為 NOW（目前定位）。</param>
        /// <returns>可選擇的地區清單。</returns>
        // API：地區清單（Regions）－回傳可選擇的地區清單
        [Authorize]
        [HttpGet]
        [Route("Regions")]
        // GET: api/Story/Regions?mode=NOW
        public IActionResult Regions(
            [FromQuery] string mode = "NOW"
        )
        {
            try
            {
                return Ok(new ResultViewModel<List<StoryWheelSpinResponse>>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetRegions(mode)
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢地區清單失敗");

                return StatusCode(
                    500,
                    new ResultViewModel<List<StoryWheelSpinResponse>>
                    {
                        isSuccess = false,
                        message = e.Message,
                        Result = null
                    }
                );
            }
        }

        #endregion

        #region 產生劇本選項

        /// <summary>
        /// 依地區與偏好產生可選擇的劇本選項清單。
        /// </summary>
        /// <remarks>
        /// 對應「選擇劇情」頁面的「劇本檔案館」列表。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Story/Generate
        ///     {
        ///       "region": "台南市"
        ///     }
        /// </remarks>
        /// <param name="req">產生劇本選項的請求資料，包含地區等條件。</param>
        /// <returns>符合條件的劇本選項清單。</returns>
        // API：產生劇本選項（Generate）－依地區與偏好回傳可選擇的劇本清單
        [Authorize]
        [HttpPost]
        [Route("Generate")]
        // POST: api/Story/Generate
        public IActionResult Generate(
            [FromBody] StoryGenerateRequest req
        )
        {
            try
            {
                return Ok(new ResultViewModel<List<StoryOptionResponse>>
                {
                    isSuccess = true,
                    message = "產生成功",
                    Result = _service.GenerateOptions(req)
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "產生劇本選項失敗");

                return StatusCode(
                    500,
                    new ResultViewModel<List<StoryOptionResponse>>
                    {
                        isSuccess = false,
                        message = e.Message,
                        Result = null
                    }
                );
            }
        }

        #endregion

        #region 劇情觀看更多

        /// <summary>
        /// 取得指定劇本的詳細內容。
        /// </summary>
        /// <remarks>
        /// 對應「劇情觀看更多」頁面，顯示劇本前傳與探索總覽。
        ///
        /// Request 範例：
        ///
        ///     GET /api/Story/{story_id}/Detail
        /// </remarks>
        /// <param name="story_id">劇本代號。</param>
        /// <returns>劇本詳細內容。</returns>
        // API：劇情觀看更多（Detail）－回傳指定劇本的前傳與探索總覽
        [Authorize]
        [HttpGet]
        [Route("{story_id}/Detail")]
        // GET: api/Story/{story_id}/Detail
        public IActionResult Detail(string story_id)
        {
            try
            {
                return Ok(new ResultViewModel<StoryDetailResponse>
                {
                    isSuccess = true,
                    message = "查詢成功",
                    Result = _service.GetDetail(story_id)
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢劇本詳情失敗");

                return StatusCode(500, new ResultViewModel<StoryDetailResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        #region 確認選卷

        /// <summary>
        /// 確認選擇指定劇本卷。
        /// </summary>
        /// <remarks>
        /// 對應「劇情觀看更多」頁面的「確認此卷」按鈕，確認後即進入探索地圖。
        ///
        /// Request 範例：
        ///
        ///     POST /api/Story/Confirm
        ///     {
        ///       "story_id": "S001"
        ///     }
        /// </remarks>
        /// <param name="req">確認選卷的請求資料。</param>
        /// <returns>確認後的劇本與節點資訊。</returns>
        // API：確認選卷（Confirm）－確認選擇劇本卷並回傳劇本與節點資訊
        [Authorize]
        [HttpPost]
        [Route("Confirm")]
        // POST: api/Story/Confirm
        public IActionResult Confirm(
            [FromBody] StoryConfirmRequest req
        )
        {
            try
            {
                // 目前只回傳劇本與節點資訊
                // 之後再補 ep_story_progress 寫入資料庫
                StoryDetailResponse detail =
                    _service.ConfirmStory(req);

                return Ok(new ResultViewModel<StoryDetailResponse>
                {
                    isSuccess = true,
                    message = "確認選卷成功，即將進入探索地圖",
                    Result = detail
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "確認選卷失敗");

                return StatusCode(500, new ResultViewModel<StoryDetailResponse>
                {
                    isSuccess = false,
                    message = e.Message,
                    Result = null
                });
            }
        }

        #endregion

        // =========================================================
        // 以下為新增的 AI 專屬非同步生成劇本 API
        // =========================================================

        #region AI 專屬劇本生成 (非同步 Job 機制)

        /// <summary>
        /// 1. 送出客製化劇本需求，讓 Python AI 開始生成 (限制 1~2 小時行程，5~7 個節點)
        /// </summary>
        [Authorize]
        [HttpPost]
        [Route("GenerateAi")]
        public async Task<IActionResult> GenerateAiStory([FromBody] StoryGenerateRequest req)
        {
            try
            {
                string epId = User.FindFirst("ep_id")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(epId)) return Unauthorized(new ResultViewModel<string> { isSuccess = false, message = "無法驗證身分" });

                /* 這裡已經完全改用你的 req.party_size, req.region, req.preferences, req.transport */
                string customPrompt = $@"
                    你是一個專業的實境解謎遊戲設計師。
                    請為 {req.party_size} 位玩家，在「{req.region}」設計一個大約 1~2 小時的解謎劇本。
                    玩家的偏好是：{(req.preferences != null ? string.Join("、", req.preferences) : "無特定偏好")}。
                    交通方式為：{(req.transport != null ? string.Join("、", req.transport) : "不拘")}。
                    請務必確保行程順暢，景點與景點之間的距離合理，節點數量嚴格控制在 5 到 7 個，並且回傳我指定的 JSON 格式。
                ";

                var payloadToPython = new
                {
                    region = req.region,
                    player_count = req.party_size,
                    preferences = req.preferences,
                    transport = req.transport,
                    system_prompt = customPrompt
                };

                var jsonContent = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(payloadToPython), System.Text.Encoding.UTF8, "application/json");

                /* 向 Python AI 服務請求生成，拿到遠端 Task ID */
                string externalTaskId = await _aiClient.GenerateStoryAsync(jsonContent);

                if (string.IsNullOrEmpty(externalTaskId))
                    throw new Exception("AI 服務未回傳有效的 Task ID");

                /* 在本地建立任務追蹤，把玩家選擇的 region 暫存在 job 的 result_url 欄位裡 */
                string localJobId = Guid.NewGuid().ToString();
                var newJob = new MediaJobModel
                {
                    job_id = localJobId,
                    owner_id = epId,
                    job_type = "story_generation",
                    external_task_id = externalTaskId,
                    status = "Processing",
                    result_url = req.region 
                };
                await _jobDao.InsertJobAsync(newJob);

                return Ok(new ResultViewModel<object>
                {
                    isSuccess = true,
                    message = "AI 正在努力為您撰寫專屬劇本，請稍候...",
                    Result = new { jobId = localJobId, status = "Processing" }
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "觸發 AI 生成失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        /// <summary>
        /// 2. 前端輪詢進度：若 Python 完成，C# 就把它存進資料庫，並回傳真正的 Story_ID
        /// </summary>
        [Authorize]
        [HttpGet]
        [Route("GenerateAiStatus/{jobId}")]
        public async Task<IActionResult> GetAiStoryStatus(string jobId)
        {
            try
            {
                string epId = User.FindFirst("ep_id")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                var job = await _jobDao.GetJobAsync(jobId);
                if (job == null) return NotFound(new ResultViewModel<string> { isSuccess = false, message = "找不到此任務" });

                // 如果已經完成了，直接回傳之前存好的 story_id
                if (job.status == "Completed")
                {
                    return Ok(new ResultViewModel<object>
                    {
                        isSuccess = true,
                        message = "劇本生成完畢！",
                        Result = new { status = "Completed", story_id = job.result_url }
                    });
                }

                // 如果還在處理中，去問 Python 好了沒
                if (job.status == "Processing")
                {
                    var remoteStatus = await _aiClient.CheckStatusAsync(job.external_task_id);

                    if (remoteStatus.status == "completed" || remoteStatus.status == "done")
                    {
                        // 1. 將 Python 傳回來的 JSON 反序列化成我們的 C# Model
                        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        AiStoryResult aiResult = System.Text.Json.JsonSerializer.Deserialize<AiStoryResult>(remoteStatus.result_path, options);

                        // 2. 拿出之前暫存的地區名稱
                        string regionName = job.result_url;

                        // 3. 把 AI 生出來的劇本，正式寫進 MySQL
                        string newStoryId = _service.SaveAiGeneratedStory(epId, regionName, aiResult);

                        // 4. 更新 Job 狀態，並把 result_url 替換成真實的 Story ID
                        await _jobDao.UpdateJobStatusAsync(jobId, "Completed", newStoryId);

                        return Ok(new ResultViewModel<object>
                        {
                            isSuccess = true,
                            message = "劇本生成完畢！",
                            Result = new { status = "Completed", story_id = newStoryId }
                        });
                    }
                    else if (remoteStatus.status == "failed")
                    {
                        await _jobDao.UpdateJobStatusAsync(jobId, "Failed", null);
                        return Ok(new ResultViewModel<object>
                        {
                            isSuccess = true,
                            message = "AI 生成失敗，請重新嘗試",
                            Result = new { status = "Failed" }
                        });
                    }
                }

                // 依然還在處理中
                return Ok(new ResultViewModel<object>
                {
                    isSuccess = true,
                    message = "生成中...",
                    Result = new { status = "Processing" }
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "查詢 AI 生成狀態失敗");
                return StatusCode(500, new ResultViewModel<string> { isSuccess = false, message = e.Message, Result = null });
            }
        }

        #endregion
    }
}