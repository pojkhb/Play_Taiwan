using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.Services;
using backend.Models;
using backend.utils;
using backend.ViewModels;

namespace backend.Controllers
{
    /// <summary>
    /// 商家專屬後台 API。
    /// 提供商家註冊、店家資料維護、近期檔案列表、生成影音及查看 Reels 完成畫面等功能。
    /// 商家登入跟一般帳號共用 POST api/Auth/Login；除了註冊之外，一律用 JWT 識別商家（au_id / s_id Claim），不再從 request 帶 s_id。
    /// 錯誤統一由 ExceptionHandlingMiddleware 轉成 ResultViewModel（404 / 409 / 403 / 500）。
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class MerchantController : ControllerBase
    {
        private readonly MerchantService _service;

        public MerchantController(MerchantService service)
        {
            _service = service;
        }

        #region 1. 商家註冊
        /// <summary>註冊商家。</summary>
        /// <remarks>
        /// 建立 auth(auth_type=2, 免審核直接啟用) + store。place_uid 與 new_place 二擇一：
        /// 選擇既有景點時帶 place_uid（由 GET api/merchant/register/search-place 取得）；
        /// 選「都沒有，我要建立新的」時帶 new_place，後端會在 Neo4j 建立新的身分節點與版本節點，
        /// 並寫入 MySQL place（座標）與 place_type（通用任務類型 6、7、8），讓這個景點能被排進劇本行程。
        /// new_place.lat / new_place.lng 必填（建議由地圖選點取得）；new_place.category 可選
        /// Attraction / Restaurant / Hotel / Event，不帶時為 Restaurant（Restaurant 會多出地方美食型任務）。
        /// 註冊完成後請呼叫 POST api/Auth/Login（跟一般帳號同一個登入）取得 Token。
        /// </remarks>
        [AllowAnonymous]
        [HttpPost]
        [Route("Register")]
        [ProducesResponseType(typeof(ResultViewModel<MerchantRegisterResponse>), 200)]
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
        #endregion

        #region 2. 店家資料
        /// <summary>查詢登入商家的店家資料。</summary>
        [Authorize(Roles = "Merchant")]
        [HttpGet]
        [Route("Profile")]
        [ProducesResponseType(typeof(ResultViewModel<MerchantDetailResponse>), 200)]
        public IActionResult GetProfile()
        {
            return Ok(new ResultViewModel<MerchantDetailResponse>
            {
                isSuccess = true,
                message = "查詢成功",
                Result = _service.GetProfile(User.GetSId())
            });
        }

        /// <summary>更新登入商家的店家資料。</summary>
        /// <remarks>
        /// 只更新有帶值的欄位，沒帶的欄位維持原值；只改店名時帶 store_name 即可（取代原本的 StoreName API）。
        /// 同時會在 Neo4j 建立新的版本節點，讓 QR Code 掃描回傳的商家資訊跟著更新。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "store_name": "日式復古串燒居酒屋"
        /// }
        /// ```
        /// </remarks>
        [Authorize(Roles = "Merchant")]
        [HttpPut]
        [Route("Profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] MerchantUpdateRequest req)
        {
            await _service.UpdateProfileAsync(User.GetSId(), req);
            return Ok(new ResultViewModel<object>
            {
                isSuccess = true,
                message = "商家資料更新成功",
                Result = null
            });
        }

        /// <summary>查詢商家列表（限管理員）。</summary>
        [Authorize(Roles = "Admin")]
        [HttpGet]
        [Route("List")]
        [ProducesResponseType(typeof(ResultViewModel<MerchantListResponse>), 200)]
        public IActionResult List([FromQuery] MerchantListQuery query)
        {
            return Ok(new ResultViewModel<MerchantListResponse>
            {
                isSuccess = true,
                message = "查詢成功",
                Result = _service.List(query)
            });
        }
        #endregion

        #region 3. 已經生成檔案列表 (依編輯時間新到舊排序)
        /// <summary>
        /// 取得商家已生成的檔案清單。
        /// </summary>
        /// <remarks>
        /// 對應商家首頁的「近期檔案」列表，後端會自動依**編輯時間 (updated_at) 從新到舊**排序回傳。
        ///
        /// **Request 範例**：
        ///
        ///     GET /api/Merchant/Files
        ///
        /// **Response 範例**：
        /// ```json
        /// {
        ///   "isSuccess": true,
        ///   "message": "查詢成功",
        ///   "Result": [
        ///     {
        ///       "mm_id": 1,
        ///       "mm_title": "日式串燒限時特惠活動",
        ///       "mm_video_url": "https://example.com/video.mp4",
        ///       "mm_status": 3,
        ///       "updated_at": "2026-09-07T12:30:00"
        ///     }
        ///   ]
        /// }
        /// ```
        /// </remarks>
        /// <returns>依編輯時間新到舊排序的檔案清單陣列。</returns>
        [Authorize(Roles = "Merchant")]
        [HttpGet]
        [Route("Files")]
        [ProducesResponseType(typeof(ResultViewModel<List<MerchantFileItem>>), 200)]
        public IActionResult GetMerchantFiles()
        {
            return Ok(new ResultViewModel<List<MerchantFileItem>>
            {
                isSuccess = true,
                message = "查詢成功",
                Result = _service.GetMerchantFiles(User.GetAuId())
            });
        }
        #endregion
    }
}
