using System.Collections.Generic;
using backend.Services;
using backend.util;
using backend.utils;
using backend.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>
    /// 管理員後台：用戶管理、劇本管理、徽章管理（限管理員 auth_type = 3）。
    /// 用戶、劇本、勳章的刪除都是軟刪除（is_active = 0），用修改 API 把 is_active 改回 true 即可還原。
    /// </summary>
    [Authorize(Roles = "Admin")]
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly AdminService _service;

        public AdminController(AdminService service)
        {
            _service = service;
        }

        #region 用戶管理

        /// <summary>查詢用戶列表。</summary>
        /// <remarks>
        /// keyword 比對名稱或 Email；auth_type 1=遊客、2=商家、3=管理員；is_active 不帶就是全部。
        /// 依 au_id 新到舊排序，page_size 上限 100。
        ///
        ///     GET /api/Admin/Users?keyword=test&amp;auth_type=1&amp;page=1&amp;page_size=20
        /// </remarks>
        [HttpGet]
        [Route("Users")]
        [ProducesResponseType(typeof(ResultViewModel<AdminPagedResponse<AdminUserResponse>>), 200)]
        public IActionResult ListUsers([FromQuery] AdminUserListQuery query)
        {
            return Ok(new ResultViewModel<AdminPagedResponse<AdminUserResponse>> { isSuccess = true, message = "查詢成功", Result = _service.ListUsers(query) });
        }

        /// <summary>查詢單一用戶。</summary>
        [HttpGet]
        [Route("Users/{auId:int}")]
        [ProducesResponseType(typeof(ResultViewModel<AdminUserResponse>), 200)]
        public IActionResult GetUser(int auId)
        {
            return Ok(new ResultViewModel<AdminUserResponse> { isSuccess = true, message = "查詢成功", Result = _service.GetUser(auId) });
        }

        /// <summary>新增用戶。</summary>
        /// <remarks>
        /// 管理員建立的帳號直接視為已驗證信箱，可以馬上登入；Email 重複回 409。
        /// 建立商家帳號（auth_type = 2）後，商家還要自己建立店家資料才能使用商家功能。
        ///
        ///     POST /api/Admin/Users
        ///     { "auth_name": "王小明", "auth_email": "ming@example.com", "auth_pswd": "Password123", "auth_type": 1 }
        /// </remarks>
        [HttpPost]
        [Route("Users")]
        public IActionResult CreateUser([FromBody] AdminUserCreateRequest req)
        {
            int auId = _service.CreateUser(req);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "用戶新增成功", Result = new { au_id = auId } });
        }

        /// <summary>修改用戶資料（整筆覆蓋，不含密碼）。</summary>
        /// <remarks>不能停用自己或拿掉自己的管理員身分；Email 跟其他帳號重複回 409。</remarks>
        [HttpPut]
        [Route("Users/{auId:int}")]
        public IActionResult UpdateUser(int auId, [FromBody] AdminUserUpdateRequest req)
        {
            _service.UpdateUser(User.GetAuId(), auId, req);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "用戶修改成功", Result = null });
        }

        /// <summary>重設用戶密碼。</summary>
        /// <remarks>
        ///     PUT /api/Admin/Users/7/Password
        ///     { "new_password": "NewPassword123" }
        /// </remarks>
        [HttpPut]
        [Route("Users/{auId:int}/Password")]
        public IActionResult ResetUserPassword(int auId, [FromBody] AdminUserPasswordRequest req)
        {
            _service.ResetUserPassword(auId, req.new_password);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "密碼已重設", Result = null });
        }

        /// <summary>刪除（停用）用戶。</summary>
        /// <remarks>只把 is_active 設為 0，資料都保留，停用後無法登入；不能停用自己。</remarks>
        [HttpDelete]
        [Route("Users/{auId:int}")]
        public IActionResult DeleteUser(int auId)
        {
            _service.DeleteUser(User.GetAuId(), auId);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "用戶已停用", Result = null });
        }

        #endregion

        #region 劇本管理

        /// <summary>查詢劇本列表。</summary>
        /// <remarks>
        /// keyword 比對標題、縣市、鄉鎮區；au_id 只看某位用戶的劇本；is_active=false 只看已刪除的劇本。
        /// 依 s_id 新到舊排序，page_size 上限 100。
        ///
        ///     GET /api/Admin/Stories?keyword=台北&amp;page=1&amp;page_size=20
        /// </remarks>
        [HttpGet]
        [Route("Stories")]
        [ProducesResponseType(typeof(ResultViewModel<AdminPagedResponse<AdminStoryListItem>>), 200)]
        public IActionResult ListStories([FromQuery] AdminStoryListQuery query)
        {
            return Ok(new ResultViewModel<AdminPagedResponse<AdminStoryListItem>> { isSuccess = true, message = "查詢成功", Result = _service.ListStories(query) });
        }

        /// <summary>查詢單一劇本（含偏好標籤與節點）。</summary>
        [HttpGet]
        [Route("Stories/{storyId:int}")]
        [ProducesResponseType(typeof(ResultViewModel<AdminStoryDetailResponse>), 200)]
        public IActionResult GetStory(int storyId)
        {
            return Ok(new ResultViewModel<AdminStoryDetailResponse> { isSuccess = true, message = "查詢成功", Result = _service.GetStory(storyId) });
        }

        /// <summary>新增劇本主檔。</summary>
        /// <remarks>
        /// 只建立劇本主檔，節點、任務仍由 AI 生成流程寫入。
        ///
        ///     POST /api/Admin/Stories
        ///     { "au_id": 7, "story_title": "大稻埕的秘密", "city_name": "臺北市", "district_name": "大同區", "party_size": 2, "npc_id": 1 }
        /// </remarks>
        [HttpPost]
        [Route("Stories")]
        public IActionResult CreateStory([FromBody] AdminStoryCreateRequest req)
        {
            int storyId = _service.CreateStory(req);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "劇本新增成功", Result = new { s_id = storyId } });
        }

        /// <summary>修改劇本主檔（整筆覆蓋）。</summary>
        /// <remarks>is_active 改回 true 可以還原已刪除的劇本。</remarks>
        [HttpPut]
        [Route("Stories/{storyId:int}")]
        public IActionResult UpdateStory(int storyId, [FromBody] AdminStoryUpdateRequest req)
        {
            _service.UpdateStory(storyId, req);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "劇本修改成功", Result = null });
        }

        /// <summary>刪除（停用）劇本。</summary>
        /// <remarks>只把 is_active 設為 0，節點、遊玩紀錄等資料都保留，玩家端就查不到這個劇本。</remarks>
        [HttpDelete]
        [Route("Stories/{storyId:int}")]
        public IActionResult DeleteStory(int storyId)
        {
            _service.DeleteStory(storyId);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "劇本已刪除", Result = null });
        }

        #endregion

        #region 徽章管理

        /// <summary>勳章分類清單（新增 / 修改勳章時的下拉選單）。</summary>
        [HttpGet]
        [Route("Badges/Categories")]
        [ProducesResponseType(typeof(ResultViewModel<IReadOnlyList<string>>), 200)]
        public IActionResult GetBadgeCategories()
        {
            return Ok(new ResultViewModel<IReadOnlyList<string>> { isSuccess = true, message = "查詢成功", Result = _service.GetBadgeCategories() });
        }

        /// <summary>查詢勳章列表。</summary>
        /// <remarks>
        /// keyword 比對名字或對應物件；b_fication 篩選分類（不含圖示）；is_active=false 只看已刪除的勳章。
        /// 依 b_id 排序，page_size 上限 100。b_image 是完整網址，owned_count 是已收集的用戶數。
        ///
        ///     GET /api/Admin/Badges?b_fication=島嶼城市&amp;page=1&amp;page_size=50
        /// </remarks>
        [HttpGet]
        [Route("Badges")]
        [ProducesResponseType(typeof(ResultViewModel<AdminPagedResponse<AdminBadgeResponse>>), 200)]
        public IActionResult ListBadges([FromQuery] AdminBadgeListQuery query)
        {
            AdminPagedResponse<AdminBadgeResponse> result = _service.ListBadges(query);
            foreach (AdminBadgeResponse badge in result.items)
                badge.b_image = PublicUrl.Of(Request, badge.b_image);

            return Ok(new ResultViewModel<AdminPagedResponse<AdminBadgeResponse>> { isSuccess = true, message = "查詢成功", Result = result });
        }

        /// <summary>查詢單一勳章。</summary>
        [HttpGet]
        [Route("Badges/{badgeId:int}")]
        [ProducesResponseType(typeof(ResultViewModel<AdminBadgeResponse>), 200)]
        public IActionResult GetBadge(int badgeId)
        {
            AdminBadgeResponse badge = _service.GetBadge(badgeId);
            badge.b_image = PublicUrl.Of(Request, badge.b_image);
            return Ok(new ResultViewModel<AdminBadgeResponse> { isSuccess = true, message = "查詢成功", Result = badge });
        }

        /// <summary>新增勳章。</summary>
        /// <remarks>
        /// b_fication 只能是 GET /api/Admin/Badges/Categories 列出的分類；b_image 先用 POST /api/Upload 上傳圖片拿到網址。
        /// 抽勳章時會用 b_thing 比對劇本內容（島嶼城市填縣市名、台灣印記填地標名）。
        ///
        ///     POST /api/Admin/Badges
        ///     { "b_fication": "台灣味", "b_thing": "豆花", "b_name": "豆香密令", "b_image": "/images/badges/douhua.png" }
        /// </remarks>
        [HttpPost]
        [Route("Badges")]
        public IActionResult CreateBadge([FromBody] AdminBadgeSaveRequest req)
        {
            int badgeId = _service.CreateBadge(req);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "勳章新增成功", Result = new { b_id = badgeId } });
        }

        /// <summary>修改勳章（整筆覆蓋）。</summary>
        /// <remarks>is_active 改回 true 可以還原已刪除的勳章。</remarks>
        [HttpPut]
        [Route("Badges/{badgeId:int}")]
        public IActionResult UpdateBadge(int badgeId, [FromBody] AdminBadgeUpdateRequest req)
        {
            _service.UpdateBadge(badgeId, req);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "勳章修改成功", Result = null });
        }

        /// <summary>刪除（停用）勳章。</summary>
        /// <remarks>
        /// 只把 is_active 設為 0：徽章圖鑑、首頁徽章數不再算這枚，抽勳章也抽不到；
        /// 用戶已收集的紀錄（au_badge）保留，劇本回顧裡當時抽到的勳章照樣顯示。
        /// </remarks>
        [HttpDelete]
        [Route("Badges/{badgeId:int}")]
        public IActionResult DeleteBadge(int badgeId)
        {
            _service.DeleteBadge(badgeId);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "勳章已刪除", Result = null });
        }

        #endregion
    }
}
