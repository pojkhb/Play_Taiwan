// 檔案路徑：System\dao\Map\NearbyFoodDao.cs
// 用餐時間推播：MySQL 裡的美食店家（place）與店家目前可領的優惠券（store → coupon）
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using backend.Models;
using backend.utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class NearbyFoodDao
    {
        private readonly AppSettings _appSettings;

        public NearbyFoodDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        /// <summary>MySQL place 表的一間美食</summary>
        public class FoodPlaceRow
        {
            public int p_id { get; set; }
            public string uid { get; set; }          // place_type 依名稱對到的 Neo4j uid，沒有時為 null
            public string p_name { get; set; }
            public string p_address { get; set; }
            public string p_type { get; set; }
            public double lat { get; set; }
            public double lng { get; set; }
            public string p_open_time { get; set; }
            public string p_image { get; set; }
        }

        /// <summary>
        /// 範圍內的美食：分類是「飲食」，或 place_type 標成 Restaurant（含商家註冊時自建的餐廳）。
        /// 只用外框粗篩，距離由呼叫端計算。
        /// </summary>
        public async Task<List<FoodPlaceRow>> GetFoodPlacesInBoundsAsync(double minLat, double maxLat, double minLon, double maxLon)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            return (await conn.QueryAsync<FoodPlaceRow>(@"
                SELECT p.p_id, p.p_name, p.p_address, p.p_type,
                       p.p_latitude AS lat, p.p_longitude AS lng,
                       NULLIF(p.p_open_time, '') AS p_open_time, NULLIF(p.p_image, '') AS p_image,
                       (SELECT MIN(pt.place_id) FROM place_type pt WHERE pt.place_name = p.p_name) AS uid
                FROM place p
                WHERE p.p_latitude BETWEEN @minLat AND @maxLat
                  AND p.p_longitude BETWEEN @minLon AND @maxLon
                  AND (p.p_category = '飲食'
                       OR EXISTS (SELECT 1 FROM place_type pt
                                  WHERE pt.place_name = p.p_name AND pt.place_category = 'Restaurant'));",
                new { minLat, maxLat, minLon, maxLon })).ToList();
        }

        /// <summary>這些景點（Neo4j uid）的店家目前上架、在有效期內的優惠券，依 uid 分組</summary>
        public async Task<ILookup<string, NearbyFoodCoupon>> GetActiveCouponsAsync(IEnumerable<string> placeUids)
        {
            List<string> uids = (placeUids ?? Enumerable.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();
            if (uids.Count == 0) return Enumerable.Empty<(string, NearbyFoodCoupon)>().ToLookup(x => x.Item1, x => x.Item2);

            using var conn = new MySqlConnection(_appSettings.mydb);
            var rows = await conn.QueryAsync<CouponRow>(@"
                SELECT st.store_uid, c.coupon_id, c.coupon_name, c.discount_commodity, c.discount_type, c.discount_value, c.valid_to
                FROM coupon c
                JOIN store st ON st.s_id = c.s_id
                WHERE st.store_uid IN @uids
                  AND c.status = 'active'
                  AND (c.valid_from IS NULL OR c.valid_from <= NOW())
                  AND (c.valid_to IS NULL OR c.valid_to >= NOW())
                ORDER BY c.coupon_id;", new { uids });

            return rows.ToLookup(r => r.store_uid, r => new NearbyFoodCoupon
            {
                coupon_id = r.coupon_id,
                coupon_name = r.coupon_name,
                discount_commodity = r.discount_commodity,
                discount_type = r.discount_type,
                discount_value = r.discount_value,
                valid_to = r.valid_to
            });
        }

        private class CouponRow
        {
            public string store_uid { get; set; }
            public int coupon_id { get; set; }
            public string coupon_name { get; set; }
            public string discount_commodity { get; set; }
            public string discount_type { get; set; }
            public decimal discount_value { get; set; }
            public System.DateTime? valid_to { get; set; }
        }
    }
}
