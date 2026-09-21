using System.Threading.Tasks;
using backend.Services;
using backend.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace backend.Controllers
{
    /// <summary>
    /// 商家帳號與商家資料維護。本階段不做登入驗證，所有操作直接以 request 帶入的 s_id 識別商家。
    /// </summary>
    [ApiController]
    [Route("api/merchant")]
    public class MerchantAccountController : ControllerBase
    {
        private readonly ILogger<MerchantAccountController> _logger;
        private readonly MerchantAccountService _service;

        public MerchantAccountController(ILogger<MerchantAccountController> logger, MerchantAccountService service)
        {
            _logger = logger;
            _service = service;
        }

        /// <summary>註冊商家。</summary>
        /// <remarks>
        /// 建立 auth(auth_type=2, 免審核直接啟用) + store。place_uid 與 new_place 二擇一：
        /// 選擇既有景點時帶 place_uid（由 GET api/merchant/register/search-place 取得）；
        /// 選「都沒有，我要建立新的」時帶 new_place，後端會在 Neo4j 建立新的身分節點與版本節點。
        /// </remarks>
        [HttpPost]
        [Route("register")]
        public async Task<IActionResult> Register([FromBody] MerchantRegisterRequest req)
        {
            MerchantRegisterResponse result = await _service.RegisterAsync(req);
            return Ok(new ResultViewModel<MerchantRegisterResponse>
            {
                isSuccess = true,
                message = "商家註冊成功",
                Result = result
            });
        }

        /// <summary>查詢商家資料。</summary>
        [HttpGet]
        [Route("{sId:int}")]
        public IActionResult Get(int sId)
        {
            MerchantDetailResponse result = _service.GetById(sId);
            return Ok(new ResultViewModel<MerchantDetailResponse>
            {
                isSuccess = true,
                message = "查詢成功",
                Result = result
            });
        }

        /// <summary>更新商家資料。</summary>
        /// <remarks>同時會在 Neo4j 建立新的版本節點，讓 NFC 掃描回傳的商家資訊跟著更新。</remarks>
        [HttpPut]
        [Route("{sId:int}")]
        public async Task<IActionResult> Update(int sId, [FromBody] MerchantUpdateRequest req)
        {
            await _service.UpdateAsync(sId, req);
            return Ok(new ResultViewModel<object>
            {
                isSuccess = true,
                message = "商家資料更新成功",
                Result = null
            });
        }

        /// <summary>刪除商家帳號。</summary>
        /// <remarks>
        /// 交易內依序刪除 nfc_coupon／user_coupon／coupon／question_option／store_question／store／auth；
        /// 若商家題庫仍被任務引用，會回傳 409 並附上引用的 task_id 清單，不會刪除任何資料。
        /// </remarks>
        [HttpDelete]
        [Route("{sId:int}")]
        public async Task<IActionResult> Delete(int sId)
        {
            await _service.DeleteAsync(sId);
            return Ok(new ResultViewModel<object>
            {
                isSuccess = true,
                message = "商家帳號已刪除",
                Result = null
            });
        }

        /// <summary>查詢商家列表。</summary>
        [HttpGet]
        [Route("")]
        public IActionResult List([FromQuery] MerchantListQuery query)
        {
            MerchantListResponse result = _service.List(query);
            return Ok(new ResultViewModel<MerchantListResponse>
            {
                isSuccess = true,
                message = "查詢成功",
                Result = result
            });
        }
    }
}
