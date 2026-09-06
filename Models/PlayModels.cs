using System;
using System.Collections.Generic;

namespace backend.Models
{
    // ===================== 登入 / 帳號 =====================
    public class LoginRequest
    {
        public string ep_name { get; set; }      // 探員代號
        public string ep_pswd { get; set; }      // 通行密碼
    }

    public class LoginResponse
    {
        public string token { get; set; }        // JWT 登入權杖
        public string ep_id { get; set; }        // 探員代號
        public string ep_name { get; set; }      // 帳號名稱 (可編輯)
         public int account_type { get; set; }          // 原始數字：1=玩家，2=商家
          public string account_type_name { get; set; }   // 轉換後文字：Tourist / 
    }

    public class EpAccount
    {
        public string ep_id { get; set; }           // 探員/商家代號 (UUID，系統自動產生)。
        public string ep_name { get; set; }         // 探員/商家顯示名稱 (登入時可使用)。
        public int account_type { get; set; }       // 帳號身分：1 = 遊客(Tourist)，2 = 商家(Merchant)。
        public string email { get; set; }           // 電子信箱 (註冊與登入時使用)。
        public string ep_pswd { get; set; }         // 資料庫內儲存的 HMAC SHA256 密碼雜湊。
        public bool is_active { get; set; }         // 帳號總開關：是否啟用 (true=啟用, false=停用)。
        public string email_token { get; set; }     // 信箱驗證專用的暫存 Token (驗證成功後清空)。
        public bool is_email_verified { get; set; } // 信箱是否已通過驗證 (true=已驗證, false=未驗證)。
    }

    public class EpAccountUpdateRequest
    {
        public string ep_name { get; set; }   // 欲更新的探員/商家顯示名稱
    }

    // ===================== 首頁總覽 =====================
    public class HomeOverviewResponse
    {
        public int completed_story_count { get; set; }       // 已完成腳本數
        public int postcard_count { get; set; }               // 已收集明信片數
        public int badge_count { get; set; }                   // 已獲得徽章數
        public int vlog_count { get; set; }                     // 已生成 VLOG 數
        public List<HomeCardItem> recent_cards { get; set; } // 出任探險/過往旅程等首頁卡片清單
    }

    public class HomeCardItem
    {
        public string card_id { get; set; }       // 卡片識別碼
        public string card_type { get; set; }     // 卡片類型，例如 "出任探險" / "過往旅途"
        public string title { get; set; }          // 卡片標題
        public string image_url { get; set; }      // 卡片圖片網址
    }

    // ===================== 劇本 / 選擇模式 =====================
    public class StoryGenerateRequest
{
    public string city_name { get; set; }
    public string town_name { get; set; }
    public int traveler_count { get; set; }
    public List<string> preferences { get; set; }
    public List<string> transportation { get; set; }
    public int node_count { get; set; }
    public bool is_night { get; set; }
    
    // 新增：要求 AI 生成的劇本數量
    public int story_count { get; set; } 
}

    public class StoryWheelSpinResponse
    {
        public string region_id { get; set; }      // 地區固定代號，例如 region_tainan_anping
        public string region { get; set; }          // 前端顯示的地區名稱，例如 台南安平
        public string city_name { get; set; }        // 所屬城市名稱，例如 臺南市
        public string district_name { get; set; }    // 所屬行政區名稱，例如 安平區
    }

    public class StoryOptionResponse
    {
        public string story_id { get; set; }               // 劇本代號
        public string title { get; set; }                   // 劇本標題
        public string prologue { get; set; }                // 前傳劇情
        public string category { get; set; }                // 分類: 文史/美食探險
        public string transport { get; set; }                // 預計交通工具
        public List<string> expected_badges { get; set; }   // 預期可獲得的徽章名稱清單
        public int expected_postcards { get; set; }           // 預期可收集的明信片數量
        public string region_id { get; set; }                 // 地區代號
        public string region { get; set; }                     // 地區名稱
        public List<string> route_preview { get; set; }      // 探索路線預覽(景點名稱清單)

        public class RouteNode
        {
            public string node_id { get; set; }         // 節點代號
            public string location_name { get; set; }    // 景點名稱
            public int node_order { get; set; }            // 節點順序
        }
    }

    public class StoryConfirmRequest
    {
        public string story_id { get; set; }   // 欲確認開始的劇本代號
    }

    // ===================== 地圖 / 探索進度 =====================
    public class MapResponse
    {
        public string story_id { get; set; }                    // 劇本代號
        public int unlocked_node_count { get; set; }             // 已解鎖節點數
        public int total_node_count { get; set; }                 // 總節點數
        public int postcard_unlocked_count { get; set; }          // 已解鎖明信片數
        public int postcard_total_count { get; set; }              // 總明信片數
        public List<MapNode> nodes { get; set; }                 // 地圖節點清單
        public int day_index { get; set; }                        // 第一日/第二日等目前天數索引
        public int total_days { get; set; }                        // 劇本總天數
    }

