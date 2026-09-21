using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace backend.Models
{
    // ===================== 任務 / 答題 / 提示 =====================
    public class TaskListReq
    {
        public string node_id { get; set; } // 欲進行任務的節點代號
        public float gps_lon { get; set; } // 玩家目前經度
        public float gps_lat { get; set; } // 玩家目前緯度
        // player_count 已移除：任務生成改在劇本生成（POST api/Story/GenerateAi）時就決定，
        // 這裡純讀取 md_task，人數不影響查詢結果。

        // 協作解謎型（type_id=5）專用：標示這是隊伍中的第幾位玩家（1 或 2），
        // 後端會依此回傳對應角色的線索文字。其他題型忽略此欄位，未帶值視為 1。
        public int player_index { get; set; } = 1;
    }

    /// <summary>
    /// 測試用：手動觸發任務生成的請求。
    /// 正式流程的任務生成是在 POST api/Story/GenerateAi 劇本存檔後自動執行。
    /// </summary>
    public class TaskGenerateReq
    {
        public string story_id { get; set; }     // 為整份劇本的所有節點生成
        public string node_id { get; set; }      // 有值時只針對此節點生成（優先於 story_id）
        public int player_count { get; set; }    // 遊玩人數，未給預設 2
    }

    public class SearchNeo4jReq
    {
        public string place_id { get; set; } // 欲查詢的景點代號
        public string story_id { get; set; } // 所屬故事代號
    }

    /// <summary>
    /// 回傳給前端的任務詳細資訊
    /// </summary>
    public class TaskDetailResponse
    {
        public int task_id { get; set; }
        public string story_id { get; set; }
        public string node_id { get; set; }
        public string task_place_id { get; set; }
        public int type_id { get; set; }
        public string task_type { get; set; }

        public string task_describe { get; set; } // AI 生成的任務描述（協作解謎型時，已依 player_index 換成對應角色的線索）

        public List<TaskOption> options { get; set; } // 選擇題選項
        public List<string> media_urls { get; set; } // 使用者上傳圖片/影片

        // 協作解謎型（type_id=5）專用欄位，僅供後端內部換算 task_describe 用，不回傳給前端。
        [JsonIgnore]
        public string task_describe_b { get; set; } // 玩家 B 看到的線索文字

        // 文字問答類題型（跨關集結型/協作解謎型）的正確答案，供 SubmitAnswer 核對，不回傳給前端。
        [JsonIgnore]
        public string correct_answer { get; set; }
    }

    public class TaskOption
    {
        public string option_key { get; set; }   // 選項標籤 (A,B,C)
        public string option_text { get; set; }  // 選項文字
        public string option_url { get; set; }   // 選項圖片網址(可選)
        [JsonIgnore]
        public bool is_correct { get; set; }     // 是否為正確解答(不會回傳給前端)
    }

    public class TaskAnswerRequest
    {
        public int task_id { get; set; }                           // 任務代號 (md_task.task_id)

        public float gps_lon { get; set; }                         // 玩家提交當下的經度（位置驗證用）

        public float gps_lat { get; set; }                         // 玩家提交當下的緯度（位置驗證用）

        public string selected_option_key { get; set; }            // 選擇題型 (A/B/C)

        public string text_answer { get; set; }                    // 文字問答型

        public string photo_url { get; set; }                      // 照片上傳型

        public string video_url { get; set; }                      // 短片演繹型

        public string audio_url { get; set; }                      // 採訪蒐證型

        [System.Text.Json.Serialization.JsonIgnore]
        public string ep_id { get; set; }                          // 後端從 Token 取得
    }

    public class TaskAnswerResponse
    {
        public bool is_correct { get; set; }                  // 答題是否正確
        public bool is_pending_review { get; set; }             // 是否人工審核中
        public string feedback_message { get; set; }             // 系統回饋文字
        public string unlocked_postcard_id { get; set; }          // 答對後解鎖的明信片代號
        public int unlocked_node_progress { get; set; }            // 目前已解鎖節點進度
        public int total_node_count { get; set; }                   // 任務總節點數
    }

    public class TaskHintResponse
    {
        public string task_id { get; set; }             // 任務代號
        public string npc_avatar_url { get; set; }        // 提示對話框顯示的NPC頭像
        public string hint_text { get; set; }              // 提示文字內容
        public bool is_available { get; set; }              // 是否有可用的提示內容
    }

    /// <summary>
    /// 相機姿勢比對所需特徵資訊，關聯 md_task.pose_reference_json 解析後之結構。
    /// </summary>
    public class PoseReference
    {
        public List<double> JointAngles { get; set; } = new(); // 姿勢特徵的各關節角度陣列
    }


    /// <summary>
    /// 採訪任務語音轉文字識別，關聯 md_task.interview_script_json 解析後之結構。
    /// </summary>
    public class InterviewScript
    {
        public List<string> ExpectedKeywords { get; set; } = new(); // 店家/NPC對話中需包含的關鍵字陣列
    }


    /// <summary>
    /// 跨節點任務解鎖條件，關聯 md_task.hidden_unlock_condition_json 解析後之結構。
    /// </summary>
    public class CrossLevelCondition
    {
        public List<string> RequiredTaskIds { get; set; } = new(); // 解鎖此任務所需之前置 task_id 陣列
    }


    /// <summary>
    /// 隱藏劇情觸發檢查結果，關聯 md_hidden_level 表，依玩家 GPS 座標判斷是否進入範圍，若符合則返回劇情。
    /// </summary>
    public class HiddenLevelTriggerResult
    {
        public bool triggered { get; set; }               // 此次檢查是否觸發了新隱藏劇情
        public string hidden_level_id { get; set; }        // 觸發的隱藏劇情代號
        public string title { get; set; }                    // 隱藏劇情標題
        public string cultural_background { get; set; }       // 歷史背景/文化說明
        public string content { get; set; }                     // 隱藏劇情內容
        public string reward_badge_id { get; set; }               // 觸發後可獲得的徽章代號
        public string reward_postcard_id { get; set; }              // 觸發後可獲得的明信片代號
    }

    /// <summary>任務線索提示 (對應 md_task_hint)。</summary>
    public class TaskHint
    {
        public int HintId { get; set; }                    // 提示紀錄唯一編號
        public string TaskId { get; set; }                  // 對應的任務代號
        public int HintStage { get; set; }                  // 提示階段(第幾階段的提示，數字越大提示越明顯)
        public int TriggerWrongCount { get; set; }          // 累積答錯幾次後觸發此階段提示
        public string HintText { get; set; }                // 提示文字內容
        public string LlmPromptTemplate { get; set; }        // 給 LLM 動態生成提示用的提示詞範本
        public bool IsActive { get; set; }                   // 是否啟用此提示
    }

    /// <summary>動態難度 LLM 提示字 (對應 md_difficulty_prompt)。</summary>
    public class DifficultyPrompt
    {
        public int DifficultyStar { get; set; }              // 難度星級 1~5
        public string Title { get; set; }                     // 該難度等級的標題名稱
        public string LlmPromptTemplate { get; set; }          // 給 LLM 依此難度生成內容用的提示詞範本
        public int RaiseVisitThreshold { get; set; }            // 累積造訪次數達到此門檻後，難度自動提升
    }

    /// <summary>隱藏關卡 (對應 md_hidden_level)。</summary>
    public class HiddenLevel
    {
        public string HiddenLevelId { get; set; }            // 隱藏關卡代號
        public string RegionId { get; set; }                  // 所屬地區代號
        public string PlaceId { get; set; }                    // 所屬景點代號
        public string StoryId { get; set; }                     // 所屬劇本代號
        public string Title { get; set; }                        // 隱藏關卡標題
        public string CulturalBackground { get; set; }            // 在地歷史/文化背景說明
        public string Content { get; set; }                        // 支線劇情內容
        public decimal? TriggerLat { get; set; }                    // 觸發此隱藏關卡所需的GPS緯度
        public decimal? TriggerLng { get; set; }                     // 觸發此隱藏關卡所需的GPS經度
        public int TriggerRadiusM { get; set; }                       // 觸發範圍半徑(公尺)
        public string RewardBadgeId { get; set; }                     // 觸發後可能給予的徽章代號
        public string RewardPostcardId { get; set; }                   // 觸發後可能給予的明信片代號
        public bool IsActive { get; set; }                              // 是否啟用此隱藏關卡
    }

    /// <summary>探員造訪次數 (對應 ep_visit_count)，用於動態難度判定。</summary>
    public class VisitCount
    {
        public string EpId { get; set; }                     // 探員代號
        public string RegionId { get; set; }                  // 地區代號
        public int VisitCountValue { get; set; }                // 累積造訪次數
        public int CurrentDifficultyStar { get; set; }            // 目前套用的難度星級 1~5
    }
}
