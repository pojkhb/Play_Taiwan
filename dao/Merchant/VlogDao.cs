// 檔案路徑：System\dao\Merchant\VlogDao.cs
// 對應新資料表 `merchant_media`，取代舊的 ep_vlog（商家端用法）。
// 外部 AI 服務的 task_id 存在 mm_task_id，狀態用 mm_status（1=草稿、2=處理中、3=已完成、4=失敗），
// 因此不再需要舊的 media_generation_job 來追蹤進度。
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using backend.Models;
using backend.utils;

namespace backend.dao
{
    /// <summary>商家 Vlog 合成任務的資料存取層。</summary>
    public class VlogDao
    {
        private readonly AppSettings _appSettings;

        public VlogDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        #region 依外部任務 ID 查詢，避免輪詢時重複寫入
        public async Task<MerchantMedia> GetByTaskIdAsync(string taskId)
        {
            string sql = @"
                SELECT mm_id, au_id, mm_task_id, mm_title, mm_video_url,
                       mm_thumbnail, mm_status, created_at, updated_at
                FROM merchant_media
                WHERE mm_task_id = @taskId
                LIMIT 1;
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                await conn.OpenAsync();
                return await conn.QueryFirstOrDefaultAsync<MerchantMedia>(sql, new { taskId });
            }
        }
        #endregion

        #region 影片合成完成後寫入完成紀錄
        public async Task<int> CreateCompletedRecordAsync(
            int auId, string taskId, string videoUrl, string thumbnailUrl)
        {
            string sql = @"
                INSERT INTO merchant_media
                    (au_id, mm_task_id, mm_video_url, mm_thumbnail, mm_status)
                VALUES
                    (@auId, @taskId, @videoUrl, @thumbnailUrl, 3);
                SELECT LAST_INSERT_ID();
            ";

            using (var conn = new MySqlConnection(_appSettings.mydb))
            {
                await conn.OpenAsync();
                return await conn.ExecuteScalarAsync<int>(
                    sql, new { auId, taskId, videoUrl, thumbnailUrl });
            }
        }
        #endregion
    }
}
