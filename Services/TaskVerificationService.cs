using System;
using System.Linq;
using System.Text.Json;
using backend.Models;

namespace backend.Services
{
    /// <summary>
    /// 任務答案驗證分發器。依 task.task_type（對應 DB task_category）決定使用哪種驗證方式。
    /// </summary>
    public interface ITaskVerificationService
    {
        TaskAnswerResponse Verify(TaskDetailResponse task, TaskAnswerRequest req);
    }

    public class TaskVerificationService : ITaskVerificationService
    {
        private readonly IVisionApiClient _vision;
        private readonly IPoseCompareClient _pose;
        private readonly ISpeechToTextClient _speech;
        private readonly IQrTokenStore _qrStore;

        public TaskVerificationService(
            IVisionApiClient vision, IPoseCompareClient pose,
            ISpeechToTextClient speech, IQrTokenStore qrStore)
        {
            _vision = vision;
            _pose = pose;
            _speech = speech;
            _qrStore = qrStore;
        }

        public TaskAnswerResponse Verify(TaskDetailResponse task, TaskAnswerRequest req)
        {
            return task.task_type switch
            {
                "MULTIPLE_CHOICE" => VerifyMultipleChoice(task, req),
                "PHOTO_CHECKIN" => VerifyPhotoCheckin(task, req),
                "VIDEO_PERFORM" => VerifyVideoPerform(task, req),
                "INTERVIEW" => VerifyInterview(task, req),
                "COUNT_REASON" => VerifyCountReason(task, req),
                "CROSS_LEVEL" => VerifyCrossLevel(task, req),
                "GPS_GEOFENCE" => VerifyGpsGeofence(task, req),
                "QR_CODE" => VerifyQrCode(task, req),
                "IMAGE_GEO_GUESS" => VerifyGpsGeofence(task, req), // 複用地理圍欄驗證
                _ => Fail($"未支援的任務類型：{task.task_type}")
            };
        }

        // 1. 文化問答型
        private TaskAnswerResponse VerifyMultipleChoice(TaskDetailResponse task, TaskAnswerRequest req)
            => req.selected_option_key == task.correct_option_key ? Success(task) : Fail("答錯了，再想想看");

        // 2. 拍照打卡型：Vision API 物體偵測，比對關鍵標籤
        private TaskAnswerResponse VerifyPhotoCheckin(TaskDetailResponse task, TaskAnswerRequest req)
        {
            if (string.IsNullOrEmpty(req.photo_url)) return Fail("請上傳照片");

            var requiredLabels = JsonSerializer.Deserialize<string[]>(task.vision_target_labels_json ?? "[]");
            var detectedLabels = _vision.AnnotateImage(req.photo_url);

            bool matched = requiredLabels.Any(required =>
                detectedLabels.Any(d => d.Label.Equals(required, StringComparison.OrdinalIgnoreCase) && d.Confidence >= 0.6));

            return matched ? Success(task) : Fail("照片內容與任務要求不符，請確認角度或重拍");
        }

        // 3. 短片演繹型：姿勢骨架相似度比對
        private TaskAnswerResponse VerifyVideoPerform(TaskDetailResponse task, TaskAnswerRequest req)
        {
            if (string.IsNullOrEmpty(req.video_url)) return Fail("請上傳影片");

            var referencePose = JsonSerializer.Deserialize<PoseReference>(task.pose_reference_json ?? "{}");
            double similarity = _pose.CompareToReference(req.video_url, referencePose);

            if (similarity >= 0.7) return Success(task);
            if (similarity >= 0.4) return Pending("動作辨識信心不足，已送人工審核");
            return Fail("動作與範本差異過大，請重新錄製");
        }

        // 4. 採訪蒐證型：關鍵字模糊比對
        private TaskAnswerResponse VerifyInterview(TaskDetailResponse task, TaskAnswerRequest req)
        {
            var script = JsonSerializer.Deserialize<InterviewScript>(task.interview_script_json ?? "{}");
            string transcript = !string.IsNullOrEmpty(req.audio_url) ? _speech.Transcribe(req.audio_url) : (req.text_answer ?? "");

            bool matched = script.ExpectedKeywords.Any(kw => transcript.Contains(kw, StringComparison.OrdinalIgnoreCase));
            return matched ? Success(task) : Fail("關鍵字不吻合，可以再問問看或用照片佐證訪問過程");
        }

