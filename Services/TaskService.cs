using System;
using System.Collections.Generic;
using backend.dao;
using System.Threading.Tasks;
using backend.Models;

namespace backend.Services
{
    /// <summary>
    /// 任務答題相關業務邏輯層。整合 TaskDao 的資料查詢與 TaskVerificationService 的驗證邏輯。
    /// </summary>
    public class TaskService(
        TaskDao task_dao_obj,
        ITaskVerificationService verification)
    {
        private readonly TaskDao task_dao = task_dao_obj;
        private readonly ITaskVerificationService _verification = verification;

        #region 取得任務詳情
        /// <summary>
        /// 驗證玩家位置後，從資料庫取出該節點的所有任務清單。
        /// 流程：位置驗證 → 讀取 md_task → 回傳
        /// 任務本身是在劇本生成時（POST api/Story/GenerateAi）就已產生並寫入資料庫，此處只負責讀取。
        /// </summary>
        public async Task<List<TaskDetailResponse>> GetTask(TaskListReq req)
        {
            //位置驗證（已實作）
            if (await PlaceValidation(req) == false)
            {
                throw new ArgumentException("玩家位置不在任務地點附近，無法取得任務詳情。");
            }

            var tasks = task_dao.GetTasksByNodeId(req.node_id);

            if (tasks == null || tasks.Count == 0)
                throw new InvalidOperationException($"節點 {req.node_id} 尚未生成任務，請先完成劇本生成。");

            // 協作解謎型（type_id=5）：玩家 B（player_index=2）要看到的是 task_describe_b，
            // 對玩家 A 或其他題型則不動，維持 task_describe 原樣。
            if (req.player_index == 2)
            {
                foreach (var task in tasks)
                {
                    if (task.type_id == 5 && !string.IsNullOrEmpty(task.task_describe_b))
                        task.task_describe = task.task_describe_b;
                }
            }

            return tasks;
        }
        #endregion


        #region 送出答案

        /// <summary>
        /// 驗證玩家位置後，依任務類型驗證答案，並將答錯次數或完成狀態寫入 ep_task_record。
        /// </summary>
        public async Task<TaskAnswerResponse> SubmitAnswer(TaskAnswerRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.ep_id))
            {
                throw new ArgumentException("ep_id 不能為空白。");
            }

            var task = task_dao.GetTaskDetail(req.task_id);

            //位置驗證：提交答案時玩家也必須在任務地點附近
            if (await ValidatePlaceProximity(task.node_id, req.gps_lat, req.gps_lon) == false)
            {
                throw new ArgumentException("玩家位置不在任務地點附近，無法提交答案。");
            }

            TaskAnswerResponse result = _verification.Verify(task, req);

            // 若使用者有上傳圖片或影片，則存入 md_task_media
            if (!string.IsNullOrEmpty(req.photo_url) || !string.IsNullOrEmpty(req.video_url))
            {
                var mediaUrls = new List<string>();
                if (!string.IsNullOrEmpty(req.photo_url)) mediaUrls.Add(req.photo_url);
                if (!string.IsNullOrEmpty(req.video_url)) mediaUrls.Add(req.video_url);
                task_dao.InsertTaskMedia(req.task_id, req.ep_id, mediaUrls);
            }

            if (result.is_correct)
            {
                // 答對：若為最後一關，還要標記景點完成
                task_dao.MarkTaskCompleted(
                    req.ep_id,
                    task.task_id.ToString(),
                    null,          // 後續可從 md_task 補回 story_id
                    task.node_id
                );
            }
            else if (!result.is_pending_review)
            {
                // 答錯：人工審核中的影片任務不算答錯；其他失敗結果累積一次。
                task_dao.IncreaseWrongCount(
                    req.ep_id,
                    task.task_id,
                    null,          // 後續可從 md_task 補回 story_id
                    task.node_id
                );
            }
            return result;
        }

        #endregion

        #region 取得提示

        public TaskHintResponse GetHint(string epId, string taskId)
        {
            if (string.IsNullOrWhiteSpace(epId))
            {
                throw new ArgumentException("ep_id 不可為空白。");
            }

            int wrongCount = task_dao.GetWrongCount(epId, taskId);

            return task_dao.GetHintByWrongCount(taskId, wrongCount);
        }

        #endregion

        #region 動態難度
        public int RecordVisitAndGetDifficulty(string ep_id, string region_id)
        {
            return task_dao.RecordVisitAndGetDifficulty(ep_id, region_id);
        }

        public string GetDifficultyPrompt(int star)
        {
            return task_dao.GetDifficultyPrompt(star);
        }
        #endregion

        #region 獎章抽取
        public string DrawBadge(string ep_id, string story_id)
        {
            return task_dao.DrawBadge(ep_id, story_id);
        }
        #endregion

        #region 隱藏關卡
        public HiddenLevelTriggerResult CheckHiddenLevel(string ep_id, double lat, double lng, string region_id)
        {
            return task_dao.CheckHiddenLevelTrigger(ep_id, lat, lng, region_id);
        }
        #endregion

        //
        //common
        //

        private async Task<bool> PlaceValidation(TaskListReq req)
        {
            return await ValidatePlaceProximity(req.node_id, req.gps_lat, req.gps_lon);
        }

        /// <summary>
        /// 驗證玩家目前位置是否在指定節點對應景點的 500 公尺內。
        /// 取得任務內容（GetTask）與提交答案（SubmitAnswer）共用同一套位置驗證。
        /// </summary>
        private async Task<bool> ValidatePlaceProximity(string node_id, double gps_lat, double gps_lon)
        {
            // 1. 取得目標地點的經緯度
            var place_loc = await task_dao.GetPlaceLocation(node_id);

            // 2. 防呆：如果找不到該節點，或是該節點缺少經緯度，則驗證失敗
            if (place_loc == null || !place_loc.Lat.HasValue || !place_loc.Lon.HasValue)
            {
                return false;
            }

            // 3. 計算玩家目前位置與目標地點的距離 (單位: 公尺)
            double distance = CalculateDistance(
                gps_lat,
                gps_lon,
                place_loc.Lat.Value,
                place_loc.Lon.Value
            );

            // 4. 判斷是否在 500 公尺以內
            return distance <= 500;
        }

        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371e3; // 地球半徑 (公尺)

            double rLat1 = lat1 * Math.PI / 180.0;
            double rLat2 = lat2 * Math.PI / 180.0;
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(rLat1) * Math.Cos(rLat2) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c; // 回傳距離 (公尺)
        }
    }
}