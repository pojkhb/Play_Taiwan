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
        /// 建立 auth(auth_type=2, 免審核直接啟用) + store。
        /// 選擇既有景點時帶 place_uid（由 GET api/merchant/register/search-place 取得）；
        /// 沒帶 place_uid 代表選「都沒有，我要建立新的」，後端會在 Neo4j 建立新的身分節點與版本節點，
        /// 並寫入 MySQL place（座標）與 place_type（通用任務類型 6、7、8），讓這個景點能被排進劇本行程。
        /// 新景點的名稱、地址、介紹用 store_name（必填）、store_address、store_dec；地址與 lat / lng 至少填一個，只填一個時後端自動轉另一個；
        /// category 可選 Attraction / Restaurant / Hotel / Event，不帶時為 Restaurant（Restaurant 會多出地方美食型任務）。
        /// lat、lng、category、phone、website、operating_hours 只在建立新景點時使用，帶了 place_uid 會忽略。
        ///
        /// operating_hours（營業時間，選填）跟 Neo4j 政府資料同格式，一個時段一筆，公休日不填、中午休息就同一天填兩筆；
        /// 跨夜營業結束時間填隔天的時間，營業到半夜 12 點填 00:00，全天營業填 00:00～23:59。格式不對或同一天時段重疊回 400：
        /// ```json
        /// "operating_hours": [
        ///   { "day_of_week": "Tuesday", "open_time": "11:00", "close_time": "14:00" },
        ///   { "day_of_week": "Tuesday", "open_time": "17:00", "close_time": "21:00" },
        ///   { "day_of_week": "Saturday", "open_time": "18:00", "close_time": "02:00" }
        /// ]
        /// ```
        /// 註冊後掃 QR Code 回傳的 merchant_place.operating_hours 就是這份資料（跟政府景點同一個欄位）。
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
        /// <remarks>
        /// 多回傳 operating_hours（目前的營業時間，格式同註冊）與 is_self_built_place（true 才能修改座標與營業時間），給編輯畫面帶入。
        /// </remarks>
        [Authorize(Roles = "Merchant")]
        [HttpGet]
        [Route("Profile")]
        [ProducesResponseType(typeof(ResultViewModel<MerchantProfileResponse>), 200)]
        public async Task<IActionResult> GetProfile()
        {
            return Ok(new ResultViewModel<MerchantProfileResponse>
            {
                isSuccess = true,
                message = "查詢成功",
                Result = await _service.GetProfileAsync(User.GetSId())
            });
        }

        /// <summary>更新登入商家的店家資料。</summary>
        /// <remarks>
        /// 只更新有帶值的欄位，沒帶的欄位維持原值；只改店名時帶 store_name 即可（取代原本的 StoreName API）。
        /// 同時會在 Neo4j 建立新的版本節點，讓 QR Code 掃描回傳的商家資訊跟著更新。
        ///
        /// operating_hours（營業時間）只對商家自建景點有效（GET Profile 的 is_self_built_place = true），格式跟註冊相同；
        /// 有帶就整份取代，帶 [] 代表清空，不帶維持原值。格式不對或同一天時段重疊回 400，其他欄位也不會被改到。
        ///
        /// **Request 範例**：
        /// ```json
        /// {
        ///   "store_name": "日式復古串燒居酒屋",
        ///   "operating_hours": [
        ///     { "day_of_week": "Monday", "open_time": "17:00", "close_time": "00:00" },
        ///     { "day_of_week": "Friday", "open_time": "17:00", "close_time": "02:00" }
        ///   ]
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
