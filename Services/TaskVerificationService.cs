using System;
using System.Linq;
using backend.Models;

namespace backend.Services
{
    public interface ITaskVerificationService
    {
        TaskAnswerResponse Verify(TaskDetailResponse task, TaskAnswerRequest req);
    }

    public class TaskVerificationService : ITaskVerificationService
    {
        public TaskAnswerResponse Verify(TaskDetailResponse task, TaskAnswerRequest req)
        {
            return task.type_id switch
            {
                1 => VerifyGpsAreaPositioning(task, req),          
                2 => VerifyCrossLevelRally(task, req),             
                3 => VerifyCreativePhoto(task, req),               
                4 => VerifyLocalFood(task, req),                   
                5 => VerifyCoopPuzzle(task, req),                  
                6 => VerifyCulturalQa(task, req),                  
                7 => VerifyGuessAttraction(task, req),             
                8 => VerifyExtrovertInterview(task, req),          
                9 => VerifyMerchantKnowledgeQa(task, req),         
                10 => VerifyImageGeoGuess(task, req),              
                _ => Fail($"未知的任務類型 (Type {task.type_id})")
            };
        }

        #region 各題型專屬驗證邏輯

        // 1: GPS 區域定位型 (純打卡)
        private TaskAnswerResponse VerifyGpsAreaPositioning(TaskDetailResponse task, TaskAnswerRequest req)
        {
            return Success(task, "定位打卡成功！");
        }

        // 2: 跨關集結型 (需輸入文字答案)
        private TaskAnswerResponse VerifyCrossLevelRally(TaskDetailResponse task, TaskAnswerRequest req)
        {
            if (string.IsNullOrEmpty(req.text_answer))
                return Fail("請輸入您的推理答案！");

            return Success(task, "推理正確，恭喜集結成功！");
        }

        // 3: 創意攝影型 (需上傳照片)
        private TaskAnswerResponse VerifyCreativePhoto(TaskDetailResponse task, TaskAnswerRequest req)
        {
            if (string.IsNullOrEmpty(req.photo_url))
                return Fail("請務必上傳創意照片！");

            return Success(task, "創意照片上傳成功！");
        }

        // 4: 地方美食型 (需上傳照片)
        private TaskAnswerResponse VerifyLocalFood(TaskDetailResponse task, TaskAnswerRequest req)
        {
            if (string.IsNullOrEmpty(req.photo_url))
                return Fail("請務必拍下美食照片！");

            return Success(task, "美食紀錄成功！");
        }

        // 5: 協作解謎型 (需輸入文字答案)
        private TaskAnswerResponse VerifyCoopPuzzle(TaskDetailResponse task, TaskAnswerRequest req)
        {
            if (string.IsNullOrEmpty(req.text_answer))
                return Fail("請輸入您們的共同解答！");

            return Success(task, "解答正確，團隊合作無間！");
        }

        // 6: 文化問答型 (選擇題)
        private TaskAnswerResponse VerifyCulturalQa(TaskDetailResponse task, TaskAnswerRequest req)
        {
            if (task.options == null || task.options.Count == 0) return Fail("此任務缺少選項。");
            var correct = task.options.FirstOrDefault(o => o.is_correct);
            
            if (req.selected_option_key == correct?.option_key)
                return Success(task, "文化知識正確，恭喜答對！");
            else
                return Fail("文化知識有誤，請再仔細想想！");
        }

        // 7: 景點猜猜樂 (選擇題)
        private TaskAnswerResponse VerifyGuessAttraction(TaskDetailResponse task, TaskAnswerRequest req)
        {
            if (task.options == null || task.options.Count == 0) return Fail("此任務缺少選項。");
            var correct = task.options.FirstOrDefault(o => o.is_correct);
            
            if (req.selected_option_key == correct?.option_key)
                return Success(task, "好眼力！猜對了景點的角落！");
            else
                return Fail("猜錯囉，請在附近再多觀察看看！");
        }

        // 8: e 人訪談型 (需上傳錄音檔或影片)
        private TaskAnswerResponse VerifyExtrovertInterview(TaskDetailResponse task, TaskAnswerRequest req)
        {
            if (string.IsNullOrEmpty(req.audio_url) && string.IsNullOrEmpty(req.video_url))
                return Fail("請務必上傳訪談錄音或影片！");

            return Success(task, "訪談紀錄已成功上傳！");
        }

        // 9: 商家知識問答 (選擇題)
        private TaskAnswerResponse VerifyMerchantKnowledgeQa(TaskDetailResponse task, TaskAnswerRequest req)
        {
            if (task.options == null || task.options.Count == 0) return Fail("此任務缺少選項。");
            var correct = task.options.FirstOrDefault(o => o.is_correct);
            
            if (req.selected_option_key == correct?.option_key)
                return Success(task, "完全正確！看來您對這間店很了解！");
            else
                return Fail("哎呀，關於這家店的知識好像記錯囉！");
        }

        // 10: 圖像地理猜謎型 (選擇題)
        private TaskAnswerResponse VerifyImageGeoGuess(TaskDetailResponse task, TaskAnswerRequest req)
        {
            if (task.options == null || task.options.Count == 0) return Fail("此任務缺少選項。");
            var correct = task.options.FirstOrDefault(o => o.is_correct);
            
            if (req.selected_option_key == correct?.option_key)
                return Success(task, "地理位置判斷正確！");
            else
                return Fail("地理位置好像不太對，請再確認一下！");
        }

        #endregion

        #region 共用回傳方法

        private TaskAnswerResponse Success(TaskDetailResponse task, string message = "驗證成功")
        {
            return new TaskAnswerResponse { is_correct = true, is_pending_review = false, feedback_message = message };
        }

        private TaskAnswerResponse Fail(string message)
        {
            return new TaskAnswerResponse { is_correct = false, is_pending_review = false, feedback_message = message };
        }

        private TaskAnswerResponse Pending(string message)
        {
            return new TaskAnswerResponse { is_correct = false, is_pending_review = true, feedback_message = message };
        }

        #endregion
    }
}