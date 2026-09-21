using System.Collections.Generic;
using System.Threading.Tasks;
using backend.Services.Neo4j;
using backend.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>商家註冊時，與 Neo4j 景點資料比對／綁定。</summary>
    [ApiController]
    [Route("api/merchant")]
    public class MerchantPlaceController : ControllerBase
    {
        private readonly PlaceVersionChainService _placeService;

        public MerchantPlaceController(PlaceVersionChainService placeService)
        {
            _placeService = placeService;
        }

        /// <summary>搜尋既有景點。</summary>
        /// <remarks>用店名/地址做模糊比對，回傳候選清單供商家在註冊畫面挑選；查無結果代表可能要建立新景點。</remarks>
        [HttpGet]
        [Route("register/search-place")]
        public async Task<IActionResult> SearchPlace([FromQuery] string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return Ok(new ResultViewModel<List<PlaceSearchResultItem>>
                {
                    isSuccess = true,
                    message = "請輸入關鍵字",
                    Result = new List<PlaceSearchResultItem>()
                });
            }

            List<PlaceSearchResultItem> result = await _placeService.SearchPlacesAsync(keyword);
            return Ok(new ResultViewModel<List<PlaceSearchResultItem>>
            {
                isSuccess = true,
                message = "查詢成功",
                Result = result
            });
        }
    }
}
