using System;
using Dapper;
using backend.utils;
using backend.ViewModels;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class MerchantNfcDao
    {
        private readonly AppSettings _appSettings;

        public MerchantNfcDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        public bool ExistsNfcUid(string nfcUid)
        {
            const string sql = "SELECT COUNT(1) FROM nfc_coupon WHERE nfc_uid = @nfcUid;";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            return connection.ExecuteScalar<int>(sql, new { nfcUid }) > 0;
        }

        public void Bind(string nfcUid, int couponId)
        {
            const string sql = @"
                INSERT INTO nfc_coupon (nfc_uid, coupon_id, used_count)
                VALUES (@nfcUid, @couponId, 0);
            ";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            connection.Execute(sql, new { nfcUid, couponId });
        }

        /// <summary>
        /// 掃描 NFC 貼紙：nfc_coupon → coupon → store 一次 join 取出優惠券內容與商家的 store_uid。
        /// </summary>
        public (CouponResponse coupon, string storeUid) GetScanInfo(string nfcUid)
        {
            const string sql = @"
                SELECT
                    c.coupon_id, c.s_id, c.coupon_code, c.coupon_name, c.discount_commodity,
                    c.discount_type, c.discount_value, c.valid_from, c.valid_to, c.status,
                    s.store_uid AS StoreUid
                FROM nfc_coupon nc
                JOIN coupon c ON c.coupon_id = nc.coupon_id
                JOIN store s ON s.s_id = c.s_id
                WHERE nc.nfc_uid = @nfcUid
                LIMIT 1;
            ";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();

            var row = connection.QueryFirstOrDefault<dynamic>(sql, new { nfcUid });
            if (row == null) return (null, null);

            var coupon = new CouponResponse
            {
                coupon_id = row.coupon_id,
                s_id = row.s_id,
                coupon_code = row.coupon_code,
                coupon_name = row.coupon_name,
                discount_commodity = row.discount_commodity,
                discount_type = row.discount_type,
                discount_value = row.discount_value,
                valid_from = row.valid_from,
                valid_to = row.valid_to,
                status = row.status
            };
            string storeUid = row.StoreUid;
            return (coupon, storeUid);
        }

        public bool HasClaimed(int auId, int couponId)
        {
            const string sql = "SELECT COUNT(1) FROM user_coupon WHERE au_id = @auId AND coupon_id = @couponId;";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            return connection.ExecuteScalar<int>(sql, new { auId, couponId }) > 0;
        }

        public void ClaimCoupon(int auId, int couponId)
        {
            const string sql = @"
                INSERT INTO user_coupon (au_id, coupon_id, obtained_at, is_used)
                VALUES (@auId, @couponId, NOW(), 0);
            ";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            connection.Execute(sql, new { auId, couponId });
        }

        /// <summary>
        /// 核銷優惠券：更新 user_coupon.is_used/used_at，並遞增該優惠券綁定的 nfc_coupon.used_count。
        /// 兩個更新包在同一個 Transaction 內。
        /// </summary>
        public bool Redeem(int couponId, int auId)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                int affected = connection.Execute(@"
                        UPDATE user_coupon
                        SET is_used = 1, used_at = NOW()
                        WHERE au_id = @auId AND coupon_id = @couponId AND is_used = 0;
                    ", new { auId, couponId }, transaction);

                if (affected > 0)
                {
                    connection.Execute(@"
                            UPDATE nfc_coupon
                            SET used_count = used_count + 1
                            WHERE coupon_id = @couponId;
                        ", new { couponId }, transaction);
                }

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