    public class MapNode
    {
        public string node_id { get; set; }                    // 節點代號
        public string location_name { get; set; }               // 景點名稱
        public double lat { get; set; }                          // 緯度
        public double lng { get; set; }                          // 經度
        public bool is_unlocked { get; set; }                    // 是否已解鎖
        public bool is_night_only { get; set; }                   // 是否為僅限夜間開放的節點
        public string fog_hint { get; set; }                      // 雲霧區隱約描述
        public int day_index { get; set; } // 節點所屬天數，前端用來切換第一日／第二日
        public List<string> child_node_ids { get; set; }        // 樹狀分支的子節點代號清單
        public string image_url { get; set; }                    // 已解鎖景點原圖
        public string silhouette_image_url { get; set; }        // 未解鎖時顯示的剪影圖
        public int node_order { get; set; }                       // 前端畫路線用的排序值
    }

    public class NodeDetailResponse
    {
        public string node_id { get; set; }                 // 節點代號
        public string location_name { get; set; }            // 景點名稱
        public string npc_name { get; set; }                  // 該節點對應的 NPC 名稱
        public string intro_story { get; set; }               // NPC 介紹劇情
        public string opening_hours { get; set; }              // 景點開放時間
        public List<string> nearby_food { get; set; }        // 周邊美食推薦清單
        public string task_id { get; set; }                    // 該節點對應的任務代號
        public string review_story_url { get; set; }          // 回顧劇情連結
    }

    public class NpcInteractionResponse
    {
        public string node_id { get; set; }               // 節點代號

        // 地點／場景
        public string location_name { get; set; }          // 景點名稱
        public string location_subtitle { get; set; }       // 景點副標題
        public string scene_image_url { get; set; }          // 場景背景圖網址

        // NPC
        public string npc_id { get; set; }                    // NPC 代號
        public string npc_name { get; set; }                   // NPC 名稱
        public string npc_avatar_url { get; set; }              // NPC 頭像網址

        // 對話
        public string npc_dialogue { get; set; }                // NPC 對話文字
        public string emotion { get; set; }                      // normal / happy / hint

        // 前端按鈕與後續動作
        public string skip_button_text { get; set; }             // 跳過按鈕顯示文字
        public string next_task_id { get; set; }                  // 隨機互動模板套資訊，指向下一個任務代號
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
    public class TaskDetailResponse
    {
        public string task_id { get; set; }              // 任務代號
        public string node_id { get; set; }               // 所屬節點代號
        public string task_type { get; set; }              // 任務類型（對應 DB 的 task_category，決定驗證方式）
        public string task_description { get; set; }        // 任務題目描述
        public List<TaskOption> options { get; set; }      // 選擇題選項(若為文化問答型)
        public bool requires_photo { get; set; }             // 是否需要拍照
        public bool requires_gps { get; set; }                // 是否需要定位
        public bool requires_group { get; set; }               // 是否為協作解謎型(需多人)

        public string correct_option_key { get; set; }         // 文化問答型-正確選項

        public double? geofence_lat { get; set; }               // GPS區域定位型-中心緯度
        public double? geofence_lng { get; set; }               // GPS區域定位型-中心經度
        public int? geofence_radius_m { get; set; }             // GPS區域定位型-半徑(公尺)
        public int? geofence_dwell_seconds { get; set; }        // GPS區域定位型-最少停留秒數

        public string vision_target_labels_json { get; set; }  // 拍照打卡型-Vision API比對關鍵標籤陣列(JSON)
        public string pose_reference_json { get; set; }        // 短片演繹型-姿勢比對範本資料(JSON)
        public string interview_script_json { get; set; }      // 採訪蒐證型-預期關鍵字腳本(JSON)

        public int? count_target_answer { get; set; }           // 計數推理型-正確數量
        public int? count_tolerance { get; set; }                // 計數推理型-容許誤差

        public string qr_code_token { get; set; }                // QR Code掃碼解鎖型-唯一驗證碼
        public string hidden_unlock_condition_json { get; set; } // 跨關集結型-所需前置關卡條件(JSON)

        public int difficulty_star { get; set; }                  // 難度星級 1~5，對應動態難度機制
        public int wrong_attempt_tolerance { get; set; }          // 容許錯誤次數(誘惑閾值)，超過視為需要提示
        public int recommended_players_min { get; set; }          // 建議遊玩人數下限
        public int recommended_players_max { get; set; }          // 建議遊玩人數上限

