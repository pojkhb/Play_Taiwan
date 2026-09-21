using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using backend.Models;

namespace backend.dao
{
    /// <summary>任務線索提示 (md_task_hint) 資料存取物件。</summary>
    public class TaskHintDao
    {
        private readonly string _connectionString;

        public TaskHintDao(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MySqlConnection");
        }

        /// <summary>依任務取得所有啟用中的提示階段設定，依 hint_stage 排序。</summary>
        public async Task<List<TaskHint>> GetByTaskIdAsync(string taskId)
        {
            var list = new List<TaskHint>();
            const string sql = @"SELECT hint_id, task_id, hint_stage, trigger_wrong_count,
                                         hint_text, llm_prompt_template, is_active
                                  FROM md_task_hint
                                  WHERE task_id = @task_id AND is_active = 1
                                  ORDER BY hint_stage";

            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@task_id", taskId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new TaskHint
                {
                    HintId = reader.GetInt32("hint_id"),
                    TaskId = reader.GetString("task_id"),
                    HintStage = reader.GetInt32("hint_stage"),
                    TriggerWrongCount = reader.GetInt32("trigger_wrong_count"),
                    HintText = reader.GetString("hint_text"),
                    LlmPromptTemplate = reader.IsDBNull(reader.GetOrdinal("llm_prompt_template")) ? null : reader.GetString("llm_prompt_template"),
                    IsActive = reader.GetBoolean("is_active")
                });
            }
            return list;
        }
    }
}