using System;
using System.Collections.Generic;

namespace backend.Models
{
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
        public int story_id { get; set; }                  // 故事代號，對應 story.s_id
        public string title { get; set; }                   // 故事標題，對應 story_title
        public string prologue { get; set; }                // 前導故事，對應 story_prologue
        /// <summary>分類。新資料表 story 沒有這個欄位，固定為 null。</summary>
        public string category { get; set; }
        public string transport { get; set; }                // 建議交通工具，對應 sd_transport
        public List<string> expected_badges { get; set; }   // 預期會解鎖的徽章，對應 story_badge
        public int expected_postcards { get; set; }           // 預期明信片數量，對應 story_postcards
        /// <summary>地區代號。新資料庫沒有地區主表，固定為 null。</summary>
        public string region_id { get; set; }
        public string region { get; set; }                     // 地區名稱，由 city_name + district_name 組成
        public List<string> route_preview { get; set; }      // 預覽路線節點(節點標題陣列)

        public class RouteNode
        {
            public int node_id { get; set; }            // 節點代號，對應 story_node.sn_id
            public string location_name { get; set; }    // 景點名稱
            public int node_order { get; set; }            // 節點順序，對應 sn_order
        }
    }

    public class StoryConfirmRequest
    {
        public int story_id { get; set; }   // 使用者確認選擇的故事代號
    }

    // ===================== 地圖 / 路線導覽 =====================
    public class MapResponse
    {
        public int story_id { get; set; }                       // 故事代號，對應 story.s_id
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
        public int node_id { get; set; }                       // 節點代號，對應 story_node.sn_id
        public string location_name { get; set; }               // 景點名稱，取自 story_node.sn_title
        public double lat { get; set; }                          // 緯度
        public double lng { get; set; }                          // 經度
        public bool is_unlocked { get; set; }                    // 是否已解鎖，依 story_session.ss_current 計算
        public bool is_night_only { get; set; }                   // 是否為夜晚限定景點
        public string fog_hint { get; set; }                      // 迷霧探索提示，取自 story_node.sn_hint
        /// <summary>節點所屬天數。新資料表沒有 day_index 欄位，目前固定為 1。</summary>
        public int day_index { get; set; }
        public List<int> child_node_ids { get; set; }           // 包含的子節點代號列表
        public string image_url { get; set; }                    // 已解鎖景點圖片
        public string silhouette_image_url { get; set; }        // 未解鎖的顯示剪影圖片
        public int node_order { get; set; }                       // 畫面連線用的順序值，對應 sn_order
    }

    public class NodeDetailResponse
    {
        public int node_id { get; set; }                    // 節點代號，對應 story_node.sn_id
        public string location_name { get; set; }            // 景點名稱
        public string npc_name { get; set; }                  // 探索節點出現的 NPC 名稱
        public string intro_story { get; set; }               // NPC 介紹故事
        public string opening_hours { get; set; }              // 景點開放時間
        public List<string> nearby_food { get; set; }        // 附近美食推薦陣列
        public int? task_id { get; set; }                     // 探索節點對應的任務代號，對應 task.task_id
        public string review_story_url { get; set; }          // 回顧故事網址
    }

    // ===================== 故事結尾 / 回顧 =====================
    public class StoryEndingResponse
    {
        public string story_id { get; set; }                          // 故事代號
        public string title { get; set; }                               // 故事標題
        public int walked_steps { get; set; }                            // 步行步數
        public string task_completion_ratio { get; set; }                 // 任務完成比例
        public string postcard_completion_ratio { get; set; }              // 收集明信片比例
        public string ending_type { get; set; }                             // 結尾類型：完美結局/一般結局
    }

    // ===================== 歷史紀錄 (History) =====================
    public class HistoryStoryItem
    {
        /// <summary>故事代號，關聯 story.s_id</summary>
        public int story_id { get; set; }                       // 故事代號

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

        /// <summary>使用者完成獲得的 Vlog 代號，關聯 au_vlog.av_id，尚未生成則為 null</summary>
        public int? vlog_id { get; set; }                              // 關聯的 Vlog 代號

        /// <summary>明信片回顧頁面的連結，若無則為 null。</summary>
        public string postcard_review_url { get; set; }                 // 明信片回顧連結
        public List<string> spots { get; set; }
    }
}
