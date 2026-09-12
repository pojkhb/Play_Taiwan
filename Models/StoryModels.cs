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
        public string location_codename { get; set; }
    public string opening { get; set; }
    public string success { get; set; }
    public string npc_name { get; set; }
    }
    public class AgentOrchestrateRequest
{
    public string user_voice_transcript { get; set; }
    public string emotion_label { get; set; }
    public double user_lat { get; set; }
    public double user_lon { get; set; }
}

public class AgentOrchestrateResponse
{
    public Phase1Perception phase_1_perception { get; set; }
    public Phase2Cognition phase_2_cognition { get; set; }
    public Phase3GraphRag phase_3_graph_rag { get; set; }
    public Phase4ActionAndTools phase_4_action_and_tools { get; set; }
    public string phase_5_next_step { get; set; }
}

public class Phase1Perception
{
    public string voice_input { get; set; }
    public string emotion_detected { get; set; }
}

public class Phase2Cognition
{
    public string agent_thought_process { get; set; }
    public List<string> extracted_tags { get; set; }
}

public class Phase3GraphRag
{
    public RecommendedSpot recommended_spot { get; set; }
}

public class RecommendedSpot
{
    public string name { get; set; }
    public string address { get; set; }
    public string description { get; set; }
    public List<string> tags { get; set; }
    public double distance_m { get; set; }
}

public class Phase4ActionAndTools
{
    public ScriptBlueprintSimple script_blueprint { get; set; }
    public string tool_1_calendar_sync { get; set; }
    public string tool_2_social_share { get; set; }
}

public class ScriptBlueprintSimple
{
    public string theme_title { get; set; }
    public string npc_dialogue { get; set; }
    public string task_mission { get; set; }
    public List<string> preparation_tips { get; set; }
}
}