using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace backend.Services
{
    // =====================================================================
    // AI 任務生成 API - Request / Response Models
    // 對應 AI_Task_API_Spec.md
    // =====================================================================

    /// <summary>
    /// 呼叫 AI 任務生成 API 的共用 Request Body。
    /// 適用於兩支 API：generate/description 與 generate/choice。
    /// </summary>
    public class AiTaskRequest
    {
        /// <summary>任務題型代碼（數字），對應 md_type.type_id</summary>
        public int type_id { get; set; }

        /// <summary>任務題型名稱（繁體中文），對應 md_type.type_name，例如 "文化問答型"</summary>
        public string task_type { get; set; }

        /// <summary>
        /// 該景點在 Neo4j 圖資料庫的唯一識別 UID（elementId 格式）。
        /// 由 md_story_node.place_id 傳入。
        /// </summary>
        public string place_uid { get; set; }

        /// <summary>
        /// 該景點所屬劇本的敘事內容，組合自 md_story.prologue + md_story_node.fog_hint + md_place.introduction。
        /// AI 以此作為背景知識生成任務。
        /// </summary>
        public string story_content { get; set; }

        /// <summary>
        /// 最重要參數。後端依題型組合的完整生成指引，
        /// 直接告知 AI 要生成什麼題型、風格要求、字數限制等。
        /// 對應 md_task.task_describe 欄位中存放的 Prompt 範本。
        /// </summary>
        public string task_prompt { get; set; }
    }

    /// <summary>
    /// generate/description API 的 Response（無選項型：類型 2/3/4/5/7/8）
    /// </summary>
    public class AiTaskDescriptionResponse
    {
        public bool success { get; set; }

        /// <summary>AI 生成的任務描述文字，直接顯示給玩家</summary>
        public string task_describe { get; set; }

        /// <summary>失敗時的錯誤代碼，例如 "GENERATION_FAILED"</summary>
        public string error_code { get; set; }

        /// <summary>失敗時的錯誤訊息</summary>
        public string message { get; set; }
    }

    /// <summary>
    /// generate/choice API 的 Response（選擇題型：類型 6）
    /// </summary>
    public class AiTaskChoiceResponse
    {
        public bool success { get; set; }

        /// <summary>選擇題的題幹文字</summary>
        public string task_describe { get; set; }

        /// <summary>
        /// 選項陣列，固定四個（A/B/C/D）。
        /// 恰好只有一個 is_correct = true。
        /// </summary>
        public List<AiTaskChoiceOption> options { get; set; }

        public string error_code { get; set; }
        public string message { get; set; }
    }

    /// <summary>
    /// 選擇題的單一選項
    /// </summary>
    public class AiTaskChoiceOption
    {
        /// <summary>選項代碼，依序為 A / B / C / D</summary>
        public string option_key { get; set; }

        /// <summary>選項文字內容</summary>
        public string option_text { get; set; }

        /// <summary>是否為正確答案（四個選項中恰好只有一個為 true）</summary>
        public bool is_correct { get; set; }
    }

    // =====================================================================
    // IAiTaskClient - 介面定義
    // =====================================================================

    /// <summary>
    /// 定義呼叫 AI 任務生成服務的介面。
    /// 對應 AI_Task_API_Spec.md 的兩支 API。
    /// </summary>
    public interface IAiTaskClient
    {
        /// <summary>
        /// 呼叫無選項型任務描述生成 API。
        /// 適用題型：跨關集結型(2)、創意攝影型(3)、地方美食型(4)、
        ///           協作解謎型(5)、景點猜猜樂(7)、e人訪談型(8)。
        /// POST /api/task/generate/description
        /// </summary>
        Task<AiTaskDescriptionResponse> GenerateDescriptionAsync(AiTaskRequest request);

        /// <summary>
        /// 呼叫選擇題型任務生成 API。
        /// 適用題型：文化問答型(6)。
        /// POST /api/task/generate/choice
        /// </summary>
        Task<AiTaskChoiceResponse> GenerateChoiceAsync(AiTaskRequest request);
    }

    // =====================================================================
    // AiTaskClient - 正式實作（呼叫實際 AI 服務）
    // =====================================================================

    /// <summary>
    /// 實作 IAiTaskClient，透過 HTTP 呼叫 AI 任務生成服務。
    /// BaseUrl 設定於 appsettings.json 的 AiService:BaseUrl。
    /// </summary>
    public class AiTaskClient : IAiTaskClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public AiTaskClient(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _baseUrl = configuration["AiService:BaseUrl"] ?? "http://localhost:2026";
        }

        public async Task<AiTaskDescriptionResponse> GenerateDescriptionAsync(AiTaskRequest request)
        {
            string url = $"{_baseUrl}/api/task/generate/description";
            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"呼叫 AI 任務描述生成 API 失敗，HTTP {(int)response.StatusCode}");

            return await response.Content.ReadFromJsonAsync<AiTaskDescriptionResponse>();
        }

        public async Task<AiTaskChoiceResponse> GenerateChoiceAsync(AiTaskRequest request)
        {
            string url = $"{_baseUrl}/api/task/generate/choice";
            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"呼叫 AI 選擇題生成 API 失敗，HTTP {(int)response.StatusCode}");

            return await response.Content.ReadFromJsonAsync<AiTaskChoiceResponse>();
        }
    }

    // =====================================================================
    // MockAiTaskClient - 假實作（本地測試用）
    // =====================================================================

    /// <summary>
    /// 假 AI 任務生成用戶端，用於本地開發與測試。
    /// 不呼叫實際 AI 服務，直接回傳符合格式的假資料。
    /// </summary>
    public class MockAiTaskClient : IAiTaskClient
    {
        public async Task<AiTaskDescriptionResponse> GenerateDescriptionAsync(AiTaskRequest request)
        {
            await Task.Delay(300); // 模擬網路延遲

            return new AiTaskDescriptionResponse
            {
                success = true,
                task_describe = $"[Mock AI] ({request.task_type}) 這是根據「{request.place_uid}」景點生成的假任務描述，請發揮創意完成任務！"
            };
        }

        public async Task<AiTaskChoiceResponse> GenerateChoiceAsync(AiTaskRequest request)
        {
            await Task.Delay(300); // 模擬網路延遲

            return new AiTaskChoiceResponse
            {
                success = true,
                task_describe = $"[Mock AI] ({request.task_type}) 關於此景點，下列何者正確？",
                options = new List<AiTaskChoiceOption>
                {
                    new AiTaskChoiceOption { option_key = "A", option_text = "[Mock] 正確答案選項", is_correct = true  },
                    new AiTaskChoiceOption { option_key = "B", option_text = "[Mock] 錯誤選項 B",  is_correct = false },
                    new AiTaskChoiceOption { option_key = "C", option_text = "[Mock] 錯誤選項 C",  is_correct = false },
                    new AiTaskChoiceOption { option_key = "D", option_text = "[Mock] 錯誤選項 D",  is_correct = false }
                }
            };
        }
    }
}
