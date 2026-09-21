using System;

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
