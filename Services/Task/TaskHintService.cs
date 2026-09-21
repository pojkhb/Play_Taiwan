using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend.dao;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>
    /// 線索提示邏輯：依玩家目前答錯次數，判斷該給第幾階段的提示。
    /// 規則：wrongCount 未達第一階段 trigger_wrong_count → 不給提示；
    ///       達第一階段門檻但未達第二階段門檻 → 給第一階段(較模糊)；
    ///       達第二階段門檻 → 給第二階段(更明確)。
    /// </summary>
    public class TaskHintService
    {
        private readonly TaskHintDao _dao;

        public TaskHintService(TaskHintDao dao)
        {
            _dao = dao;
        }

        public async Task<HintResponse> GetHintAsync(string taskId, int wrongCount)
        {
            var hints = await _dao.GetByTaskIdAsync(taskId);
            if (hints.Count == 0)
            {
                return new HintResponse { Available = false, HintStage = 0, HintText = null, LlmPromptTemplate = null };
            }

            var eligible = hints.Where(h => wrongCount >= h.TriggerWrongCount)
                                 .OrderByDescending(h => h.HintStage)
                                 .FirstOrDefault();

            if (eligible == null)
            {
                return new HintResponse { Available = false, HintStage = 0, HintText = null, LlmPromptTemplate = null };
            }

            return new HintResponse
            {
                Available = true,
                HintStage = eligible.HintStage,
                HintText = eligible.HintText,
                LlmPromptTemplate = eligible.LlmPromptTemplate
            };
        }
    }
}