        public string reward_postcard_id { get; set; }            // 答對後解鎖的明信片代號
        public string reward_badge_id { get; set; }               // 答對後可能觸發的徽章代號
    }

    public class TaskOption
    {
        public string option_key { get; set; }    // 選項代號 A/B/C/D
        public string option_text { get; set; }    // 選項文字內容
    }

    public class TaskAnswerRequest
    {
        public string task_id { get; set; }                // 任務代號
        public string selected_option_key { get; set; }     // 文化問答型-選擇的選項
        public string photo_url { get; set; }                // 拍照打卡型-玩家上傳的照片網址
        public string text_answer { get; set; }               // 通用文字答案：採訪關鍵字/跨關集結型推理答案
        public double? lat { get; set; }                       // GPS 相關類型-玩家目前緯度
        public double? lng { get; set; }                        // GPS 相關類型-玩家目前經度

        public string ep_id { get; set; }                        // 探員代號，QR Code 綁定帳號驗證需要
        public string video_url { get; set; }                    // 短片演繹型-玩家上傳的影片網址
        public string audio_url { get; set; }                     // 採訪蒐證型-玩家錄音網址(語音轉文字用)
        public int? count_answer { get; set; }                    // 計數推理型-玩家輸入的數量
        public int? dwell_seconds { get; set; }                   // GPS區域定位型-玩家在範圍內已停留秒數
        public string qr_token { get; set; }                       // QR Code掃碼解鎖型-掃到的驗證碼

        public List<string> completed_task_ids { get; set; } = new();               // 跨關集結型-已完成的前置關卡task_id清單
        public Dictionary<string, string> collected_fragments { get; set; } = new(); // 跨關集結型-各關卡蒐集到的線索片段
    }

    public class TaskAnswerResponse
    {
        public bool is_correct { get; set; }                  // 答案是否正確
        public bool is_pending_review { get; set; }             // 是否送人工審核中(短片演繹型信心不足時)
        public string feedback_message { get; set; }             // 給玩家的回饋文字
        public string unlocked_postcard_id { get; set; }          // 答對後解鎖的明信片代號
        public int unlocked_node_progress { get; set; }            // 目前已解鎖節點進度
        public int total_node_count { get; set; }                   // 該劇本總節點數
    }

    public class TaskHintResponse
    {
        public string task_id { get; set; }             // 任務代號
        public string npc_avatar_url { get; set; }        // 提示對話框顯示的NPC頭像
        public string hint_text { get; set; }              // 提示文字內容
        public bool is_available { get; set; }              // 是否已達到答錯次數門檻，可顯示提示
    }

    // ===================== 明信片 =====================
    public class PostcardResponse
    {
        public string postcard_id { get; set; }        // 明信片代號
        public string title { get; set; }                // 明信片標題，e.g. 臺南孔廟-全台首學的書香
        public string subtitle { get; set; }               // 明信片副標題
        public string front_image_url { get; set; }         // AI 生成正面圖片網址
        public string back_photo_url { get; set; }            // 玩家拍攝的背面照片網址
        public string culture_note { get; set; }                // 文化解說文字
        public DateTime found_date { get; set; }                 // 取得日期
        public bool is_night_edition { get; set; }                 // 是否為夜間限定版本
    }

    public class PostcardPrintRequest
    {
        public string postcard_id { get; set; }   // 欲列印的明信片代號
    }

    public class PostcardPrintResponse
    {
        public string ibon_pickup_code { get; set; }    // 對應 pincode (取件碼)
        public string pdf_url { get; set; }               // 原本的圖片網址
        public string deadline { get; set; }              // ★ 新增：取件期限
        public string qrcode_base64 { get; set; }         // ★ 新增：QRCode 的 Base64 字串
    }

    public class PostcardShareRequest
    {
        public string postcard_id { get; set; }   // 欲分享的明信片代號
        public string platform { get; set; }        // 分享平台，例如 IG 等
    }

    // ===================== 徽章 =====================
    public class BadgeResponse
    {
        public string badge_id { get; set; }             // 徽章代號
        public string badge_name { get; set; }             // 徽章名稱
        public string badge_type { get; set; }               // 徽章類型：特色/景點/系列
        public string image_url { get; set; }                 // 徽章圖片網址
        public DateTime obtained_date { get; set; }             // 取得日期
    }

    // ===================== Vlog / 劇本結束 / 回顧 =====================
    public class StoryEndingResponse
    {
        public string story_id { get; set; }                          // 劇本代號
        public string title { get; set; }                               // 劇本標題，例如 府城儒生的失落卷
        public int walked_steps { get; set; }                            // 跋涉步數
        public string task_completion_ratio { get; set; }                 // 破解謎題比例，例如 16/16
        public string postcard_completion_ratio { get; set; }              // 尋獲明信片比例，例如 10/10
        public string ending_type { get; set; }                             // 結局類型：一般結局/隱藏結局
    }

