using System;
using System.Collections.Generic;
using System.Linq;
using backend.dao;
using System.Threading.Tasks;
using backend.Models;
using backend.Services.Neo4j;
using backend.utils;

namespace backend.Services
{
    /// <summary>
    /// 任務答題相關業務邏輯層。整合 TaskDao 的資料查詢與 TaskVerificationService 的驗證邏輯。
    /// </summary>
    public class TaskService(
        TaskDao task_dao_obj,
        ITaskVerificationService verification,
        PlaceLookupService place_lookup_obj,
        TaskDifficultyService difficulty_obj)
    {
        private readonly TaskDao task_dao = task_dao_obj;
        private readonly ITaskVerificationService _verification = verification;
        private readonly PlaceLookupService place_lookup = place_lookup_obj;
        private readonly TaskDifficultyService _difficulty = difficulty_obj;

        #region 節點遊玩畫面
        /// <summary>
        /// 節點遊玩畫面（前端串接用）：一次回傳這一站的劇情、自己的座位、進度與每一題的作答方式、狀態。
        /// 只有劇本擁有者或協作隊員可以查看，且這一站要已被隊伍抵達過（不用再帶 GPS，作答時才檢查位置）。
        /// </summary>
        public NodePlayResponse GetNodePlay(int auId, int snId)
        {
            TaskDao.PlayerNodeState state = task_dao.GetPlayerNodeState(auId, snId)
                ?? throw new NotFoundException($"找不到節點 {snId}");

            int seatNo = ResolveSeat(state, auId);

            if (!TeamProgress.IsReached(state.node_order, state.current_node_order))
                throw new ConflictException("還沒抵達這一站，請先在地圖上確認抵達");

            TaskDao.NodePlayRow node = task_dao.GetNodePlayRow(snId);
            List<TaskDetailResponse> tasks = task_dao.GetTasksByNodeId(snId.ToString(), seatNo);
            Dictionary<int, (int wrongCount, bool hasHint)> stats =
                task_dao.GetTaskPlayerStats(auId, tasks.Select(t => t.task_id).ToList());

            NodeProgress progress = task_dao.GetNodeProgress(snId);
            PlayerPerformance performance = _difficulty.GetPerformance(auId);

            return new NodePlayResponse
            {
                node = new NodePlayNode
                {
                    node_id = node.sn_id,
                    story_id = node.s_id,
                    node_order = node.sn_order,
                    title = node.sn_title,
                    location_codename = node.location_codename,
                    opening_text = node.sn_opening_text,
                    success_text = progress.all_passed ? node.sn_success_text : null
                },
                npc = new NodePlayNpc
                {
                    npc_id = node.npc_id,
                    npc_name = node.npc_name ?? MapDao.DefaultNpcName,   // NPC 名單還沒匯入時
                    npc_role = node.npc_role,
                    npc_avatar_url = node.npc_avatar,                     // 站內路徑，Controller 組成完整網址
                    npc_voice = node.npc_voice
                },
                my_seat_no = seatNo,
                progress = progress,
                tasks = tasks.Select(t =>
                {
                    var (wrongCount, hasHint) = stats.TryGetValue(t.task_id, out var s) ? s : (0, false);
                    return new NodePlayTask
                    {
                        task_id = t.task_id,
                        type_id = t.type_id,
                        type_name = t.task_type,
                        answer_mode = AnswerModes.ForType(t.type_id),
                        task_describe = t.task_describe,
                        clue_text = t.clue_text,
                        options = (t.options ?? new List<TaskOption>())
                            .Select(o => new NodePlayOption { option_key = o.option_key, option_text = o.option_text, option_url = o.option_url })
                            .ToList(),
                        pass = t.pass,
                        wrong_count = wrongCount,
                        hint_available = t.pass == 0 && hasHint
                            && wrongCount >= TaskDifficultyService.HintUnlockWrongCount(t.type_id, performance)
                    };
                }).ToList()
            };
        }

        /// <summary>
        /// 登入者在這份劇本的座位：協作隊員用隊伍中的座位；擁有者還沒組隊時為 1 號；都不是就不能查看。
        /// </summary>
        private static int ResolveSeat(TaskDao.PlayerNodeState state, int auId)
        {
            int? seatNo = state.seat_no ?? (state.owner_id == auId ? 1 : null);
            return seatNo ?? throw new UnauthorizedAccessException("你沒有參與這個劇本，無法查看任務。");
        }
        #endregion

        #region 取得任務詳情
        /// <summary>
        /// 驗證玩家身分與位置後，從資料庫取出該節點的所有任務清單（含通關狀態 pass）。
        /// 流程：身分與座位（JWT）→ 位置驗證 → 讀取 task → 回傳
        /// 任務本身是在劇本生成時就已產生並寫入資料庫，此處只負責讀取。
        /// </summary>
        public async Task<List<TaskDetailResponse>> GetTask(TaskListReq req, int auId)
        {
            // 座位依登入者在協作隊伍中的 seat_no 決定；擁有者還沒組隊時當 1 號座位，其他人不能查看
            if (!int.TryParse(req.node_id, out int snId))
                throw new ArgumentException("node_id 格式錯誤。");

            TaskDao.PlayerNodeState player = task_dao.GetPlayerNodeState(auId, snId)
                ?? throw new KeyNotFoundException($"找不到節點 {req.node_id}。");

            int seatNo = ResolveSeat(player, auId);

            //位置驗證（已實作）
            if (await ValidatePlaceProximity(snId, req.gps_lat, req.gps_lon) == false)
            {
                throw new ArgumentException("玩家位置不在任務地點附近，無法取得任務詳情。");
            }

            // 協作解謎型（type_id=5）：依座位帶出該玩家在 task_clue 的線索
            var tasks = task_dao.GetTasksByNodeId(req.node_id, seatNo);

            if (tasks == null || tasks.Count == 0)
                throw new InvalidOperationException($"節點 {req.node_id} 尚未生成任務，請先完成劇本生成。");

            return tasks;
        }
        #endregion


        #region 送出答案

        private const int CoopTaskTypeId = 5; // 協作解謎型，作答紀錄要帶 pair_id

        /// <summary>
        /// 驗證玩家身分、作答欄位、抵達進度與位置後，依任務類型驗證答案，並把這次作答寫入 user_task_record；
        /// 答對時把 task.pass 設為 1（已通過），這一站全部通過則標記完成，整份劇本全部通過則自動完成劇本。
        /// 已通過的任務不再重複作答。回應帶回這一站的最新進度。
        /// 沒帶這一題作答方式（answer_mode）需要的欄位、或不在景點附近時回 400，不寫作答紀錄。
        /// </summary>
        public async Task<TaskAnswerResponse> SubmitAnswer(TaskAnswerRequest req)
        {
            var task = task_dao.GetTaskDetail(req.task_id);

            if (!int.TryParse(task.story_id, out int storyId))
            {
                throw new InvalidOperationException("此任務尚未掛入劇本，無法作答。");
            }

            // 只有劇本擁有者或該劇本協作隊伍的成員可以作答
            var (isOwner, pairId) = task_dao.GetStoryAccess(req.au_id, storyId);
            if (!isOwner && pairId == null)
            {
                throw new UnauthorizedAccessException("你沒有參與這個劇本，無法提交此任務。");
            }

            int nodeId = int.Parse(task.node_id);

            if (task.pass == 1)
            {
                NodeProgress current = task_dao.GetNodeProgress(nodeId);
                return new TaskAnswerResponse
                {
                    is_correct = true,
                    pass = 1,
                    feedback_message = "此任務已通過，不需要重複作答。",
                    node_progress = current,
                    node_completed = current.all_passed
                };
            }

            // 沒帶作答欄位是輸入錯誤（400），不算答錯一次，也不會因此解鎖提示
            string missingField = AnswerModes.MissingFieldMessage(AnswerModes.ForType(task.type_id), req);
            if (missingField != null)
            {
                throw new BadRequestException(missingField);
            }

            // 只能作答隊伍已抵達過的站（避免人在現場就先做後面的站）
            TaskDao.PlayerNodeState state = task_dao.GetPlayerNodeState(req.au_id, nodeId);
            if (state == null || !TeamProgress.IsReached(state.node_order, state.current_node_order))
            {
                throw new ConflictException("還沒抵達這一站，請先在地圖上確認抵達");
            }

            //位置驗證：提交答案時玩家也必須在任務地點附近
            if (await ValidatePlaceProximity(nodeId, req.gps_lat, req.gps_lon) == false)
            {
                throw new BadRequestException("玩家位置不在任務地點附近，無法提交答案。");
            }

            TaskAnswerResponse result = _verification.Verify(task, req);

            var mediaUrls = new[] { req.photo_url, req.video_url, req.audio_url }
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .ToList();

            // 人工審核中的任務不算答錯，is_correct 存 NULL；其他失敗結果記為答錯一次。
            bool? isCorrect = result.is_pending_review ? null : result.is_correct;

            TaskDao.AnswerSaveResult saved = task_dao.SaveAnswerRecord(
                req.au_id,
                task.task_id,
                storyId,
                nodeId,
                task.type_id == CoopTaskTypeId ? pairId : null,
                req.selected_option_key ?? req.text_answer,
                mediaUrls,
                isCorrect);

            result.pass = result.is_correct ? 1 : 0;
            result.node_progress = saved.node_progress;
            result.node_completed = saved.node_progress.all_passed;
            result.story_completed = saved.story_completed;
            return result;
        }

        #endregion

        #region 取得提示

        /// <summary>
        /// 依玩家在此任務的答錯次數（user_task_record）決定是否給提示：答錯次數達到門檻後才顯示 task.task_hint。
        /// 門檻依題目難易度與玩家表現決定（TaskDifficultyService.HintUnlockWrongCount）。
        /// </summary>
        public TaskHintResponse GetHint(int auId, int taskId)
        {
            int wrongCount = task_dao.GetWrongCount(auId, taskId);
            string hint = task_dao.GetTaskHint(taskId);
            int hintWrongCount = TaskDifficultyService.HintUnlockWrongCount(
                task_dao.GetTaskTypeId(taskId), _difficulty.GetPerformance(auId));
            var (npcName, npcAvatar) = task_dao.GetTaskNpc(taskId);   // 圖片是站內路徑，Controller 組成完整網址
            npcName ??= MapDao.DefaultNpcName;

            if (wrongCount < hintWrongCount)
            {
                return new TaskHintResponse { task_id = taskId.ToString(), npc_name = npcName, npc_avatar_url = npcAvatar, hint_text = $"答錯 {hintWrongCount} 次後就能取得提示。", is_available = false };
            }

            return new TaskHintResponse
            {
                task_id = taskId.ToString(),
                npc_name = npcName,
                npc_avatar_url = npcAvatar,
                hint_text = hint ?? "目前沒有更多的提示內容了。",
                is_available = hint != null
            };
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

        /// <summary>
        /// 驗證玩家目前位置是否在指定節點對應景點的抵達距離（PlayRules.ArrivalRadiusMeters）內。
        /// 取得任務內容（GetTask）與提交答案（SubmitAnswer）共用，座標與地圖抵達同一份（PlaceLookupService，依 uid 查 Neo4j）。
        /// </summary>
        private async Task<bool> ValidatePlaceProximity(int snId, double gps_lat, double gps_lon)
        {
            // 1. 取得目標地點的經緯度
            PlaceLookupService.PlaceInfo place = await place_lookup.GetPlaceAsync(task_dao.GetNodePlaceId(snId));

            // 2. 防呆：查不到景點座標時驗證失敗
            if (place == null)
            {
                return false;
            }

            // 3. 計算玩家目前位置與目標地點的距離 (單位: 公尺)
            double distance = CalculateDistance(
                gps_lat,
                gps_lon,
                place.lat,
                place.lng
            );

            // 4. 判斷是否在抵達距離內（與地圖抵達共用 PlayRules.ArrivalRadiusMeters）
            return distance <= PlayRules.ArrivalRadiusMeters;
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