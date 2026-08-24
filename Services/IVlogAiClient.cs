using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace backend.Services
{
    /* 定義呼叫 AI 的介面 */
    public interface IVlogAiClient
    {
        Task<string> GenerateStoryAsync(HttpContent content);
        Task<VlogTaskStatusResponse> CheckStatusAsync(string taskId);
    }

    /* 實作呼叫 AI 的邏輯 */
    public class VlogAiClient : IVlogAiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public VlogAiClient(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            // 這裡對應 Python 服務的網址
            _baseUrl = configuration["AiService:BaseUrl"] ?? "http://localhost:2026"; 
        }

        public async Task<string> GenerateStoryAsync(HttpContent content)
        {
            string url = $"{_baseUrl}/api/story/generate";
            var response = await _httpClient.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
                throw new Exception("呼叫 AI 服務生成劇本失敗");
                
            var result = await response.Content.ReadFromJsonAsync<VlogTaskStatusResponse>();
            return result?.task_id;
        }

        public async Task<VlogTaskStatusResponse> CheckStatusAsync(string taskId)
        {
            string url = $"{_baseUrl}/check_status/{taskId}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                throw new Exception("查詢 AI 任務狀態失敗");
                
            return await response.Content.ReadFromJsonAsync<VlogTaskStatusResponse>();
        }
    }

    /* 接收 Python 回傳狀態的模型 */
    public class VlogTaskStatusResponse
    {
        public string task_id { get; set; }
        public string status { get; set; } 
        public string result_path { get; set; }
        public string message { get; set; }
    }
    
    /* --- 這是用來測試的「假 Python AI 伺服器」 --- */
    public class MockVlogAiClient : IVlogAiClient
    {
        // 假裝收到生成請求，回傳一個假的 Task ID
        public async Task<string> GenerateStoryAsync(HttpContent content)
        {
            await Task.Delay(500); // 模擬網路延遲
            return "FAKE_TASK_999"; 
        }

        // 假裝被查詢狀態
        public async Task<VlogTaskStatusResponse> CheckStatusAsync(string taskId)
        {
            await Task.Delay(500); // 模擬網路延遲
            
            // 模擬 Python 吐出來的完美 JSON 劇本格式
            string fakeAiJson = @"
            {
                ""title"": ""模擬生成的台南大探險"",
                ""subtitle"": ""AI假資料測試"",
                ""prologue"": ""這是一段由 Mock 產生的測試故事..."",
                ""synopsis"": ""測試大綱：走訪安平古堡與老街。"",
                ""expected_badges"": [""badge_tainan_ex""],
                ""expected_postcards"": 3,
                ""nodes"": [
                    { ""place_id"": ""P001"", ""node_title"": ""安平古堡"", ""node_order"": 1 },
                    { ""place_id"": ""P002"", ""node_title"": ""安平樹屋"", ""node_order"": 2 }
                ]
            }";

            return new VlogTaskStatusResponse
            {
                task_id = taskId,
                status = "completed", // 直接假裝完成
                result_path = fakeAiJson
            };
        }
    }
}