using System;
using backend.dao;
using backend.Models;

namespace backend.Services
{
    /// <summary>
    /// 任務答題相關業務邏輯層。整合 TaskDao 的資料查詢與 TaskVerificationService 的驗證邏輯。
    /// </summary>
    public class TaskService
    {
        private readonly TaskDao _dao;
        private readonly ITaskVerificationService _verification;

        public TaskService(TaskDao dao, ITaskVerificationService verification)
        {
            _dao = dao;
            _verification = verification;
        }

        #region 取得任務詳情
        public TaskDetailResponse GetTaskDetail(string task_id)
        {
            return _dao.GetTaskDetail(task_id);
        }
        #endregion

        #region 送出答案

        /// <summary>
        /// 依任務類型驗證答案，並將答錯次數或完成狀態寫入 ep_task_record。
        /// </summary>
        public TaskAnswerResponse SubmitAnswer(TaskAnswerRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.ep_id))
            {
                throw new ArgumentException("ep_id 不可為空白。");
            }

            var task = _dao.GetTaskDetail(req.task_id);

            TaskAnswerResponse result = _verification.Verify(task, req);

            if (result.is_correct)
            {
                // 答對：保留既有答錯次數，標記任務完成。
                _dao.MarkTaskCompleted(
                    req.ep_id,
                    task.task_id,
                    null,          // 後續可從 md_task 補回 story_id
                    task.node_id);
            }
            else if (!result.is_pending_review)
            {
                // 答錯：人工審核中的影片任務不算答錯；其他失敗結果累積一次。
                _dao.IncreaseWrongCount(
                    req.ep_id,
                    task.task_id,
                    null,          // 後續可從 md_task 補回 story_id
                    task.node_id);
            }

            return result;
        }

        #endregion

        #region 取得提示

        /// <summary>
        /// 由後端查詢玩家實際答錯次數，再回傳對應提示。
        /// 不信任前端傳入的 wrongCount，避免玩家直接跳到第二階段提示。
        /// </summary>
        public TaskHintResponse GetHint(string epId, string taskId)
        {
            if (string.IsNullOrWhiteSpace(epId))
            {
                throw new ArgumentException("ep_id 不可為空白。");
            }

            int wrongCount = _dao.GetWrongCount(epId, taskId);

            return _dao.GetHintByWrongCount(taskId, wrongCount);
        }

        #endregion

        #region 動態難度
        public int RecordVisitAndGetDifficulty(string ep_id, string region_id)
        {
            return _dao.RecordVisitAndGetDifficulty(ep_id, region_id);
        }

        public string GetDifficultyPrompt(int star)
        {
            return _dao.GetDifficultyPrompt(star);
        }
        #endregion

        #region 獎章抽取
        public string DrawBadge(string ep_id, string story_id)
        {
            return _dao.DrawBadge(ep_id, story_id);
        }
        #endregion

        #region 隱藏關卡
        public HiddenLevelTriggerResult CheckHiddenLevel(string ep_id, double lat, double lng, string region_id)
        {
            return _dao.CheckHiddenLevelTrigger(ep_id, lat, lng, region_id);
        }
        #endregion
    }
}