        // 5. 計數推理型：允許正負 count_tolerance 誤差
        private TaskAnswerResponse VerifyCountReason(TaskDetailResponse task, TaskAnswerRequest req)
        {
            if (req.count_answer == null) return Fail("請輸入計數答案");
            int diff = Math.Abs(req.count_answer.Value - (task.count_target_answer ?? 0));
            return diff <= (task.count_tolerance ?? 1) ? Success(task) : Fail("數量不太對，再仔細數一次看看");
        }

        // 6. 跨關集結型：檢查前置關卡是否都完成，並組合線索比對答案
        private TaskAnswerResponse VerifyCrossLevel(TaskDetailResponse task, TaskAnswerRequest req)
        {
            var condition = JsonSerializer.Deserialize<CrossLevelCondition>(task.hidden_unlock_condition_json ?? "{}");
            bool allCompleted = condition.RequiredTaskIds.All(id => req.completed_task_ids.Contains(id));
            if (!allCompleted) return Fail("線索不完整，請先完成前面的關卡");

            string composedAnswer = string.Concat(condition.RequiredTaskIds.Select(id =>
    req.collected_fragments.ContainsKey(id) ? req.collected_fragments[id] : ""));
            return req.text_answer == composedAnswer ? Success(task) : Fail("推理結果不正確");
        }

        // 7. GPS區域定位型：伺服器端 Haversine 距離計算，搭配停留秒數(DWELL)
        private TaskAnswerResponse VerifyGpsGeofence(TaskDetailResponse task, TaskAnswerRequest req)
        {
            if (req.lat == null || req.lng == null) return Fail("缺少定位資訊");

            double distance = HaversineMeters(task.geofence_lat ?? 0, task.geofence_lng ?? 0, req.lat.Value, req.lng.Value);
            if (distance > (task.geofence_radius_m ?? 120)) return Fail("尚未進入指定範圍");
            if ((req.dwell_seconds ?? 0) < (task.geofence_dwell_seconds ?? 10)) return Fail("請在此處多停留幾秒再試一次");

            return Success(task);
        }

        // 8. QR Code掃碼解鎖型：一次性驗證，綁定探員帳號
        private TaskAnswerResponse VerifyQrCode(TaskDetailResponse task, TaskAnswerRequest req)
        {
            if (string.IsNullOrEmpty(req.qr_token)) return Fail("請掃描現場 QR Code");
            if (req.qr_token != task.qr_code_token) return Fail("QR Code 不正確");
            if (_qrStore.IsAlreadyUsed(req.qr_token, req.ep_id)) return Fail("此 QR Code 已被使用過");

            _qrStore.MarkUsed(req.qr_token, req.ep_id);
            return Success(task);
        }

        private static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
        {
            const double R = 6371000;
            double dLat = (lat2 - lat1) * Math.PI / 180;
            double dLng = (lng2 - lng1) * Math.PI / 180;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                       Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static TaskAnswerResponse Success(TaskDetailResponse task) => new()
        {
            is_correct = true,
            feedback_message = "任務完成！",
            unlocked_postcard_id = task.reward_postcard_id
        };
        private static TaskAnswerResponse Fail(string msg) => new() { is_correct = false, feedback_message = msg };
        private static TaskAnswerResponse Pending(string msg) => new() { is_correct = false, is_pending_review = true, feedback_message = msg };
    }

    // ---- 外部整合介面，實作可晚點補（先給 Fake 版本讓系統能編譯執行） ----
    public interface IVisionApiClient { (string Label, double Confidence)[] AnnotateImage(string photoUrl); }
    public interface IPoseCompareClient { double CompareToReference(string videoUrl, PoseReference reference); }
    public interface ISpeechToTextClient { string Transcribe(string audioUrl); }
    public interface IQrTokenStore
    {
        bool IsAlreadyUsed(string token, string epId);
        void MarkUsed(string token, string epId);
    }
}