using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace backend.Models
{
    // ==========================================
    // 🎯 接收 Vlog API 回傳的資料結構 (AiStoryResult)
    // ==========================================
    public class AiStoryResult
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("data")]
        public VlogBlueprintData Data { get; set; }
    }

    public class VlogBlueprintData
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("preface")]
        public string Preface { get; set; }

        [JsonPropertyName("synopsis")]
        public string Synopsis { get; set; }

        [JsonPropertyName("is_night_mode")]
        public bool IsNightMode { get; set; }

        [JsonPropertyName("npc")]
        public VlogNpc Npc { get; set; }

        [JsonPropertyName("nodes")]
        public List<VlogNode> Nodes { get; set; }
    }

    public class VlogNpc
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("role")]
        public string Role { get; set; }

        [JsonPropertyName("intro")]
        public string Intro { get; set; }
    }

    public class VlogNode
    {
        [JsonPropertyName("node_order")]
        public int NodeOrder { get; set; }

        [JsonPropertyName("place_name")]
        public string PlaceName { get; set; }

        [JsonPropertyName("task_type")]
        public string TaskType { get; set; }

        [JsonPropertyName("task_description")]
        public string TaskDescription { get; set; }

        [JsonPropertyName("dialogues")]
        public VlogDialogues Dialogues { get; set; }
    }

    public class VlogDialogues
    {
        [JsonPropertyName("opening")]
        public string Opening { get; set; }

        [JsonPropertyName("success")]
        public string Success { get; set; }
    }

    // ==========================================
    // 🎯 回傳給前端畫 UI 的 ViewModel (對應你的 StoryDetailResponse)
    // ==========================================
    public class StoryDetailResponse
    {
       public string story_id { get; set; }
        public string title { get; set; }
        public string preface { get; set; }   // 畫面 2：前傳
        public string synopsis { get; set; }  // 畫面 1：內文簡介
        public NpcDetail npc { get; set; }    // 畫面 3：NPC 資訊
        public List<NodeDetail> nodes { get; set; } // 畫面 2、4：探索總覽與地圖節點
        
        // 💡 補回原本 DAO 需要的欄位，解決 CS0117 與 CS1061 錯誤
        public string subtitle { get; set; }  
        public List<StoryOptionResponse.RouteNode> route_nodes { get; set; }
    }

    public class NpcDetail
    {
        public string name { get; set; }
        public string role { get; set; }
    }

    public class NodeDetail
    {
        public int order { get; set; }
        public string place_name { get; set; }
        public string task_description { get; set; }
        public string opening_dialogue { get; set; } // 已去名字的乾淨對話
    }
}