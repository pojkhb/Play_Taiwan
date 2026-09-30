// 檔案路徑：System\dao\Merchant\MerchantDao.cs
// 對應新資料表 `auth` + `store`（商家帳號與店家資料）與 `merchant_media`（商家影音），
// 取代舊的 ep_account.store_name / ep_vlog；原 MerchantAccountDao 的功能也併入這裡。
using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using backend.Models;
using backend.utils;
using backend.ViewModels;

namespace backend.dao
{
    public class MerchantDao
    {
        private readonly AppSettings _appSettings;

        public MerchantDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        #region 1. 商家註冊
        /// <summary>
        /// 註冊商家：建立 auth(auth_type=2, is_active=1) + store，在同一個 Transaction 內完成。
        /// 免審核直接啟用，同時把 is_email_verified 設為 1，讓商家可以直接登入，
        /// 不用額外走一次信箱驗證流程。
        /// newPlace 有值（商家自建景點）時，同一個交易再寫入 place_type（通用任務類型 6、7、8）。
        /// 座標只存在 Neo4j 的自建景點節點（:MerchantPlace），行程規劃、地圖、任務位置驗證都依 uid 向 Neo4j 查。
        /// </summary>
        public (int auId, int sId) RegisterMerchant(MerchantRegisterRequest req, string passwordHash, string storeUid, MerchantNewPlace newPlace)
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

                if (newPlace != null)
                {
                    string placeName = string.IsNullOrWhiteSpace(newPlace.name) ? req.store_name : newPlace.name;

                    PlaceTypeWriter.AddTypes(connection, transaction, storeUid, placeName, newPlace.category, PlaceTypeWriter.GenericTypeIds);
                }

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
        #endregion

        #region 2. 商家資料查詢 / 更新 / 列表
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

        /// <summary>
        /// 更新店家資料：只更新有帶值的欄位，沒帶（null）的欄位維持原值，
        /// 所以只改店名時只要帶 store_name。
        /// </summary>
        public bool Update(int sId, MerchantUpdateRequest req)
        {
            const string sql = @"
                UPDATE store
                SET store_name = COALESCE(@store_name, store_name),
                    store_dec = COALESCE(@store_dec, store_dec),
                    store_address = COALESCE(@store_address, store_address),
                    store_city = COALESCE(@store_city, store_city),
                    store_town = COALESCE(@store_town, store_town)
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

        /// <summary>
        /// 商家自建景點的店名變更後，同步 place_type.place_name（題型抽選與任務生成用的景點名稱）。
        /// 地址、介紹等內容在 Neo4j 的版本節點（PlaceVersionChainService），不存 MySQL。
        /// </summary>
        public void SyncSelfBuiltPlace(string storeUid, string placeName)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Execute(
                "UPDATE place_type SET place_name = @placeName WHERE place_id = @storeUid;",
                new { placeName, storeUid });
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
        #endregion

        #region 3. 刪除商家帳號
        /// <summary>
        /// 商家整體刪除，單一 Transaction 內依序處理：
        /// qrcode_coupon → user_coupon → coupon → (檢查 task 引用) → question_option → store_question →
        /// store → auth。若有題目仍被 task 引用，整包 Rollback 並丟出 ConflictException。
        /// 同時整理 place_type：景點已沒有商家題目時移除 (store_uid, 9)；
        /// selfBuiltPlace（商家自建景點）時移除該景點所有 place_type 列；座標保留在 Neo4j（節點標成已刪除），不再排入行程。
        /// 回傳該商家的 store_uid，供呼叫端在 MySQL commit 成功後另外處理 Neo4j 側清理。
        /// </summary>
        public string DeleteCascade(int sId, bool selfBuiltPlace)
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
                        "DELETE FROM qrcode_coupon WHERE coupon_id IN @couponIds;",
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

                PlaceTypeWriter.RemoveMerchantQuizIfEmpty(connection, transaction, store.storeUid);

                if (selfBuiltPlace && !string.IsNullOrWhiteSpace(store.storeUid))
                {
                    connection.Execute(
                        "DELETE FROM place_type WHERE place_id = @storeUid;", new { storeUid = store.storeUid }, transaction);
                }

                transaction.Commit();
                return store.storeUid;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        #endregion

        #region 4. 已經生成的影音檔案（依最後更新時間新到舊）
        public List<MerchantFileItem> GetMerchantFiles(int auId)
        {
            string sql = @"
                SELECT mm_id, mm_title, mm_video_url, mm_status, updated_at
                FROM merchant_media
                WHERE au_id = @auId
                ORDER BY updated_at DESC;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                conn.Open();
                return conn.Query<MerchantFileItem>(sql, new { auId }).ToList();
            }
        }
        #endregion
    }
}
