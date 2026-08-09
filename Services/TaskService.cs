using backend.dao;
using backend.Models;

namespace backend.Services
{
    public class TaskService
    {
        private readonly TaskDao _dao;
        public TaskService(TaskDao dao) { _dao = dao; }

        #region 取得任務詳情
        public TaskDetailResponse GetTaskDetail(string task_id)
        {
            return _dao.GetTaskDetail(task_id);
        }
        #endregion

        #region 送出答案
        public TaskAnswerResponse SubmitAnswer(TaskAnswerRequest req)
        {
            // TODO: 依 task_id 對應答案(或AI判定圖片/文字) 進行核對，並更新 ep_task_record / 觸發明信片生成
            return _dao.SubmitAnswer(req);
        }
        #endregion

        #region 取得提示
        public TaskHintResponse GetHint(string task_id)
        {
            return _dao.GetHint(task_id);
        }
        #endregion
    }
}