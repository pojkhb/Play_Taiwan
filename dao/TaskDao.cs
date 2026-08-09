using System.Collections.Generic;
using backend.utils;
using backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class TaskDao
    {
        private readonly AppSettings _appSettings;
        private readonly MySqlConnection _MysqlConnect;
        private readonly HttpContext _ipContext;

        public TaskDao(IOptions<AppSettings> appSettings, IHttpContextAccessor httpContextAccessor)
        {
            _appSettings = appSettings.Value;
            _MysqlConnect = new MySqlConnection(_appSettings.mydb);
            _ipContext = httpContextAccessor.HttpContext;
        }

        #region 取得任務詳情
        public TaskDetailResponse GetTaskDetail(string task_id)
        {
            // TODO: 資料表尚未建置，預計欄位:
            // task(task_id, node_id, task_type, task_description, requires_photo, requires_gps, requires_group)
            // task_option(task_id, option_key, option_text)
            return new TaskDetailResponse
            {
                task_id = task_id,
                node_id = "NODE-001",
                task_type = "文化問答型",
                task_description = "御匾歷經百年仍高懸於此。其中最早的一方，出自哪位皇帝之手？",
                options = new List<TaskOption>
                {
                    new TaskOption { option_key = "A", option_text = "康熙" },
                    new TaskOption { option_key = "B", option_text = "雍正" },
                    new TaskOption { option_key = "C", option_text = "乾隆" },
                    new TaskOption { option_key = "D", option_text = "光緒" }
                },
                requires_photo = false,
                requires_gps = true,
                requires_group = false
            };
        }
        #endregion

        #region 送出答案
        public TaskAnswerResponse SubmitAnswer(TaskAnswerRequest req)
        {
            // TODO: 資料表 task_answer_key(task_id, correct_option_key) 尚未建置
            // 依 req.task_id 查詢正確答案並比對 req.selected_option_key
            // 正確 -> 呼叫 AI 明信片生成服務(RAG+LLM) 產生 postcard 並 INSERT INTO ep_postcard
            bool isCorrect = req.selected_option_key == "A";
            return new TaskAnswerResponse
            {
                is_correct = isCorrect,
                feedback_message = isCorrect ? "答對了！" : "答錯了，再想想看",
                unlocked_postcard_id = isCorrect ? "POSTCARD-001" : null,
                unlocked_node_progress = isCorrect ? 2 : 1,
                total_node_count = 6
            };
        }
        #endregion

        #region 取得提示
        public TaskHintResponse GetHint(string task_id)
        {
            // TODO: 資料表 task_hint(task_id, hint_text, npc_avatar_url) 尚未建置
            return new TaskHintResponse
            {
                task_id = task_id,
                npc_avatar_url = null,
                hint_text = "八方御區中，有一方屬於清朝最早的一位皇帝"
            };
        }
        #endregion
    }
}