using System.Collections.Generic;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    public class StoryService
    {
        private readonly StoryDao _dao;
        public StoryService(StoryDao dao) { _dao = dao; }

        #region 轉盤抽取地區
        public StoryWheelSpinResponse SpinWheel()
        {
            return _dao.SpinWheel();
        }
        #endregion

        #region 產生劇本選項 (呼叫 RAG+LLM 服務)
        public List<StoryOptionResponse> GenerateStoryOptions(StoryGenerateRequest req)
        {
            // TODO: 呼叫外部 RAG/LLM 服務 (Python microservice / Azure OpenAI)，
            // 依 req.region / req.preferences / req.party_size 產生 2-3 個劇本選項
            return _dao.GenerateStoryOptions(req);
        }
        #endregion

        #region 劇情觀看更多
        public StoryDetailResponse GetStoryDetail(string story_id)
        {
            return _dao.GetStoryDetail(story_id);
        }
        #endregion

        #region 確認選卷
        public void ConfirmStory(StoryConfirmRequest req)
        {
            _dao.ConfirmStory(req);
        }
        #endregion

        #region 劇本結束總結
        public StoryEndingResponse GetEnding(string story_id)
        {
            return _dao.GetEnding(story_id);
        }
        #endregion
    }
}