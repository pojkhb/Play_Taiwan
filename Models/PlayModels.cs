using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace backend.Models
{
    public class NpcInteractionResponse
    {
        public int node_id { get; set; }                  // 節點代號，對應 story_node.sn_id

        // 景點資訊
        public string location_name { get; set; }          // 景點名稱
        public string location_subtitle { get; set; }       // 景點副標題
        public string scene_image_url { get; set; }          // 情境背景圖片網址

        // NPC
        public string npc_id { get; set; }                    // NPC 代號
        public string npc_name { get; set; }                   // NPC 名稱
        public string npc_avatar_url { get; set; }              // NPC 頭像網址

        // 對話
        public string npc_dialogue { get; set; }                // NPC 對話內容
        public string emotion { get; set; }                      // normal / happy / hint

        // 畫面按鈕文字與動作
        public string skip_button_text { get; set; }             // 略過按鈕顯示內容
        public string next_task_id { get; set; }                  // 前往的任務代號
    }

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

    // ===================== 收藏 =====================
    public class FavoriteItemResponse
    {
        public string favorite_id { get; set; }     // 收藏紀錄的代號
        public string item_type { get; set; }         // 收藏項目類型：postcard/badge/vlog
        public string ref_id { get; set; }              // 關聯的項目實際代號
        public string image_url { get; set; }            // 縮圖網址
        public string title { get; set; }                  // 顯示標題
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


    public class LocationRequest
    {
        public double lat { get; set; }
        public double lng { get; set; }
        public double? accuracy { get; set; }
    }


    public class LocationResponse
    {
        public double lat { get; set; }
        public double lng { get; set; }
        public double? accuracy { get; set; }
        public string city_name { get; set; }
        public string district_name { get; set; }
        public DateTime received_at { get; set; }
    }

}
