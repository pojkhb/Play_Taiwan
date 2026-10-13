// 檔案路徑：System\Controllers\Pair\PairController.cs
using backend.Services;
using backend.utils;
using backend.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>
    /// 協作解謎配對 API。
    /// 劇本擁有者產生四位數配對碼（10 分鐘內有效）分享給同行玩家，其他玩家輸入配對碼加入同一份劇本的協作隊伍；
    /// 擁有者是隊長、坐 1 號座位，其他人依加入順序坐空座位，座位決定協作解謎任務看到哪一段線索。
    /// 人數到齊（劇本的 party_size）時隊伍自動鎖定、配對碼失效；鎖定後有人退出，隊長再產生一次配對碼即可補人。
    /// 身分一律取自 JWT；錯誤統一由 ExceptionHandlingMiddleware 轉成 ResultViewModel（404 / 409 / 403 / 500）。
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PairController : ControllerBase
    {
        private readonly PairService _service;

        public PairController(PairService service)
        {
            _service = service;
        }

        /// <summary>產生配對碼（限劇本擁有者）。</summary>
        /// <remarks>
        /// - 還沒有隊伍：建立隊伍，擁有者坐 1 號座位，回傳新的四位數配對碼
        /// - 配對碼仍在有效期內：回傳原本的配對碼（不會換號）
        /// - 配對碼逾期，或隊伍鎖定後有人退出：重新開放並給一組新配對碼，已加入的成員維持原座位
        /// - 隊伍已滿：409；單人劇本（party_size 小於 2）：409
        ///
        /// **Request 範例**：
        /// ```json
        /// { "story_id": 1 }
        /// ```
        /// </remarks>
        [HttpPost]
        [Route("Code")]
        [ProducesResponseType(typeof(ResultViewModel<PairSessionResponse>), 200)]
        public IActionResult IssueCode([FromBody] PairStoryRequest req)
        {
            return Ok(new ResultViewModel<PairSessionResponse>
            {
                isSuccess = true,
                message = "配對碼已產生",
                Result = _service.IssueCode(User.GetAuId(), req?.story_id ?? 0)
            });
        }

        /// <summary>輸入配對碼加入協作隊伍。</summary>
        /// <remarks>
        /// 加入後坐最小的空座位，這份劇本同時設為自己進行中的劇本（同確認開始）；已經是成員時直接回傳隊伍資訊。
        /// 配對碼錯誤或逾期：404；隊伍已滿：409。
        ///
        /// **Request 範例**：
        /// ```json
        /// { "pair_code": "0427" }
        /// ```
        /// </remarks>
        [HttpPost]
        [Route("Join")]
        [ProducesResponseType(typeof(ResultViewModel<PairSessionResponse>), 200)]
        public IActionResult Join([FromBody] PairJoinRequest req)
        {
            return Ok(new ResultViewModel<PairSessionResponse>
            {
                isSuccess = true,
                message = "已加入協作隊伍",
                Result = _service.Join(User.GetAuId(), req?.pair_code)
            });
        }

        /// <summary>查詢劇本目前的協作隊伍（限擁有者與隊員）。</summary>
        /// <remarks>
        /// 隊長等待隊員加入時可輪詢這支；回傳成員與座位、自己的座位（my_seat_no）、配對碼剩餘秒數。
        /// 還沒有隊伍時 Result 為 null。
        ///
        ///     GET /api/Pair/Story/1
        /// </remarks>
        [HttpGet]
        [Route("Story/{story_id:int}")]
        [ProducesResponseType(typeof(ResultViewModel<PairSessionResponse>), 200)]
        public IActionResult GetByStory(int story_id)
        {
            PairSessionResponse result = _service.GetByStory(User.GetAuId(), story_id);
            return Ok(new ResultViewModel<PairSessionResponse>
            {
                isSuccess = true,
                message = result == null ? "這份劇本目前沒有協作隊伍" : "查詢成功",
                Result = result
            });
        }

        /// <summary>隊員退出協作隊伍。</summary>
        /// <remarks>隊長（劇本擁有者）不能退出，請改用取消隊伍；退出後這份劇本的遊玩狀態改成暫停。</remarks>
        [HttpPost]
        [Route("Leave")]
        public IActionResult Leave([FromBody] PairStoryRequest req)
        {
            _service.Leave(User.GetAuId(), req?.story_id ?? 0);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "已退出協作隊伍", Result = null });
        }

        /// <summary>取消協作隊伍（限劇本擁有者）。</summary>
        /// <remarks>隊伍改成 cancelled，配對碼失效，其他隊員在這份劇本的遊玩狀態改成暫停。</remarks>
        [HttpPost]
        [Route("Cancel")]
        public IActionResult Cancel([FromBody] PairStoryRequest req)
        {
            _service.Cancel(User.GetAuId(), req?.story_id ?? 0);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "已取消協作隊伍", Result = null });
        }

        /// <summary>回報自己的位置，並拿到隊友的位置（地圖上顯示隊友在哪）。</summary>
        /// <remarks>
        /// 多人一起玩時，地圖開著的期間每 5～10 秒帶目前 GPS 呼叫一次：後端記下自己的位置，同時回傳一起玩的人各自最後的位置，
        /// 一趟來回就能更新地圖。限劇本擁有者與隊伍成員（403）；找不到劇本 404；沒帶位置 400。
        ///
        /// - members：一起玩的人（依座位排序，包含自己，is_me = true 的那一筆）；沒有協作隊伍時只有劇本擁有者自己
        /// - lat、lng：隊友最後回報的位置；還沒回報過、或超過 30 分鐘沒回報時為 null（不要畫在地圖上）
        /// - seconds_ago：這筆位置是幾秒前的；is_online：最近 60 秒內有回報，false 時可以畫成灰色
        /// - heading：面向的方向（度，0＝正北），回報時有帶才有
        ///
        /// 位置只放在後端記憶體、不寫進資料庫，也不留移動軌跡；退出隊伍或隊伍取消後，位置就不會再被隊友看到。
        ///
        /// **Request 範例**：
        /// ```json
        /// { "lat": 24.1378, "lng": 120.6835, "accuracy": 12.5, "heading": 90 }
        /// ```
        /// </remarks>
        [HttpPost]
        [Route("Story/{story_id:int}/Location")]
        [ProducesResponseType(typeof(ResultViewModel<TeamLocationsResponse>), 200)]
        public IActionResult ReportLocation(int story_id, [FromBody] TeamLocationRequest req)
        {
            return Ok(new ResultViewModel<TeamLocationsResponse>
            {
                isSuccess = true,
                message = "位置已更新",
                Result = _service.ReportLocation(User.GetAuId(), story_id, req)
            });
        }

        /// <summary>查看隊友的位置（不回報自己的位置）。</summary>
        /// <remarks>
        /// 回傳內容跟 POST /api/Pair/Story/{story_id}/Location 一樣，差別是不會更新自己的位置；
        /// 例如使用者關掉定位、只想看隊友在哪的時候用。限劇本擁有者與隊伍成員（403）。
        ///
        ///     GET /api/Pair/Story/1/Locations
        /// </remarks>
        [HttpGet]
        [Route("Story/{story_id:int}/Locations")]
        [ProducesResponseType(typeof(ResultViewModel<TeamLocationsResponse>), 200)]
        public IActionResult GetLocations(int story_id)
        {
            return Ok(new ResultViewModel<TeamLocationsResponse>
            {
                isSuccess = true,
                message = "查詢成功",
                Result = _service.GetLocations(User.GetAuId(), story_id)
            });
        }
    }
}
