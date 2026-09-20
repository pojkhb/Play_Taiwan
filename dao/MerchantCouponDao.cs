using System.Collections.Generic;
using System.Linq;
using Dapper;
using backend.utils;
using backend.ViewModels;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class MerchantCouponDao
    {
        private readonly AppSettings _appSettings;

        public MerchantCouponDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        public List<CouponResponse> GetByStore(int storeId)
        {
            const string sql = @"
                SELECT coupon_id, s_id, coupon_code, coupon_name, discount_commodity,
                       discount_type, discount_value, valid_from, valid_to, status
                FROM coupon
                WHERE s_id = @storeId
                ORDER BY coupon_id DESC;
            ";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            return connection.Query<CouponResponse>(sql, new { storeId }).ToList();
        }

        public CouponResponse GetById(int couponId)
        {
            const string sql = @"
                SELECT coupon_id, s_id, coupon_code, coupon_name, discount_commodity,
                       discount_type, discount_value, valid_from, valid_to, status
                FROM coupon
                WHERE coupon_id = @couponId
                LIMIT 1;
            ";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            return connection.QueryFirstOrDefault<CouponResponse>(sql, new { couponId });
        }

        public int Create(CouponCreateRequest req)
        {
            const string sql = @"
                INSERT INTO coupon
                    (s_id, coupon_code, coupon_name, discount_commodity, discount_type,
                     discount_value, valid_from, valid_to, status)
                VALUES
                    (@s_id, @coupon_code, @coupon_name, @discount_commodity, @discount_type,
                     @discount_value, @valid_from, @valid_to, 'active');
                SELECT LAST_INSERT_ID();
            ";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            return connection.ExecuteScalar<int>(sql, req);
        }

        public bool Update(int couponId, CouponUpdateRequest req)
        {
            const string sql = @"
                UPDATE coupon
                SET coupon_code = @coupon_code,
                    coupon_name = @coupon_name,
                    discount_commodity = @discount_commodity,
                    discount_type = @discount_type,
                    discount_value = @discount_value,
                    valid_from = @valid_from,
                    valid_to = @valid_to
                WHERE coupon_id = @couponId;
            ";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            int affected = connection.Execute(sql, new
            {
                couponId,
                req.coupon_code,
                req.coupon_name,
                req.discount_commodity,
                req.discount_type,
                req.discount_value,
                req.valid_from,
                req.valid_to
            });
            return affected > 0;
        }

        public bool UpdateStatus(int couponId, string status)
        {
            const string sql = "UPDATE coupon SET status = @status WHERE coupon_id = @couponId;";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            return connection.Execute(sql, new { couponId, status }) > 0;
        }

        /// <summary>
        /// 刪除優惠券：Transaction 內依序刪 nfc_coupon → user_coupon → coupon，比照 StoryDao 的
        /// BeginTransaction/Commit/Rollback 寫法，避免斷頭資料。
        /// </summary>
        public bool Delete(int couponId)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                connection.Execute("DELETE FROM nfc_coupon WHERE coupon_id = @couponId;", new { couponId }, transaction);
                connection.Execute("DELETE FROM user_coupon WHERE coupon_id = @couponId;", new { couponId }, transaction);
                int affected = connection.Execute("DELETE FROM coupon WHERE coupon_id = @couponId;", new { couponId }, transaction);

                transaction.Commit();
                return affected > 0;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
