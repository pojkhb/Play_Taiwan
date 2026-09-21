using System;
using System.Threading.Tasks;
using Dapper;
using backend.utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    /* 定義 Job 資料模型 */
    public class MediaJobModel
    {
        public string job_id { get; set; }
        public string owner_id { get; set; }
        public string job_type { get; set; }
        public string external_task_id { get; set; }
        public string status { get; set; }
        public string result_url { get; set; }
    }

    /* 定義 Job 資料庫操作 */
    public class MediaJobDao
    {
        private readonly string _connectionString;

        public MediaJobDao(IOptions<AppSettings> appSettings)
        {
            _connectionString = appSettings.Value.mydb;
        }

        public async Task InsertJobAsync(MediaJobModel job)
        {
            using var conn = new MySqlConnection(_connectionString);
            const string sql = @"
                INSERT INTO media_generation_job (job_id, owner_id, job_type, external_task_id, status, result_url)
                VALUES (@job_id, @owner_id, @job_type, @external_task_id, @status, @result_url)";
            await conn.ExecuteAsync(sql, job);
        }

        public async Task<MediaJobModel> GetJobAsync(string jobId)
        {
            using var conn = new MySqlConnection(_connectionString);
            const string sql = "SELECT * FROM media_generation_job WHERE job_id = @jobId LIMIT 1;";
            return await conn.QueryFirstOrDefaultAsync<MediaJobModel>(sql, new { jobId });
        }

        public async Task UpdateJobStatusAsync(string jobId, string status, string resultUrl)
        {
            using var conn = new MySqlConnection(_connectionString);
            const string sql = @"
                UPDATE media_generation_job 
                SET status = @status, result_url = @resultUrl 
                WHERE job_id = @jobId";
            await conn.ExecuteAsync(sql, new { jobId, status, resultUrl });
        }
    }
}