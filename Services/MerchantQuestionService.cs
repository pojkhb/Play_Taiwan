using System.Collections.Generic;
using System.Linq;
using backend.dao;
using backend.Models;
using backend.ViewModels;

namespace backend.Services
{
    public class MerchantQuestionService
    {
        private readonly MerchantQuestionDao _dao;

        public MerchantQuestionService(MerchantQuestionDao dao)
        {
            _dao = dao;
        }

        public int Create(QuestionCreateRequest req)
        {
            ValidateOptions(req.options);
            return _dao.Create(req);
        }

        public List<QuestionResponse> GetByStore(int storeId) => _dao.GetByStore(storeId);

        public QuestionResponse GetById(int questionId)
        {
            QuestionResponse question = _dao.GetById(questionId);
            if (question == null) throw new NotFoundException($"找不到 question_id={questionId} 的題目");
            return question;
        }

        public void Update(int questionId, QuestionUpdateRequest req)
        {
            ValidateOptions(req.options);
            bool updated = _dao.Update(questionId, req);
            if (!updated) throw new NotFoundException($"找不到 question_id={questionId} 的題目");
        }

        /// <summary>
        /// 刪除前檢查是否有 task 引用這一題；若有，擋下刪除並回傳 409 + 引用的 task_id 清單。
        /// </summary>
        public void Delete(int questionId)
        {
            List<int> referencingTaskIds = _dao.GetReferencingTaskIds(questionId);
            if (referencingTaskIds.Count > 0)
            {
                throw new ConflictException(
                    $"此題目仍被 {referencingTaskIds.Count} 筆任務引用，無法刪除",
                    referencingTaskIds.Cast<object>());
            }

            bool deleted = _dao.Delete(questionId);
            if (!deleted) throw new NotFoundException($"找不到 question_id={questionId} 的題目");
        }

        private static void ValidateOptions(List<QuestionOptionInput> options)
        {
            if (options == null || options.Count == 0)
            {
                throw new System.Exception("請至少提供一個選項");
            }
            if (!options.Any(o => o.is_correct))
            {
                throw new System.Exception("選項中必須至少有一個正確答案 (is_correct=true)");
            }
        }
    }
}