    public class VlogGenerateRequest
    {
        public string story_id { get; set; }   // 欲生成 Vlog 的劇本代號
    }

    public class VlogResponse
    {
        public string vlog_id { get; set; }              // Vlog 代號
        public string story_id { get; set; }               // 所屬劇本代號
        public string video_url { get; set; }                // 影片網址
        public string thumbnail_url { get; set; }              // 影片縮圖網址
        public DateTime completed_date { get; set; }            // 生成完成日期
    }

    // ===================== 過往 (History) =====================
    public class HistoryStoryItem
    {
        /// <summary>劇本代號，對應 md_story.story_id，例如 story_tainan_001。</summary>
        public string story_id { get; set; }                    // 劇本代號

        /// <summary>劇本標題。</summary>
        public string title { get; set; }                        // 劇本標題

        /// <summary>劇本簡介文字。</summary>
        public string synopsis { get; set; }                      // 劇本簡介

        /// <summary>此劇本完成的日期時間。</summary>
        public DateTime completed_date { get; set; }               // 完成日期

        /// <summary>劇本所屬地區名稱，例如「台南永康區」。</summary>
        public string region { get; set; }                          // 所屬地區名稱

        /// <summary>探索路線摘要，依序列出玩家走過的景點名稱。</summary>
        public List<string> route_summary { get; set; }              // 探索路線摘要(景點名稱清單)

        /// <summary>此劇本對應生成的 Vlog 代號，若尚未生成則為 null。</summary>
        public string vlog_id { get; set; }                            // 對應的 Vlog 代號

        /// <summary>明信片回顧頁面的連結，若無則為 null。</summary>
        public string postcard_review_url { get; set; }                 // 明信片回顧連結
        public List<string> spots { get; set; }
    }

    // ===================== 收藏 =====================
    public class FavoriteItemResponse
    {
        public string favorite_id { get; set; }     // 收藏紀錄代號
        public string item_type { get; set; }         // 收藏項目類型：postcard/badge/vlog
        public string ref_id { get; set; }              // 對應項目的實際代號
        public string image_url { get; set; }            // 縮圖網址
        public string title { get; set; }                  // 顯示標題
    }

    // ===================== 周邊好去 (任務/周邊資訊) =====================
    public class NearbyPlaceResponse
    {
        public string place_id { get; set; }                // 景點/店家代號
        public string category { get; set; }                  // 分類：飲食/其他
        public string name { get; set; }                        // 名稱
        public string address { get; set; }                      // 地址
        public string open_time { get; set; }                      // 開放/營業時間
        public List<string> photo_urls { get; set; }              // 照片網址清單
        public string maps_deeplink_url { get; set; }                // Google Maps 導航連結
    }

    /// <summary>
    /// 短片演繹型-姿勢比對範本資料，對應 md_task.pose_reference_json 解析後的結構。
    /// </summary>
    public class PoseReference
    {
        public List<double> JointAngles { get; set; } = new(); // 範本動作的關節角度序列
    }

    /// <summary>
    /// 採訪蒐證型-預期關鍵字腳本，對應 md_task.interview_script_json 解析後的結構。
    /// </summary>
    public class InterviewScript
    {
        public List<string> ExpectedKeywords { get; set; } = new(); // 店家/NPC回覆中應包含的關鍵字清單
    }

    /// <summary>
    /// 跨關集結型-解鎖條件，對應 md_task.hidden_unlock_condition_json 解析後的結構。
    /// </summary>
    public class CrossLevelCondition
    {
        public List<string> RequiredTaskIds { get; set; } = new(); // 終章解謎所需的前置關卡task_id清單
    }

    /// <summary>
    /// 隱藏關卡觸發檢查結果。對應 md_hidden_level 表，於玩家GPS回報時被動觸發，不對外開放主動查詢。
    /// </summary>
    public class HiddenLevelTriggerResult
    {
        public bool triggered { get; set; }               // 這次檢查是否觸發了新的隱藏關卡
        public string hidden_level_id { get; set; }        // 觸發的隱藏關卡代號
        public string title { get; set; }                    // 隱藏關卡標題
        public string cultural_background { get; set; }       // 在地歷史/文化背景說明
        public string content { get; set; }                     // 支線劇情內容
        public string reward_badge_id { get; set; }               // 觸發後可能給予的徽章代號
        public string reward_postcard_id { get; set; }              // 觸發後可能給予的明信片代號
    }
    
}