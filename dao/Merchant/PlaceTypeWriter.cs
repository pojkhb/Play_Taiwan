// 檔案路徑：System\dao\Merchant\PlaceTypeWriter.cs
// place_type（景點 ↔ 可出的任務類型）寫入工具，給商家註冊、商家題庫、刪除帳號共用，
// 一律在呼叫端的交易內執行。
// place_type.place_type_id 沒有自動遞增、(place_id, type_id) 也沒有唯一鍵，
// 所以新增前先檢查是否已存在，編號用 MAX + 1 在交易內鎖住後自行產生（同 task_option 的做法）。
using System.Collections.Generic;
using System.Linq;
using Dapper;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public static class PlaceTypeWriter
    {
        /// <summary>商家知識問答：place_type 有這一列代表該景點的商家有題庫，劇本生成時會掛入商家題</summary>
        public const int MerchantQuizTypeId = 9;

        /// <summary>商家自建景點預設可出的通用任務類型：文化問答型、景點猜猜樂、e人訪談型</summary>
        public static readonly int[] GenericTypeIds = { 6, 7, 8 };

        /// <summary>為景點加上任務類型，已存在的 (place_id, type_id) 略過。</summary>
        public static void AddTypes(MySqlConnection conn, MySqlTransaction transaction,
            string placeId, string placeName, string placeCategory, IEnumerable<int> typeIds)
        {
            List<int> existing = conn.Query<int>(
                "SELECT type_id FROM place_type WHERE place_id = @placeId;",
                new { placeId }, transaction).ToList();

            List<int> toAdd = typeIds.Distinct().Where(id => !existing.Contains(id)).ToList();
            if (toAdd.Count == 0) return;

            int nextId = conn.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(place_type_id), 0) FROM place_type FOR UPDATE;", transaction: transaction);

            foreach (int typeId in toAdd)
            {
                conn.Execute(@"
                    INSERT INTO place_type (place_type_id, place_id, place_name, place_category, type_id)
                    VALUES (@id, @placeId, @placeName, @placeCategory, @typeId);",
                    new { id = ++nextId, placeId, placeName, placeCategory, typeId }, transaction);
            }
        }

        /// <summary>
        /// 商家新增題目後呼叫：確保 place_type 有 (store_uid, 9)。
        /// 景點名稱與分類沿用該景點既有的 place_type 列，沒有時用店名、分類 Restaurant。
        /// </summary>
        public static void EnsureMerchantQuiz(MySqlConnection conn, MySqlTransaction transaction, string storeUid, string storeName)
        {
            if (string.IsNullOrWhiteSpace(storeUid)) return;

            var place = conn.QueryFirstOrDefault<(string place_name, string place_category)>(@"
                SELECT MIN(place_name) AS place_name, MIN(place_category) AS place_category
                FROM place_type
                WHERE place_id = @storeUid;", new { storeUid }, transaction);

            AddTypes(conn, transaction, storeUid,
                place.place_name ?? storeName ?? "",
                place.place_category ?? "Restaurant",
                new[] { MerchantQuizTypeId });
        }

        /// <summary>
        /// 商家刪除題目或帳號後呼叫：這個景點已經沒有任何商家題目時，移除 (store_uid, 9)。
        /// 同一個景點可能綁了多間店，所以看的是所有 store_uid 相同的店家的題庫。
        /// </summary>
        public static void RemoveMerchantQuizIfEmpty(MySqlConnection conn, MySqlTransaction transaction, string storeUid)
        {
            if (string.IsNullOrWhiteSpace(storeUid)) return;

            int remaining = conn.ExecuteScalar<int>(@"
                SELECT COUNT(1)
                FROM store_question q
                INNER JOIN store s ON s.s_id = q.store_id
                WHERE s.store_uid = @storeUid;", new { storeUid }, transaction);

            if (remaining == 0)
            {
                conn.Execute(
                    "DELETE FROM place_type WHERE place_id = @storeUid AND type_id = @typeId;",
                    new { storeUid, typeId = MerchantQuizTypeId }, transaction);
            }
        }
    }
}
