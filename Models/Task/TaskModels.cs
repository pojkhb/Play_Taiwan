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
        // player_count 已移除：任務在劇本生成時就決定，這裡純讀取 task，人數不影響查詢結果。
        // player_index 已移除：協作解謎型的座位改由後端依登入者（JWT）在協作隊伍中的 seat_no 決定。
    }

    /// <summary>
    /// 測試用（前端不用接）：手動觸發任務生成的請求。
    /// 正式流程的任務在 GenerateGameStory 生成劇本時就一併生成。
    /// </summary>
    public class TaskGenerateReq
    {
        public string story_id { get; set; }     // 為整份劇本的所有節點生成
        public string node_id { get; set; }      // 有值時只針對此節點生成（優先於 story_id）
        public int player_count { get; set; }    // 遊玩人數，未給預設 2
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

        public string task_describe { get; set; } // 任務描述（協作解謎型為兩人共同的題目）

        public string clue_text { get; set; } // 協作解謎型（type_id=5）：登入者座位（協作隊伍的 seat_no）在 task_clue 的線索，其他題型為 null

        public int pass { get; set; } // 任務通關狀態（task.pass）：0=未通過、1=已通過

        public List<TaskOption> options { get; set; } // 選擇題選項
        public List<string> media_urls { get; set; } // 使用者上傳圖片/影片

        // 協作解謎型（type_id=5）生成時使用：座位 2 的線索（座位 1 放 clue_text），寫入 task_clue，不回傳給前端。
        [JsonIgnore]
        public string task_describe_b { get; set; }

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
        public int task_id { get; set; }                           // 任務代號 (task.task_id)；要帶哪個作答欄位見節點遊玩畫面的 answer_mode

        public float gps_lon { get; set; }                         // 玩家提交當下的經度（位置驗證用）

        public float gps_lat { get; set; }                         // 玩家提交當下的緯度（位置驗證用）

        public string selected_option_key { get; set; }            // 選擇題型 (A/B/C)

        public string text_answer { get; set; }                    // 文字問答型

        public string photo_url { get; set; }                      // 照片上傳型

        public string video_url { get; set; }                      // 短片演繹型

        public string audio_url { get; set; }                      // 採訪蒐證型

        [System.Text.Json.Serialization.JsonIgnore]
        public int au_id { get; set; }                             // 後端從 Token 取得（登入者 au_id）
    }

    public class TaskAnswerResponse
    {
        public bool is_correct { get; set; }                  // 答題是否正確
        public int pass { get; set; }                           // 作答後的任務通關狀態（task.pass）：0=未通過、1=已通過
        public bool is_pending_review { get; set; }             // 是否人工審核中
        public string feedback_message { get; set; }             // 系統回饋文字
        public NodeProgress node_progress { get; set; }            // 作答後這一站的進度（答完不用重抓節點遊玩畫面）
        public bool node_completed { get; set; }                     // 這一站的任務是否已全部通過
        public bool story_completed { get; set; }                      // 整份劇本的任務是否已全部通過（此時遊玩紀錄與隊伍已自動標成完成）
    }

    /// <summary>一站的任務進度</summary>
    public class NodeProgress
    {
        public int passed { get; set; }        // 已通過題數
        public int total { get; set; }         // 總題數
        public bool all_passed { get; set; }   // 是否全部通過
    }

    /// <summary>
    /// 作答方式（NodePlayTask.answer_mode），決定前端顯示哪種輸入、作答時送哪個欄位：
    /// choice → selected_option_key；text → text_answer；photo → photo_url；
    /// audio_or_video → audio_url 或 video_url；gps → 不用帶作答欄位，在現場送出即可。
    /// </summary>
    public static class AnswerModes
    {
        public const string Gps = "gps";
        public const string Text = "text";
        public const string Choice = "choice";
        public const string Photo = "photo";
        public const string AudioOrVideo = "audio_or_video";

        /// <summary>依任務類型（type.type_id）決定作答方式，對應 TaskVerificationService 的驗證規則</summary>
        public static string ForType(int typeId) => typeId switch
        {
            1 => Gps,
            2 => Text,
            3 => Photo,
            4 => Photo,
            5 => Text,
            6 => Choice,
            7 => Choice,
            8 => AudioOrVideo,
            9 => Choice,
            10 => Choice,
            _ => Text
        };

        /// <summary>作答方式需要的欄位沒帶時回傳提示訊息（SubmitAnswer 回 400），都有帶時回傳 null</summary>
        public static string MissingFieldMessage(string answerMode, TaskAnswerRequest req) => answerMode switch
        {
            Text when string.IsNullOrWhiteSpace(req.text_answer) => "請輸入文字答案（text_answer）",
            Choice when string.IsNullOrWhiteSpace(req.selected_option_key) => "請選擇一個選項（selected_option_key）",
            Photo when string.IsNullOrWhiteSpace(req.photo_url) => "請先上傳照片（photo_url）",
            AudioOrVideo when string.IsNullOrWhiteSpace(req.audio_url) && string.IsNullOrWhiteSpace(req.video_url)
                => "請先上傳錄音或影片（audio_url 或 video_url）",
            _ => null
        };
    }

    /// <summary>節點遊玩畫面（GET api/Task/Node/{node_id}）：一次拿到這一站畫面需要的所有資料</summary>
    public class NodePlayResponse
    {
        public NodePlayNode node { get; set; }
        public int my_seat_no { get; set; }                  // 登入者的座位（協作解謎看的線索），沒有隊伍的擁有者為 1
        public NodeProgress progress { get; set; }
        public List<NodePlayTask> tasks { get; set; } = new();
    }

    public class NodePlayNode
    {
        public int node_id { get; set; }               // story_node.sn_id
        public int story_id { get; set; }
        public int node_order { get; set; }
        public string title { get; set; }
        public string location_codename { get; set; }
        public string opening_text { get; set; }       // 抵達時的開場劇情
        public string success_text { get; set; }       // 完成劇情，這一站任務全部通過後才有值
    }

    public class NodePlayTask
    {
        public int task_id { get; set; }               // 作答、取提示用的任務代號
        public int type_id { get; set; }
        public string type_name { get; set; }
        public string answer_mode { get; set; }        // 見 AnswerModes
        public string task_describe { get; set; }
        public string clue_text { get; set; }          // 協作解謎型：自己座位的線索，其他題型為 null
        public List<NodePlayOption> options { get; set; } = new();
        public int pass { get; set; }                  // 0=未通過、1=已通過（協作隊伍共用）
        public int wrong_count { get; set; }           // 自己在這題答錯的次數
        public bool hint_available { get; set; }       // 是否可以取提示（答錯次數達門檻、還沒通過、這題有提示；門檻依題目難易度與玩家表現）
    }

    /// <summary>選項（不含正確答案）</summary>
    public class NodePlayOption
    {
        public string option_key { get; set; }
        public string option_text { get; set; }
        public string option_url { get; set; }
    }

    public class TaskHintResponse
    {
        public string task_id { get; set; }             // 任務代號
        public string npc_avatar_url { get; set; }        // 提示對話框顯示的 NPC 圖片（完整網址）：這一站的 NPC，沒有指定時是預設的薯光
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

    /// <summary>題型解鎖進度（GET api/Task/Progress）：玩家在目前所在鄉鎮市區的題型解鎖狀態</summary>
    public class TaskProgressResponse
    {
        public string city_name { get; set; }          // 所在縣市，查不到所在區時為 null
        public string district_name { get; set; }      // 所在鄉鎮市區，查不到所在區時為 null
        public bool is_first_visit { get; set; }       // 是否第一次到這個區（還沒在這個區抵達過任何一站），查不到所在區時為 true
        public List<TaskTypeUnlock> unlocked_types { get; set; } = new();
        public List<TaskTypeUnlock> locked_types { get; set; } = new();
    }

    public class TaskTypeUnlock
    {
        public int type_id { get; set; }
        public string type_name { get; set; }
        public string unlock_hint { get; set; }        // 解鎖條件，已解鎖的題型為 null
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
}
