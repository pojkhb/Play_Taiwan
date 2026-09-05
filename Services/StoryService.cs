using System.Collections.Generic;
using backend.dao;
using backend.Models;
using System;
using System.Threading.Tasks;
namespace backend.Services
{
    public class StoryService
    {
        private readonly StoryDao _dao;

        public StoryService(StoryDao dao)
        {
            _dao = dao;
        }

        public StoryWheelSpinResponse WheelSpin()
        {
            return _dao.WheelSpin();
        }

        public List<StoryWheelSpinResponse> GetRegions(string mode, string cityName)
        {
            return _dao.GetRegions(mode, cityName);
        }

        public List<StoryOptionResponse> GenerateOptions(StoryGenerateRequest req)
        {
            return _dao.GenerateStories(req);
        }

        public StoryDetailResponse GetDetail(string storyId)
        {
            return _dao.GetDetail(storyId);
        }

        public StoryDetailResponse ConfirmStory(StoryConfirmRequest req)
        {
            return _dao.GetDetail(req.story_id);
        }

        public List<Dictionary<string, string>> SaveAiGeneratedStories(string ep_id, string region_id, AiStoryResult aiResult)
        {
            // 呼叫 DAO 層處理多筆寫入，並將結果回傳給 Controller
            return _dao.SaveAiGeneratedStories(ep_id, region_id, aiResult);
        }
        /// <summary>
        /// 接收前端傳入的文字，讓 AI 進行文字轉劇本並寫入資料庫
        /// </summary>
        public async Task<object> GenerateScriptFromTextAsync(string epId, string inputText)
        {
            // 這裡可以根據你的 Python AI 服務需求進行實作
            // 範例：將 inputText 包裝後打給 Python 端，或直接呼叫現有的儲存邏輯
            
            // 暫時回傳一個組好的物件結構供測試與對應
            return new 
            {
                story_id = "AI_" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(),
                title = inputText.Length > 10 ? inputText.Substring(0, 10) + "..." : inputText
            };
        }
    }
}