using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using backend.Models;
using backend.utils;
using backend.ViewModels;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    /// <summary>QR Code 與優惠券對照（qrcode_coupon）、使用者領券與核銷（user_coupon）。</summary>
    public class MerchantQrCodeDao
    {
        private readonly AppSettings _appSettings;

        public MerchantQrCodeDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        /// <summary>
        /// 綁定 QR Code 與優惠券，檢查與寫入包在同一個 Transaction 內，同時送來的綁定也擋得住：
        /// 一張優惠券只能綁一個 qr_uid、一個 qr_uid 只能綁一張優惠券，違反時丟 ConflictException（409）。
        /// </summary>
        public void Bind(int sId, string qrUid, int couponId)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                // 鎖住優惠券，同一張優惠券同時綁不同 qr_uid 時，後到的要等先到的寫完才往下檢查。
                // 這是交易內第一個讀取（鎖定讀取不建快照），後面的一般 SELECT 才看得到先到那筆已經寫入的綁定
                int? ownedCouponId = connection.ExecuteScalar<int?>(
                    "SELECT coupon_id FROM coupon WHERE coupon_id = @couponId AND s_id = @sId FOR UPDATE;",
                    new { couponId, sId }, transaction);
                if (ownedCouponId == null) throw new NotFoundException($"找不到 coupon_id={couponId} 的優惠券");

                int qrUidCount = connection.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM qrcode_coupon WHERE qr_uid = @qrUid;", new { qrUid }, transaction);
                if (qrUidCount > 0) throw new ConflictException($"QR Code {qrUid} 已經綁定過優惠券");

                string boundQrUid = connection.ExecuteScalar<string>(
                    "SELECT qr_uid FROM qrcode_coupon WHERE coupon_id = @couponId LIMIT 1;", new { couponId }, transaction);
                if (boundQrUid != null)
                {
                    throw new ConflictException($"優惠券 coupon_id={couponId} 已經綁定 QR Code {boundQrUid}，一張優惠券只能綁一個 QR Code");
                }

                connection.Execute(@"
                    INSERT INTO qrcode_coupon (qr_uid, coupon_id, used_count)
                    VALUES (@qrUid, @couponId, 0);", new { qrUid, couponId }, transaction);

                transaction.Commit();
            }
            // 同一個 qr_uid 同時綁不同優惠券：鎖的是不同優惠券擋不到，由 qr_uid 主鍵擋下
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                transaction.Rollback();
                throw new ConflictException($"QR Code {qrUid} 已經綁定過優惠券");
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// 掃描 QR Code：qrcode_coupon → coupon → store 一次 join 取出優惠券內容與商家的 store_uid。
        /// </summary>
        public (CouponResponse coupon, string storeUid) GetScanInfo(string qrUid)
        {
            const string sql = @"
                SELECT
                    c.coupon_id, c.s_id, c.coupon_code, c.coupon_name, c.discount_commodity,
                    c.discount_type, c.discount_value, c.valid_from, c.valid_to, c.status,
                    s.store_uid AS StoreUid
                FROM qrcode_coupon qc
                JOIN coupon c ON c.coupon_id = qc.coupon_id
                JOIN store s ON s.s_id = c.s_id
                WHERE qc.qr_uid = @qrUid
                LIMIT 1;
            ";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();

            var row = connection.QueryFirstOrDefault<dynamic>(sql, new { qrUid });
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

        public class UserCouponRow
        {
            public int coupon_id { get; set; }
            public int s_id { get; set; }
            public string coupon_code { get; set; }
            public string coupon_name { get; set; }
            public string discount_commodity { get; set; }
            public string discount_type { get; set; }
            public decimal discount_value { get; set; }
            public DateTime? valid_from { get; set; }
            public DateTime? valid_to { get; set; }
            public string status { get; set; }
            public string store_name { get; set; }
            public string full_address { get; set; }
            public string store_uid { get; set; }
            public DateTime obtained_at { get; set; }
            public bool is_used { get; set; }
            public DateTime? used_at { get; set; }
            public bool is_usable { get; set; }
        }

        /// <summary>
        /// 使用者領過的全部優惠券（含已核銷、已下架、已過期），新領的排前面。
        /// is_usable 用資料庫的 NOW() 判斷，跟核銷時間 used_at 同一個時鐘。
        /// </summary>
        public List<UserCouponRow> GetUserCoupons(int auId)
        {
            const string sql = @"
                SELECT
                    c.coupon_id, c.s_id, c.coupon_code, c.coupon_name, c.discount_commodity,
                    c.discount_type, c.discount_value, c.valid_from, c.valid_to, c.status,
                    s.store_name,
                    NULLIF(CONCAT(IFNULL(s.store_city, ''), IFNULL(s.store_town, ''), IFNULL(s.store_address, '')), '') AS full_address,
                    s.store_uid,
                    uc.obtained_at, uc.is_used = 1 AS is_used, uc.used_at,
                    (uc.is_used = 0
                     AND c.status = 'active'
                     AND (c.valid_from IS NULL OR c.valid_from <= NOW())
                     AND (c.valid_to IS NULL OR c.valid_to >= NOW())) AS is_usable
                FROM user_coupon uc
                JOIN coupon c ON c.coupon_id = uc.coupon_id
                JOIN store s ON s.s_id = c.s_id
                WHERE uc.au_id = @auId
                ORDER BY uc.obtained_at DESC, uc.uc_id DESC;
            ";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            return connection.Query<UserCouponRow>(sql, new { auId }).ToList();
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
        /// 核銷優惠券：更新 user_coupon.is_used/used_at，並遞增該優惠券綁定的 qrcode_coupon.used_count。
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
                            UPDATE qrcode_coupon
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
