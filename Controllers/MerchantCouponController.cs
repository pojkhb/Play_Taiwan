using System.Collections.Generic;
using backend.Services;
using backend.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>商家端優惠券維護。</summary>
    [ApiController]
    [Route("api/merchant/coupons")]
    public class MerchantCouponController : ControllerBase
    {
        private readonly MerchantCouponService _service;

        public MerchantCouponController(MerchantCouponService service)
        {
            _service = service;
        }

        /// <summary>查詢商家優惠券列表。</summary>
        [HttpGet]
        [Route("")]
        public IActionResult GetByStore([FromQuery] int storeId)
        {
            List<CouponResponse> result = _service.GetByStore(storeId);
            return Ok(new ResultViewModel<List<CouponResponse>> { isSuccess = true, message = "查詢成功", Result = result });
        }

        /// <summary>查詢單筆優惠券。</summary>
        [HttpGet]
        [Route("{couponId:int}")]
        public IActionResult GetById(int couponId)
        {
            CouponResponse result = _service.GetById(couponId);
            return Ok(new ResultViewModel<CouponResponse> { isSuccess = true, message = "查詢成功", Result = result });
        }

        /// <summary>新增優惠券。</summary>
        [HttpPost]
        [Route("")]
        public IActionResult Create([FromBody] CouponCreateRequest req)
        {
            int couponId = _service.Create(req);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "優惠券新增成功", Result = new { coupon_id = couponId } });
        }

        /// <summary>修改優惠券。</summary>
        [HttpPut]
        [Route("{couponId:int}")]
        public IActionResult Update(int couponId, [FromBody] CouponUpdateRequest req)
        {
            _service.Update(couponId, req);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "優惠券修改成功", Result = null });
        }

        /// <summary>上下架優惠券。</summary>
        [HttpPatch]
        [Route("{couponId:int}/status")]
        public IActionResult UpdateStatus(int couponId, [FromBody] CouponStatusUpdateRequest req)
        {
            _service.UpdateStatus(couponId, req.status);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "優惠券狀態已更新", Result = null });
        }

        /// <summary>刪除優惠券。</summary>
        /// <remarks>交易內依序刪除 nfc_coupon → user_coupon → coupon。</remarks>
        [HttpDelete]
        [Route("{couponId:int}")]
        public IActionResult Delete(int couponId)
        {
            _service.Delete(couponId);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "優惠券已刪除", Result = null });
        }
    }
}
