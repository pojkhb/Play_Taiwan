// 檔案路徑：Services\VisitorAnalysis\VisitorAnalysisService.cs
using System.Threading.Tasks;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    public class VisitorAnalysisService
    {
        private readonly VisitorAnalysisDao _dao;
        private readonly VisitorAnalysisAiGateway _aiGateway;

        public VisitorAnalysisService(
            VisitorAnalysisDao dao,
            VisitorAnalysisAiGateway aiGateway)
        {
            _dao = dao;
            _aiGateway = aiGateway;
        }

        #region 客群分析儀表板（MySQL visitor_analysis）

        public VisitorAnalysisDashboardResponse GetDashboard()
        {
            return _dao.GetDashboard();
        }

        #endregion

        #region Neo4j 景點清單 / AI 經營策略（經 FastAPI）

        public Task<VisitorAttractionListResponse> GetAttractionsAsync(
            string keyword,
            int page,
            int pageSize)
        {
            return _aiGateway.GetAttractionsAsync(keyword, page, pageSize);
        }

        public Task<VisitorStrategyChatResponse> ChatStrategyAsync(
            VisitorStrategyChatRequest request)
        {
            return _aiGateway.GenerateStrategyAsync(request);
        }

        #endregion
    }
}
