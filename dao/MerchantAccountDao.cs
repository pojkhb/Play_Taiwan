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
    public class MerchantAccountDao
    {
        private readonly AppSettings _appSettings;

        public MerchantAccountDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        /// <summary>
        /// 註冊商家：建立 auth(auth_type=2, is_active=1) + store，兩張表在同一個 Transaction 內完成。
        /// 免審核直接啟用，同時把 is_email_verified 設為 1，讓商家可以直接用既有的登入 API 登入，
        /// 不用額外走一次信箱驗證流程。
        /// </summary>
        public (int auId, int sId) RegisterMerchant(MerchantRegisterRequest req, string passwordHash, string storeUid)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                const string insertAuthSql = @"
                    INSERT INTO auth (auth_name, auth_type, auth_email, auth_pswd, is_active, is_email_verified)
                    VALUES (@auth_name, 2, @auth_email, @auth_pswd, 1, 1);
                    SELECT LAST_INSERT_ID();
                ";
                int auId = connection.ExecuteScalar<int>(insertAuthSql, new
                {
                    auth_name = req.auth_name,
                    auth_email = req.auth_email,
                    auth_pswd = passwordHash
                }, transaction);

                const string insertStoreSql = @"
                    INSERT INTO store (au_id, store_name, store_dec, store_address, store_city, store_town, store_uid)
                    VALUES (@au_id, @store_name, @store_dec, @store_address, @store_city, @store_town, @store_uid);
                    SELECT LAST_INSERT_ID();
                ";
                int sId = connection.ExecuteScalar<int>(insertStoreSql, new
                {
                    au_id = auId,
                    store_name = req.store_name,
                    store_dec = req.store_dec,
                    store_address = req.store_address,
                    store_city = req.store_city,
                    store_town = req.store_town,
                    store_uid = storeUid
                }, transaction);

                transaction.Commit();
                return (auId, sId);
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                transaction.Rollback();
                throw new ConflictException("此 Email 已被註冊過");
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public MerchantDetailResponse GetById(int sId)
        {
            const string sql = @"
                SELECT
                    s.s_id, s.au_id, s.store_name, s.store_dec, s.store_address,
                    s.store_city, s.store_town, s.store_uid,
                    a.auth_name, a.auth_email, a.is_active
                FROM store s
                JOIN auth a ON a.au_id = s.au_id
                WHERE s.s_id = @sId
                LIMIT 1;
            ";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            return connection.QueryFirstOrDefault<MerchantDetailResponse>(sql, new { sId });
        }

        public string GetStoreUid(int sId)
        {
            const string sql = "SELECT store_uid FROM store WHERE s_id = @sId LIMIT 1;";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            return connection.QueryFirstOrDefault<string>(sql, new { sId });
        }

        public bool Update(int sId, MerchantUpdateRequest req)
        {
            const string sql = @"
                UPDATE store
                SET store_name = @store_name,
                    store_dec = @store_dec,
                    store_address = @store_address,
                    store_city = @store_city,
                    store_town = @store_town
                WHERE s_id = @sId;
            ";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            int affected = connection.Execute(sql, new
            {
                sId,
                store_name = req.store_name,
                store_dec = req.store_dec,
                store_address = req.store_address,
                store_city = req.store_city,
                store_town = req.store_town
            });
            return affected > 0;
        }

        public (List<MerchantDetailResponse> items, int total) List(MerchantListQuery query)
        {
            string keywordPattern = string.IsNullOrWhiteSpace(query.keyword) ? null : $"%{query.keyword}%";
            int offset = (Math.Max(query.page, 1) - 1) * Math.Max(query.page_size, 1);

            const string sql = @"
                SELECT
                    s.s_id, s.au_id, s.store_name, s.store_dec, s.store_address,
                    s.store_city, s.store_town, s.store_uid,
                    a.auth_name, a.auth_email, a.is_active
                FROM store s
                JOIN auth a ON a.au_id = s.au_id
                WHERE (@keyword IS NULL OR s.store_name LIKE @keyword OR s.store_address LIKE @keyword)
                ORDER BY s.s_id DESC
                LIMIT @pageSize OFFSET @offset;

                SELECT COUNT(1)
                FROM store s
                WHERE (@keyword IS NULL OR s.store_name LIKE @keyword OR s.store_address LIKE @keyword);
            ";

            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            using var multi = connection.QueryMultiple(sql, new
            {
                keyword = keywordPattern,
                pageSize = query.page_size,
                offset
            });

            var items = multi.Read<MerchantDetailResponse>().ToList();
            int total = multi.ReadFirst<int>();
            return (items, total);
        }

        /// <summary>
        /// 商家整體刪除，單一 Transaction 內依序處理：
        /// nfc_coupon → user_coupon → coupon → (檢查 task 引用) → question_option → store_question →
        /// store → auth。若有題目仍被 task 引用，整包 Rollback 並丟出 ConflictException。
        /// 回傳該商家的 store_uid，供呼叫端在 MySQL commit 成功後另外處理 Neo4j 側清理。
        /// </summary>
        public string DeleteCascade(int sId)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var store = connection.QueryFirstOrDefault<(int auId, string storeUid)>(
                    "SELECT au_id AS auId, store_uid AS storeUid FROM store WHERE s_id = @sId LIMIT 1;",
                    new { sId }, transaction);

                if (store.auId == 0 && store.storeUid == null)
                {
                    throw new NotFoundException($"找不到 s_id={sId} 的商家資料");
                }

                var couponIds = connection.Query<int>(
                    "SELECT coupon_id FROM coupon WHERE s_id = @sId;", new { sId }, transaction).ToList();

                if (couponIds.Count > 0)
                {
                    connection.Execute(
                        "DELETE FROM nfc_coupon WHERE coupon_id IN @couponIds;",
                        new { couponIds }, transaction);
                    connection.Execute(
                        "DELETE FROM user_coupon WHERE coupon_id IN @couponIds;",
                        new { couponIds }, transaction);
                }
                connection.Execute("DELETE FROM coupon WHERE s_id = @sId;", new { sId }, transaction);

                var questionIds = connection.Query<int>(
                    "SELECT question_id FROM store_question WHERE store_id = @sId;", new { sId }, transaction).ToList();

                if (questionIds.Count > 0)
                {
                    var conflictingTaskIds = connection.Query<int>(
                        "SELECT task_id FROM task WHERE question_id IN @questionIds;",
                        new { questionIds }, transaction).ToList();

                    if (conflictingTaskIds.Count > 0)
                    {
                        throw new ConflictException(
                            $"此商家的題庫仍被 {conflictingTaskIds.Count} 筆任務引用，無法刪除商家帳號",
                            conflictingTaskIds.Cast<object>());
                    }

                    connection.Execute(
                        "DELETE FROM question_option WHERE question_id IN @questionIds;",
                        new { questionIds }, transaction);
                    connection.Execute(
                        "DELETE FROM store_question WHERE store_id = @sId;", new { sId }, transaction);
                }

                connection.Execute("DELETE FROM store WHERE s_id = @sId;", new { sId }, transaction);
                connection.Execute("DELETE FROM auth WHERE au_id = @auId;", new { auId = store.auId }, transaction);

                transaction.Commit();
                return store.storeUid;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
