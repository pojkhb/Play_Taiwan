using System.Collections.Generic;
using System.Threading.Tasks;
using backend.Services;
using backend.utils;
using backend.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>商家端優惠券維護。商家身分由 JWT 的 s_id 取得，只能操作自己店家的優惠券。</summary>
    [Authorize(Roles = "Merchant")]
    [ApiController]
    [Route("api/merchant/coupons")]
    public class MerchantCouponController : ControllerBase
    {
        private readonly MerchantCouponService _service;

        public MerchantCouponController(MerchantCouponService service)
        {
            _service = service;
        }

        /// <summary>查詢登入商家的優惠券列表。</summary>
        /// <remarks>
        /// 每張優惠券多回傳 qrcode_uid（綁定的 QR Code uid，還沒綁定時為 null）
        /// 與 operating_hours（店家目前的營業時間，格式同 GET api/Merchant/Profile，依星期與開始時間排序）；
        /// 沒有綁定景點、沒填營業時間或 Neo4j 連不上時 operating_hours 為空陣列。
        /// </remarks>
        [HttpGet]
        [Route("")]
        [ProducesResponseType(typeof(ResultViewModel<List<MerchantCouponResponse>>), 200)]
        public async Task<IActionResult> GetByStore()
        {
            List<MerchantCouponResponse> result = await _service.GetByStoreAsync(User.GetSId());
            return Ok(new ResultViewModel<List<MerchantCouponResponse>> { isSuccess = true, message = "查詢成功", Result = result });
        }

        /// <summary>查詢單筆優惠券。</summary>
        /// <remarks>跟列表一樣多回傳 qrcode_uid（綁定的 QR Code）與 operating_hours（店家目前的營業時間）。</remarks>
        [HttpGet]
        [Route("{couponId:int}")]
        [ProducesResponseType(typeof(ResultViewModel<MerchantCouponResponse>), 200)]
        public async Task<IActionResult> GetById(int couponId)
        {
            MerchantCouponResponse result = await _service.GetByIdAsync(User.GetSId(), couponId);
            return Ok(new ResultViewModel<MerchantCouponResponse> { isSuccess = true, message = "查詢成功", Result = result });
        }

        /// <summary>新增優惠券。</summary>
        [HttpPost]
        [Route("")]
        public IActionResult Create([FromBody] CouponCreateRequest req)
        {
            int couponId = _service.Create(User.GetSId(), req);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "優惠券新增成功", Result = new { coupon_id = couponId } });
        }

        /// <summary>修改優惠券。</summary>
        [HttpPut]
        [Route("{couponId:int}")]
        public IActionResult Update(int couponId, [FromBody] CouponUpdateRequest req)
        {
            _service.Update(User.GetSId(), couponId, req);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "優惠券修改成功", Result = null });
        }

        /// <summary>上下架優惠券。</summary>
        [HttpPatch]
        [Route("{couponId:int}/status")]
        public IActionResult UpdateStatus(int couponId, [FromBody] CouponStatusUpdateRequest req)
        {
            _service.UpdateStatus(User.GetSId(), couponId, req.status);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "優惠券狀態已更新", Result = null });
        }

        /// <summary>刪除優惠券。</summary>
        /// <remarks>交易內依序刪除 qrcode_coupon → user_coupon → coupon。</remarks>
        [HttpDelete]
        [Route("{couponId:int}")]
        public IActionResult Delete(int couponId)
        {
            _service.Delete(User.GetSId(), couponId);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "優惠券已刪除", Result = null });
        }
    }
}
