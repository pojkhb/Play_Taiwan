// 檔案路徑：System\dao\Merchant\VlogDao.cs
// 商家 VLOG：merchant_media（專案）、merchant_media_asset（照片）、narrative_tone（敘事語氣）、store（店家預設資料）。
// 欄位用法：mm_description = 商家輸入的推廣資訊、mm_text = AI 推薦配文、mm_hashtage = TAG（逗號分隔）。
// mm_status：1=草稿、2=處理中、3=已完成、4=失敗；外部 AI 任務 ID 存在 mm_task_id。
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using backend.Models;
using backend.utils;
using backend.ViewModels;

namespace backend.dao
{
    /// <summary>商家 VLOG 的資料存取層。</summary>
    public class VlogDao
    {
        private readonly AppSettings _appSettings;

        public VlogDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        private MySqlConnection Open() => new MySqlConnection(_appSettings.mydb);

        #region 敘事語氣 / 店家預設資料

        public async Task<List<NarrativeToneItem>> GetActiveTonesAsync()
        {
            using var conn = Open();
            return (await conn.QueryAsync<NarrativeToneItem>(
                "SELECT nt_id, nt_name, nt_prompt FROM narrative_tone WHERE is_active = 1 ORDER BY nt_id;")).ToList();
        }

        public async Task<NarrativeToneItem> GetToneAsync(int ntId)
        {
            using var conn = Open();
            return await conn.QueryFirstOrDefaultAsync<NarrativeToneItem>(
                "SELECT nt_id, nt_name, nt_prompt FROM narrative_tone WHERE nt_id = @ntId AND is_active = 1;", new { ntId });
        }

        public class StoreRow
        {
            public string store_name { get; set; }

            /// <summary>縣市 + 行政區 + 地址</summary>
            public string full_address { get; set; }
        }

        public async Task<StoreRow> GetStoreAsync(int auId)
        {
            using var conn = Open();
            return await conn.QueryFirstOrDefaultAsync<StoreRow>(@"
                SELECT store_name,
                       NULLIF(CONCAT(IFNULL(store_city, ''), IFNULL(store_town, ''), IFNULL(store_address, '')), '') AS full_address
                FROM store
                WHERE au_id = @auId;", new { auId });
        }

        #endregion

        #region 專案

        public async Task<MerchantMedia> GetMediaAsync(int mmId, int auId)
        {
            using var conn = Open();
            return await conn.QueryFirstOrDefaultAsync<MerchantMedia>(@"
                SELECT mm_id, au_id, nt_id, mm_task_id, mm_title, mm_description, mm_text, mm_hashtage,
                       mm_video_url, mm_thumbnail, mm_aspect, mm_status, error_message, created_at, updated_at
                FROM merchant_media
                WHERE mm_id = @mmId AND au_id = @auId;", new { mmId, auId });
        }

        /// <summary>新增草稿專案，回傳 mm_id</summary>
        public async Task<int> CreateDraftAsync(int auId, int? ntId, string title, string description, string caption, string hashtags)
        {
            using var conn = Open();
            return await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO merchant_media (au_id, nt_id, mm_title, mm_description, mm_text, mm_hashtage, mm_status)
                VALUES (@auId, @ntId, @title, @description, @caption, @hashtags, 1);
                SELECT LAST_INSERT_ID();", new { auId, ntId, title, description, caption, hashtags });
        }

        /// <summary>重新產生草稿：覆寫輸入與 AI 結果，狀態回到草稿</summary>
        public async Task UpdateDraftAsync(int mmId, int? ntId, string title, string description, string caption, string hashtags)
        {
            using var conn = Open();
            await conn.ExecuteAsync(@"
                UPDATE merchant_media
                SET nt_id = @ntId, mm_title = @title, mm_description = @description,
                    mm_text = @caption, mm_hashtage = @hashtags,
                    mm_status = 1, mm_task_id = NULL, error_message = NULL
                WHERE mm_id = @mmId;", new { mmId, ntId, title, description, caption, hashtags });
        }

        public async Task MarkProcessingAsync(int mmId, string taskId, string caption, string hashtags)
        {
            using var conn = Open();
            await conn.ExecuteAsync(@"
                UPDATE merchant_media
                SET mm_task_id = @taskId, mm_text = @caption, mm_hashtage = @hashtags,
                    mm_video_url = NULL, mm_status = 2, error_message = NULL
                WHERE mm_id = @mmId;", new { mmId, taskId, caption, hashtags });
        }

        public async Task MarkCompletedAsync(int mmId, string videoUrl)
        {
            using var conn = Open();
            await conn.ExecuteAsync(@"
                UPDATE merchant_media
                SET mm_video_url = @videoUrl, mm_status = 3, error_message = NULL
                WHERE mm_id = @mmId;", new { mmId, videoUrl });
        }

        public async Task MarkFailedAsync(int mmId, string errorMessage)
        {
            using var conn = Open();
            await conn.ExecuteAsync(@"
                UPDATE merchant_media
                SET mm_status = 4, error_message = @errorMessage
                WHERE mm_id = @mmId;", new { mmId, errorMessage });
        }

        #endregion

        #region 照片

        /// <summary>依影片順序取出專案照片網址</summary>
        public async Task<List<string>> GetAssetUrlsAsync(int mmId)
        {
            using var conn = Open();
            return (await conn.QueryAsync<string>(@"
                SELECT asset_url FROM merchant_media_asset
                WHERE mm_id = @mmId AND asset_type = 1
                ORDER BY sort_order, mma_id;", new { mmId })).ToList();
        }

        /// <summary>整批換掉專案照片，回傳被換掉的舊網址（給上層刪檔）</summary>
        public async Task<List<string>> ReplaceAssetsAsync(int auId, int mmId, List<string> urls)
        {
            using var conn = Open();
            await conn.OpenAsync();
            using var tx = await conn.BeginTransactionAsync();

            var old = (await conn.QueryAsync<string>(
                "SELECT asset_url FROM merchant_media_asset WHERE mm_id = @mmId;", new { mmId }, tx)).ToList();

            await conn.ExecuteAsync("DELETE FROM merchant_media_asset WHERE mm_id = @mmId;", new { mmId }, tx);
            await conn.ExecuteAsync(@"
                INSERT INTO merchant_media_asset (au_id, mm_id, asset_url, asset_type, sort_order)
                VALUES (@auId, @mmId, @url, 1, @sortOrder);",
                urls.Select((url, i) => new { auId, mmId, url, sortOrder = i + 1 }), tx);

            await tx.CommitAsync();
            return old;
        }

        #endregion
    }
}
