using System.Collections.Generic;
using System.Threading.Tasks;
using backend.Services;
using backend.Services.Neo4j;
using backend.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>商家註冊時，與 Neo4j 景點資料比對／綁定，以及自建景點的地址、座標互轉。</summary>
    [ApiController]
    [Route("api/merchant")]
    public class MerchantPlaceController : ControllerBase
    {
        private readonly PlaceVersionChainService _placeService;
        private readonly StoreLocationService _storeLocation;

        public MerchantPlaceController(PlaceVersionChainService placeService, StoreLocationService storeLocation)
        {
            _placeService = placeService;
            _storeLocation = storeLocation;
        }

        /// <summary>地址轉座標（自建景點表單自動帶出座標用）。</summary>
        /// <remarks>
        /// 不用登入（註冊畫面使用）。用「縣市＋鄉鎮市區＋地址」轉座標（TDX 內政部門牌資料），回傳的欄位名稱與註冊請求相同，可直接填回表單。
        /// store_city、store_town 可以不帶，由地址開頭判斷；有帶時地址要在該鄉鎮市區內。
        /// 對不到門牌時回 400，這時請商家地址與座標兩個都填。
        ///
        ///     GET /api/merchant/register/address-to-location?store_city=臺中市&amp;store_town=西區&amp;store_address=英才路600號
        /// </remarks>
        [HttpGet]
        [Route("register/address-to-location")]
        [ProducesResponseType(typeof(ResultViewModel<MerchantPlaceLocation>), 200)]
        public async Task<IActionResult> AddressToLocation([FromQuery] string store_city, [FromQuery] string store_town, [FromQuery] string store_address)
        {
            return Ok(new ResultViewModel<MerchantPlaceLocation>
            {
                isSuccess = true,
                message = "轉換成功",
                Result = await _storeLocation.FromAddressAsync(store_city, store_town, store_address)
            });
        }

        /// <summary>座標轉地址（自建景點表單自動帶出地址用）。</summary>
        /// <remarks>
        /// 不用登入（註冊畫面使用）。找座標最近的門牌當地址（TDX 內政部門牌資料），連同縣市、鄉鎮市區一起回傳，座標維持原本的值。
        /// 300 公尺內沒有門牌時回 400，這時請商家地址與座標兩個都填。
        ///
        ///     GET /api/merchant/register/location-to-address?lat=24.14152&amp;lng=120.66569
        /// </remarks>
        [HttpGet]
        [Route("register/location-to-address")]
        [ProducesResponseType(typeof(ResultViewModel<MerchantPlaceLocation>), 200)]
        public async Task<IActionResult> LocationToAddress([FromQuery] double? lat, [FromQuery] double? lng)
        {
            if (!lat.HasValue || !lng.HasValue)
            {
                throw new backend.Models.BadRequestException("請提供 lat、lng");
            }

            return Ok(new ResultViewModel<MerchantPlaceLocation>
            {
                isSuccess = true,
                message = "轉換成功",
                Result = await _storeLocation.FromCoordinatesAsync(null, null, lat.Value, lng.Value)
            });
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
