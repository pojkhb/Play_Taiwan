using System;
using System.Collections.Generic;
using backend.dao;
using backend.Models;
using backend.ViewModels;

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
            return _dao.SaveAiGeneratedStories(ep_id, region_id, aiResult);
        }
    }
}