using System.Collections.Generic;
using System.Threading.Tasks;
using backend.Services;
using backend.util;
using backend.utils;
using backend.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>使用者端優惠券：查看自己擁有的優惠券、核銷。</summary>
    [Authorize]
    [ApiController]
    [Route("api/coupons")]
    public class CouponRedeemController : ControllerBase
    {
        private readonly MerchantQrCodeService _service;

        public CouponRedeemController(MerchantQrCodeService service)
        {
            _service = service;
        }

        /// <summary>查詢登入者擁有的優惠券。</summary>
        /// <remarks>
        /// 回傳登入者（JWT 的 au_id）掃 QR Code 領過的全部優惠券，新領的排前面，含已核銷、已下架、已過期的，
        /// 前端可以依狀態分成「可使用／已使用／已失效」：
        ///
        /// - is_usable：現在能不能用（還沒核銷、商家沒有下架、在有效期間內）
        /// - is_used／used_at：是否已核銷與核銷時間
        /// - image_url：店家照片（完整網址），跟掃 QR Code 拿到的是同一張；沒有照片時為 null
        /// - full_address：店家完整地址（縣市＋鄉鎮市區＋地址）
        /// - operating_hours：店家營業時間，格式同掃 QR Code 的 operating_hours；沒有時（或 Neo4j 連不上）為空陣列
        /// </remarks>
        [HttpGet]
        [Route("")]
        public async Task<IActionResult> GetMine()
        {
            List<MyCouponResponse> result = await _service.GetMyCouponsAsync(User.GetAuId());
            foreach (MyCouponResponse item in result)
                item.image_url = PublicUrl.Of(Request, item.image_url);
            return Ok(new ResultViewModel<List<MyCouponResponse>> { isSuccess = true, message = "查詢成功", Result = result });
        }

        /// <summary>核銷優惠券。</summary>
        /// <remarks>
        /// 核銷登入者（JWT 的 au_id）自己領取的優惠券：更新 user_coupon.is_used/used_at，
        /// 並遞增 qrcode_coupon.used_count。不需要 request body。
        /// </remarks>
        [HttpPost]
        [Route("{couponId:int}/redeem")]
        public IActionResult Redeem(int couponId)
        {
            _service.Redeem(couponId, User.GetAuId());
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "優惠券核銷成功", Result = null });
        }
    }
}
