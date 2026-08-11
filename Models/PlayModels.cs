using System;
using System.Collections.Generic;

namespace backend.Models
{
    // ===================== 登入 / 帳號 =====================
    public class LoginRequest
    {
        public string ep_id { get; set; }      // 探員代號
        public string ep_pswd { get; set; }    // 通行密碼
    }

    public class LoginResponse
    {
        public string token { get; set; }
        public string ep_id { get; set; }
        public string ep_name { get; set; }    // 帳號名稱 (可編輯)
    }
    public class EpAccount
    {
        public string ep_id { get; set; }      // 探員代號。
        public string ep_name { get; set; }    // 探員顯示名稱。
        public string ep_pswd { get; set; }    // 資料庫內儲存的 HMAC SHA256 密碼雜湊。
        public bool is_active { get; set; }    // 帳號是否啟用。
    }

    public class EpAccountUpdateRequest
    {
        public string ep_name { get; set; }
    }

    // ===================== 首頁總覽 =====================
    public class HomeOverviewResponse
    {
        public int completed_story_count { get; set; }   // 已完成腳本
        public int postcard_count { get; set; }           // 明信片數
        public int badge_count { get; set; }               // 徽章數
        public int vlog_count { get; set; }                 // VLOG數
        public List<HomeCardItem> recent_cards { get; set; } // 出任探險/過往旅程等首頁卡片
    }

    public class HomeCardItem
    {
        public string card_id { get; set; }
        public string card_type { get; set; }   // e.g. "出任探險" / "過往旅途"
        public string title { get; set; }
        public string image_url { get; set; }
    }

    // ===================== 劇本 / 選擇模式 =====================
    public class StoryGenerateRequest
    {
        public string mode { get; set; }                 // 選擇模式
        public string region_id { get; set; }            // 地區代號
        public string region { get; set; }               // 地區名稱
        public bool use_geo { get; set; }                // 是否使用定位
        public int party_size { get; set; }              // 旅程人數
        public List<string> preferences { get; set; }    // 旅遊偏好
    }

    public class StoryWheelSpinResponse
    {
        public string region_id { get; set; }      // 地區固定代號，例如 region_tainan_anping
        public string region { get; set; }         // 前端顯示的地區名稱，例如 台南安平
        public string city_name { get; set; }      // 所屬城市名稱，例如 臺南市
        public string district_name { get; set; }  // 所屬行政區名稱，例如 安平區
    }

    public class StoryOptionResponse
    {
        public string story_id { get; set; }
        public string title { get; set; }
        public string prologue { get; set; }          // 前傳劇情
        public string category { get; set; }          // 分類: 文史/美食探險
        public string transport { get; set; }         // 預計交通工具
        public List<string> expected_badges { get; set; }
        public int expected_postcards { get; set; }
        public string region_id { get; set; }             // 地區代號
        public string region { get; set; }                // 地區名稱
        public List<string> route_preview { get; set; }  // 探索路線預覽

        public class RouteNode
        {
            public string node_id { get; set; }
            public string location_name { get; set; }
            public int node_order { get; set; }
        }
    }

    public class StoryConfirmRequest
    {
        public string story_id { get; set; }
    }

    public class StoryDetailResponse
    {
        public string story_id { get; set; }
        public string title { get; set; }
        public string subtitle { get; set; }
        public string synopsis { get; set; }
        public List<StoryOptionResponse.RouteNode> route_nodes { get; set; }
    }

    // ===================== 地圖 / 探索進度 =====================
    public class MapResponse
    {
        public string story_id { get; set; }
        public int unlocked_node_count { get; set; }
        public int total_node_count { get; set; }
        public int postcard_unlocked_count { get; set; }
        public int postcard_total_count { get; set; }
        public List<MapNode> nodes { get; set; }
        public int day_index { get; set; }         // 第一日/第二日
        public int total_days { get; set; }
    }

    public class MapNode
    {
        public string node_id { get; set; }
        public string location_name { get; set; }
        public double lat { get; set; }
        public double lng { get; set; }
        public bool is_unlocked { get; set; }
        public bool is_night_only { get; set; }
        public string fog_hint { get; set; }        // 雲霧區隱約描述
        public List<string> child_node_ids { get; set; } // 樹狀分支
    }

    public class NodeDetailResponse
    {
        public string node_id { get; set; }
        public string location_name { get; set; }
        public string npc_name { get; set; }
        public string intro_story { get; set; }        // NPC 介紹劇情
        public string opening_hours { get; set; }
        public List<string> nearby_food { get; set; }
        public string task_id { get; set; }
        public string review_story_url { get; set; }  // 回顧劇情
    }

    public class NpcInteractionResponse
    {
        public string node_id { get; set; }

        // 地點／場景
        public string location_name { get; set; }
        public string location_subtitle { get; set; }
        public string scene_image_url { get; set; }

