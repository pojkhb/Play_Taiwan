using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.Services.Neo4j;
using backend.utils;
using backend.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Services
{
    public class MerchantAccountService
    {
        private readonly MerchantAccountDao _dao;
        private readonly PlaceVersionChainService _placeService;
        private readonly AppSettings _appSettings;
        private readonly ILogger<MerchantAccountService> _logger;

        public MerchantAccountService(
            MerchantAccountDao dao,
            PlaceVersionChainService placeService,
            IOptions<AppSettings> appSettings,
            ILogger<MerchantAccountService> logger)
        {
            _dao = dao;
            _placeService = placeService;
            _appSettings = appSettings.Value;
            _logger = logger;
        }

        public async Task<MerchantRegisterResponse> RegisterAsync(MerchantRegisterRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.auth_email) || string.IsNullOrWhiteSpace(req.auth_pswd))
            {
                throw new System.Exception("請提供 Email 與密碼");
            }

            bool hasExistingPlace = !string.IsNullOrWhiteSpace(req.place_uid);
            bool hasNewPlace = req.new_place != null;

            if (hasExistingPlace == hasNewPlace)
            {
                throw new System.Exception("請擇一提供 place_uid（選擇既有景點）或 new_place（建立新景點）");
            }

            string storeUid;
            if (hasExistingPlace)
            {
                PlaceCurrentInfo existing = await _placeService.GetCurrentVersionAsync(req.place_uid);
                if (existing == null)
                {
                    throw new NotFoundException($"找不到 uid={req.place_uid} 的景點，請重新搜尋");
                }
                storeUid = req.place_uid;
            }
            else
            {
                storeUid = await _placeService.CreateMerchantPlaceAsync(req.new_place, req.store_name);
            }

            var hashTool = new sha256Hash();
            string passwordHash = hashTool.getSha256(req.auth_pswd, _appSettings.hash_key);

            (int auId, int sId) = _dao.RegisterMerchant(req, passwordHash, storeUid);

            return new MerchantRegisterResponse { au_id = auId, s_id = sId, store_uid = storeUid };
        }

        public MerchantDetailResponse GetById(int sId)
        {
            MerchantDetailResponse merchant = _dao.GetById(sId);
            if (merchant == null) throw new NotFoundException($"找不到 s_id={sId} 的商家資料");
            return merchant;
        }

        public async Task UpdateAsync(int sId, MerchantUpdateRequest req)
        {
            string storeUid = _dao.GetStoreUid(sId);
            if (storeUid == null && _dao.GetById(sId) == null)
            {
                throw new NotFoundException($"找不到 s_id={sId} 的商家資料");
            }

            _dao.Update(sId, req);

            if (!string.IsNullOrWhiteSpace(storeUid))
            {
                var fields = new MerchantPlaceFields
                {
                    name = req.store_name,
                    address = req.store_address,
                    description = req.store_dec
                };
                await _placeService.CreateNewVersionAsync(storeUid, fields, "merchant", sId.ToString());
            }
        }

        public MerchantListResponse List(MerchantListQuery query)
        {
            (var items, int total) = _dao.List(query);
            return new MerchantListResponse
            {
                items = items,
                total = total,
                page = query.page,
                page_size = query.page_size
            };
        }

        /// <summary>
        /// 商家整體刪除：MySQL 端在單一 Transaction 內完成（見 MerchantAccountDao.DeleteCascade）；
        /// Neo4j 沒有跨資料庫交易保護，因此放在 MySQL commit 成功之後才執行，
        /// 失敗只記 log，不影響已經完成的 MySQL 刪除結果（此階段尚未做兩階段提交）。
        /// </summary>
        public async Task DeleteAsync(int sId)
        {
            string storeUid = _dao.DeleteCascade(sId);

            if (!string.IsNullOrWhiteSpace(storeUid))
            {
                try
                {
                    await _placeService.DeleteMerchantVersionAsync(storeUid);
                }
                catch (System.Exception ex)
                {
                    _logger.LogError(ex, "商家 s_id={SId} 的 MySQL 資料已刪除，但清理 Neo4j uid={Uid} 失敗", sId, storeUid);
                }
            }
        }
    }
}
