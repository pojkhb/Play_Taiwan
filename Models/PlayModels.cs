using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace backend.Models
{
    // ===================== 登入 / 註冊 =====================
    public class LoginRequest
    {
        public string ep_name { get; set; }      // 帳號名稱
        public string ep_pswd { get; set; }      // 登入密碼
    }

    public class LoginResponse
    {
        public string token { get; set; }        // JWT 登入憑證
        public string ep_id { get; set; }        // 帳號代號
        public string ep_name { get; set; }      // 帳號名稱 (顯示用)
        public int account_type { get; set; }    // 帳號類型，1=玩家，2=商家
        public string account_type_name { get; set; } // 帳號類型名稱，Tourist / Merchant
    }

    public class EpAccount
    {
        public string ep_id { get; set; }           // 帳號代號 (UUID)
        public string ep_name { get; set; }         // 帳號顯示名稱
        public int account_type { get; set; }       // 帳號類型，1=玩家(Tourist)，2=商家(Merchant)
        public string email { get; set; }           // 登入信箱 (註冊或找回密碼使用)
        public string ep_pswd { get; set; }         // 密碼加密後字串，HMAC SHA256 密碼加密
        public bool is_active { get; set; }         // 帳號狀態，是否啟用 (true=啟用, false=停用)
        public string email_token { get; set; }     // 信箱驗證使用的隨機 Token (驗證完成後清空)
        public bool is_email_verified { get; set; } // 信箱是否已經完成驗證 (true=已驗證, false=未驗證)
    }

    public class EpAccountUpdateRequest
    {
        public string ep_name { get; set; }   // 欲更新的帳號顯示名稱
    }

    // ===================== 首頁總覽 =====================
    public class HomeOverviewResponse
    {
        public int completed_story_count { get; set; }       // 已完成故事數量
        public int postcard_count { get; set; }               // 已收集明信片數量
        public int badge_count { get; set; }                   // 已解鎖徽章數量
        public int vlog_count { get; set; }                     // 已產生 VLOG 數量
        public List<HomeCardItem> recent_cards { get; set; } // 最近收藏/探險的卡片等列表資料
    }

    public class HomeCardItem
    {
        public string card_id { get; set; }       // 卡片唯一代號
        public string card_type { get; set; }     // 卡片類型，例如 最近探險 / 收藏 等
        public string title { get; set; }          // 卡片標題
        public string image_url { get; set; }      // 卡片圖片網址
    }

    // ===================== 故事 / 推薦相關 =====================
    public class StoryGenerateRequest
    {
        public string city_name { get; set; }
        public string town_name { get; set; }
        public int traveler_count { get; set; }
        public List<string> preferences { get; set; }
        public List<string> transportation { get; set; }
        public int node_count { get; set; }
        public bool is_night { get; set; }
        
        // 提示，給 AI 生成的故事數量
        public int story_count { get; set; } 
    }

    public class StoryWheelSpinResponse
    {
        public string region_id { get; set; }      // 地區特定代號，例如 region_tainan_anping
        public string region { get; set; }          // 畫面顯示的地區名稱，例如 台南安平
        public string city_name { get; set; }        // 該區塊所屬縣市，如 台南市
        public string district_name { get; set; }    // 該區塊鄉鎮名稱，例如 安平區
    }

    public class StoryOptionResponse
    {
        public string story_id { get; set; }               // 故事代號
        public string title { get; set; }                   // 故事標題
        public string prologue { get; set; }                // 前導故事
        public string category { get; set; }                // 分類: 歷史/文化探險
        public string transport { get; set; }                // 建議交通工具
        public List<string> expected_badges { get; set; }   // 預期會解鎖的徽章名稱陣列
        public int expected_postcards { get; set; }           // 預期會收集的明信片數量
        public string region_id { get; set; }                 // 地區代號
        public string region { get; set; }                     // 地區名稱
        public List<string> route_preview { get; set; }      // 預覽路線節點(景點名稱陣列)

        public class RouteNode
        {
            public string node_id { get; set; }         // 節點代號
            public string location_name { get; set; }    // 景點名稱
            public int node_order { get; set; }            // 節點順序
        }
    }

    public class StoryConfirmRequest
    {
        public string story_id { get; set; }   // 使用者確認選擇的故事代號
    }

    // ===================== 地圖 / 路線導覽 =====================
    public class MapResponse
    {
        public string story_id { get; set; }                    // 故事代號
        public int unlocked_node_count { get; set; }             // 已解鎖節點數
        public int total_node_count { get; set; }                 // 總節點數
        public int postcard_unlocked_count { get; set; }          // 已解鎖明信片數量
        public int postcard_total_count { get; set; }              // 總明信片數量
        public List<MapNode> nodes { get; set; }                 // 地圖節點列表
        public int day_index { get; set; }                        // 第幾天的行程天數索引
        public int total_days { get; set; }                        // 故事總天數
    }

    public class MapNode
    {
        public string node_id { get; set; }                    // 節點代號
        public string location_name { get; set; }               // 景點名稱
        public double lat { get; set; }                          // 緯度
        public double lng { get; set; }                          // 經度
        public bool is_unlocked { get; set; }                    // 是否已解鎖
        public bool is_night_only { get; set; }                   // 是否為夜晚限定景點
        public string fog_hint { get; set; }                      // 迷霧探索提示
        public int day_index { get; set; }                        // 節點所屬天數，畫面上為第幾天
        public List<string> child_node_ids { get; set; }        // 包含的子節點代號列表
        public string image_url { get; set; }                    // 已解鎖景點圖片
        public string silhouette_image_url { get; set; }        // 未解鎖的顯示剪影圖片
        public int node_order { get; set; }                       // 畫面連線用的順序值
    }

    public class NodeDetailResponse
    {
        public string node_id { get; set; }                 // 節點代號
        public string location_name { get; set; }            // 景點名稱
        public string npc_name { get; set; }                  // 探索節點出現的 NPC 名稱
        public string intro_story { get; set; }               // NPC 介紹故事
        public string opening_hours { get; set; }              // 景點開放時間
        public List<string> nearby_food { get; set; }        // 附近美食推薦陣列
        public string task_id { get; set; }                    // 探索節點對應的任務代號
        public string review_story_url { get; set; }          // 回顧故事網址
    }

    public class NpcInteractionResponse
    {
        public string node_id { get; set; }               // 節點代號

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

    public class NavigationRequest
    {
        public string node_id { get; set; }   // 欲導航前往的節點代號
    }

    public class NavigationResponse
    {
        public string maps_deeplink_url { get; set; }   // Google Maps 導航連結
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

    // ===================== 明信片 =====================
    public class PostcardResponse
    {
        public string postcard_id { get; set; }        // 明信片代號
        public string title { get; set; }                // 明信片標題
        public string subtitle { get; set; }               // 明信片副標題
        public string front_image_url { get; set; }         // AI 生成正面圖片網址
        public string back_photo_url { get; set; }            // 玩家拍攝的背面照片網址
        public string culture_note { get; set; }                // 文化解說內容
        public DateTime found_date { get; set; }                 // 獲得日期
        public bool is_night_edition { get; set; }                 // 是否為夜晚限定版
    }

    public class PostcardPrintRequest
    {
        public string postcard_id { get; set; }   // 欲列印的明信片代號
    }

    public class PostcardPrintResponse
    {
        public string ibon_pickup_code { get; set; }    // ibon 取件代碼 (pincode)
        public string pdf_url { get; set; }               // 生成的 PDF 網址
        public string deadline { get; set; }              // 列印期限時間
        public string qrcode_base64 { get; set; }         // 列印用 QRCode 的 Base64 編碼
    }

    public class PostcardShareRequest
    {
        public string postcard_id { get; set; }   // 欲分享的明信片代號
        public string platform { get; set; }        // 分享平台，例如 IG
    }

    // ===================== 徽章 =====================
    public class BadgeResponse
    {
        public string badge_id { get; set; }             // 徽章代號
        public string badge_name { get; set; }             // 徽章名稱
        public string badge_type { get; set; }               // 徽章類型：景點/類型/等級
        public string image_url { get; set; }                 // 徽章圖片網址
        public DateTime obtained_date { get; set; }             // 獲得日期
    }

    // ===================== Vlog / 故事結尾 / 回顧 =====================
    public class StoryEndingResponse
    {
        public string story_id { get; set; }                          // 故事代號
        public string title { get; set; }                               // 故事標題
        public int walked_steps { get; set; }                            // 步行步數
        public string task_completion_ratio { get; set; }                 // 任務完成比例
        public string postcard_completion_ratio { get; set; }              // 收集明信片比例
        public string ending_type { get; set; }                             // 結尾類型：完美結局/一般結局
    }

    public class VlogGenerateRequest
    {
        public string story_id { get; set; }   // 欲生成 Vlog 的故事代號
    }

    public class VlogResponse
    {
        public string vlog_id { get; set; }              // Vlog 代號
        public string story_id { get; set; }               // 所屬故事代號
        public string video_url { get; set; }                // 影片網址
        public string thumbnail_url { get; set; }              // 影片縮圖網址
        public DateTime completed_date { get; set; }            // 生成完成日期
    }

    // ===================== 歷史紀錄 (History) =====================
    public class HistoryStoryItem
    {
        /// <summary>故事代號，關聯 md_story.story_id</summary>
        public string story_id { get; set; }                    // 故事代號

        /// <summary>故事標題</summary>
        public string title { get; set; }                        // 故事標題

        /// <summary>故事簡介內容</summary>
        public string synopsis { get; set; }                      // 故事簡介

        /// <summary>使用者完成旅行的日期時間</summary>
        public DateTime completed_date { get; set; }               // 完成日期

        /// <summary>故事所屬地區名稱</summary>
        public string region { get; set; }                          // 所屬地區名稱

        /// <summary>路線節點預覽，依順序包含各節點的景點名稱</summary>
        public List<string> route_summary { get; set; }              // 路線節點預覽(景點名稱陣列)

        /// <summary>使用者完成獲得的 Vlog 代號，若尚未生成則為 null</summary>
        public string vlog_id { get; set; }                            // 關聯的 Vlog 代號

        /// <summary>明信片回顧頁面的連結，若無則為 null。</summary>
        public string postcard_review_url { get; set; }                 // 明信片回顧連結
        public List<string> spots { get; set; }
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

    // ===================== 附近景點 (任務/附近資訊) =====================
    public class NearbyPlaceResponse
    {
        public string place_id { get; set; }                // 景點/店家代號
        public string category { get; set; }                  // 分類：美食及其他
        public string name { get; set; }                        // 名稱
        public string address { get; set; }                      // 地址
        public string open_time { get; set; }                      // 開放/營業時間
        public List<string> photo_urls { get; set; }              // 圖片網址陣列
        public string maps_deeplink_url { get; set; }                // Google Maps 導航連結
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