        // NPC
        public string npc_id { get; set; }
        public string npc_name { get; set; }
        public string npc_avatar_url { get; set; }

        // 對話
        public string npc_dialogue { get; set; }
        public string emotion { get; set; } // normal / happy / hint

        // 前端按鈕與後續動作
        public string skip_button_text { get; set; }
        public string next_task_id { get; set; }       // 隨機互動模板套資訊
    }

    public class NavigationRequest
    {
        public string node_id { get; set; }
    }

    public class NavigationResponse
    {
        public string maps_deeplink_url { get; set; }   // Google Maps 導航連結
    }

    // ===================== 任務 / 答題 / 提示 =====================
    public class TaskDetailResponse
    {
        public string task_id { get; set; }
        public string node_id { get; set; }
        public string task_type { get; set; }        // 12種任務類型之一
        public string task_description { get; set; }
        public List<TaskOption> options { get; set; } // 選擇題選項(若為文化問答型)
        public bool requires_photo { get; set; }
        public bool requires_gps { get; set; }
        public bool requires_group { get; set; }      // 協作解謎型
    }

    public class TaskOption
    {
        public string option_key { get; set; }   // A/B/C/D
        public string option_text { get; set; }
    }

    public class TaskAnswerRequest
    {
        public string task_id { get; set; }
        public string selected_option_key { get; set; }
        public string photo_url { get; set; }
        public string text_answer { get; set; }      // e.g. 味覺聯想文字
        public double? lat { get; set; }
        public double? lng { get; set; }
    }

    public class TaskAnswerResponse
    {
        public bool is_correct { get; set; }
        public string feedback_message { get; set; }
        public string unlocked_postcard_id { get; set; }
        public int unlocked_node_progress { get; set; }
        public int total_node_count { get; set; }
    }

    public class TaskHintResponse
    {
        public string task_id { get; set; }
        public string npc_avatar_url { get; set; }
        public string hint_text { get; set; }
    }

    // ===================== 明信片 =====================
    public class PostcardResponse
    {
        public string postcard_id { get; set; }
        public string title { get; set; }              // e.g. 臺南孔廟-全台首學的書香
        public string subtitle { get; set; }
        public string front_image_url { get; set; }     // AI 生成正面
        public string back_photo_url { get; set; }       // 玩家拍攝背面
        public string culture_note { get; set; }
        public DateTime found_date { get; set; }
        public bool is_night_edition { get; set; }
    }

    public class PostcardPrintRequest
    {
        public string postcard_id { get; set; }
    }

    public class PostcardPrintResponse
    {
        public string ibon_pickup_code { get; set; }     // iBON 取件編號
        public string pdf_url { get; set; }
    }

    public class PostcardShareRequest
    {
        public string postcard_id { get; set; }
        public string platform { get; set; }             // IG 等
    }

    // ===================== 徽章 =====================
    public class BadgeResponse
    {
        public string badge_id { get; set; }
        public string badge_name { get; set; }
        public string badge_type { get; set; }    // 特色/景點/系列
        public string image_url { get; set; }
        public DateTime obtained_date { get; set; }
    }

    // ===================== Vlog / 劇本結束 / 回顧 =====================
    public class StoryEndingResponse
    {
        public string story_id { get; set; }
        public string title { get; set; }               // 府城儒生的失落卷
        public int walked_steps { get; set; }            // 跋涉步數
        public string task_completion_ratio { get; set; } // 破解謎題 16/16
        public string postcard_completion_ratio { get; set; } // 尋獲明信片 10/10
        public string ending_type { get; set; }           // 一般結局/隱藏結局
    }

    public class VlogGenerateRequest
    {
        public string story_id { get; set; }
    }

    public class VlogResponse
    {
        public string vlog_id { get; set; }
        public string story_id { get; set; }
        public string video_url { get; set; }
        public string thumbnail_url { get; set; }
        public DateTime completed_date { get; set; }
    }

    // ===================== 過往 (History) =====================
    public class HistoryStoryItem
    {
        public string story_id { get; set; }
        public string title { get; set; }
        public string synopsis { get; set; }
        public DateTime completed_date { get; set; }
        public string region { get; set; }
        public List<string> route_summary { get; set; }
        public string vlog_id { get; set; }
        public string postcard_review_url { get; set; }
    }

    // ===================== 收藏 =====================
    public class FavoriteItemResponse
    {
        public string favorite_id { get; set; }
        public string item_type { get; set; }   // postcard/badge/vlog
        public string ref_id { get; set; }
        public string image_url { get; set; }
        public string title { get; set; }
    }

    // ===================== 周邊好去 (任務/周邊資訊) =====================
    public class NearbyPlaceResponse
    {
        public string place_id { get; set; }
        public string category { get; set; }     // 飲食/其他
        public string name { get; set; }
        public string address { get; set; }
        public string open_time { get; set; }
        public List<string> photo_urls { get; set; }
        public string maps_deeplink_url { get; set; }
    }
}