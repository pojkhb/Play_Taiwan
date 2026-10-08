using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using backend.ViewModels;

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

        // 景點在 Neo4j 的全域唯一識別碼，寫入 md_story_node.place_id 供任務生成 (TaskGenerationService) 使用
        [JsonPropertyName("spot_uuid")]
        public string SpotUuid { get; set; }

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
    /// <summary>劇本詳細內容（觀看更多、確認選卷用）</summary>
    public class StoryDetailResponse
    {
        /// <summary>劇本代號（story.s_id）</summary>
        public int story_id { get; set; }

        /// <summary>劇本標題</summary>
        public string title { get; set; }

        /// <summary>前傳（畫面 2）</summary>
        public string preface { get; set; }

        /// <summary>內文簡介（畫面 1）</summary>
        public string synopsis { get; set; }

        /// <summary>是否為夜間劇本（夜間劇本完成後才能抽「午夜台灣」系列勳章）</summary>
        public bool is_night_mode { get; set; }

        /// <summary>是否為使用者喜愛的劇本（用 POST /api/Story/{story_id}/Favorite 設定）</summary>
        public bool is_favorite { get; set; }

        /// <summary>NPC 資訊（畫面 3）；劇本沒有指定 NPC 時是預設的薯光</summary>
        public NpcDetail npc { get; set; }

        /// <summary>探索總覽與地圖節點（畫面 2、4）</summary>
        public List<NodeDetail> nodes { get; set; }

        /// <summary>副標題（目前為空字串）</summary>
        public string subtitle { get; set; }

        /// <summary>路線節點清單（節點代號、景點名稱、順序）</summary>
        public List<StoryOptionResponse.RouteNode> route_nodes { get; set; }
    }

    /// <summary>劇本中的 NPC</summary>
    public class NpcDetail
    {
        /// <summary>NPC 名稱</summary>
        public string name { get; set; }

        /// <summary>NPC 身分/角色設定</summary>
        public string role { get; set; }

        /// <summary>NPC 自我介紹</summary>
        public string intro { get; set; }

        /// <summary>NPC 圖片（完整網址）</summary>
        public string avatar_url { get; set; }

        /// <summary>NPC 語音的聲線，把台詞轉成語音時傳給 POST /api/Npc/Speak 的 voice</summary>
        public string voice { get; set; }
    }

    /// <summary>劇本中的一個節點</summary>
    public class NodeDetail
    {
        /// <summary>節點順序，從 1 開始</summary>
        public int order { get; set; }

        /// <summary>景點名稱</summary>
        public string place_name { get; set; }

        /// <summary>任務說明</summary>
        public string task_description { get; set; }

        /// <summary>開場對話（已去掉說話者名字）</summary>
        public string opening_dialogue { get; set; }

        /// <summary>地點代號（劇情中對這個地點的暗號/別稱）</summary>
        public string location_codename { get; set; }

        /// <summary>抵達時的開場對話</summary>
        public string opening { get; set; }

        /// <summary>完成任務後的成功對話</summary>
        public string success { get; set; }

        /// <summary>此節點 NPC 名稱</summary>
        public string npc_name { get; set; }

        /// <summary>此節點的任務類型，例如「文化問答型」；沒有設定時為空字串</summary>
        public string task_type { get; set; }

        /// <summary>景點照片（完整網址），劇本檔案館與地圖解鎖後用同一張；景點沒有照片時為 null</summary>
        public string image_url { get; set; }
    }
    public class AgentOrchestrateRequest
{
    public string user_voice_transcript { get; set; }
    public string emotion_label { get; set; }
    public double user_lat { get; set; }
    public double user_lon { get; set; }
}

/// <summary>AI Agent 即時推薦結果（依 5 個階段分組）</summary>
public class AgentOrchestrateResponse
{
    /// <summary>階段 1：感知（使用者說了什麼、情緒）</summary>
    public Phase1Perception phase_1_perception { get; set; }

    /// <summary>階段 2：理解（AI 的思考過程與擷取出的標籤）</summary>
    public Phase2Cognition phase_2_cognition { get; set; }

    /// <summary>階段 3：知識圖譜查詢（推薦的景點）</summary>
    public Phase3GraphRag phase_3_graph_rag { get; set; }

    /// <summary>階段 4：行動與工具（任務腳本、行事曆連結、分享連結）</summary>
    public Phase4ActionAndTools phase_4_action_and_tools { get; set; }

    /// <summary>階段 5：建議使用者的下一步</summary>
    public string phase_5_next_step { get; set; }
}

/// <summary>感知階段</summary>
public class Phase1Perception
{
    /// <summary>使用者輸入的語音/文字內容</summary>
    public string voice_input { get; set; }

    /// <summary>判斷出的情緒，例如「疲憊」</summary>
    public string emotion_detected { get; set; }
}

/// <summary>理解階段</summary>
public class Phase2Cognition
{
    /// <summary>AI 的思考過程說明</summary>
    public string agent_thought_process { get; set; }

    /// <summary>從輸入內容擷取出的需求標籤，例如 ["冷氣", "休息"]</summary>
    public List<string> extracted_tags { get; set; }
}

/// <summary>知識圖譜查詢階段</summary>
public class Phase3GraphRag
{
    /// <summary>推薦的景點</summary>
    public RecommendedSpot recommended_spot { get; set; }
}

/// <summary>推薦景點</summary>
public class RecommendedSpot
{
    /// <summary>景點名稱</summary>
    public string name { get; set; }

    /// <summary>地址</summary>
    public string address { get; set; }

    /// <summary>景點介紹</summary>
    public string description { get; set; }

    /// <summary>景點標籤</summary>
    public List<string> tags { get; set; }

    /// <summary>與使用者的直線距離（公尺）</summary>
    public double distance_m { get; set; }
}

/// <summary>行動與工具階段</summary>
public class Phase4ActionAndTools
{
    /// <summary>單一任務腳本</summary>
    public ScriptBlueprintSimple script_blueprint { get; set; }

    /// <summary>加入行事曆的連結</summary>
    public string tool_1_calendar_sync { get; set; }

    /// <summary>分享到社群的連結</summary>
    public string tool_2_social_share { get; set; }
}

/// <summary>單一任務腳本（AI 直接用 LLM 的輸出，沒有驗證型態，文字欄位可能被拆成陣列）</summary>
public class ScriptBlueprintSimple
{
    /// <summary>主題標題</summary>
    [JsonConverter(typeof(LooseStringJsonConverter))]
    public string theme_title { get; set; }

    /// <summary>NPC 對話（AI 偶爾回傳多句的陣列，會合併成一段，每句一行）</summary>
    [JsonConverter(typeof(LooseStringJsonConverter))]
    public string npc_dialogue { get; set; }

    /// <summary>任務內容</summary>
    [JsonConverter(typeof(LooseStringJsonConverter))]
    public string task_mission { get; set; }

    /// <summary>出發前準備建議</summary>
    public List<string> preparation_tips { get; set; }
}
public class GenerateScriptBlueprintByTextResponse
{
    /// <summary>processing（生成中）、error（失敗）；完成時 data 有值</summary>
    public string status { get; set; }

    /// <summary>AI 背景工作代號，送出後用它查進度</summary>
    public string job_id { get; set; }

    /// <summary>目前進行到的階段，例如「查詢 Neo4j 地點」</summary>
    public string stage { get; set; }

    /// <summary>status = error 時的原因</summary>
    public string error { get; set; }

    public ParsedIntent parsed_intent { get; set; }
    public ScriptBlueprintData data { get; set; }
}

/// <summary>AI 從一句話解析出的旅遊條件</summary>
public class ParsedIntent
{
    /// <summary>城市，例如「臺南市」</summary>
    public string city_name { get; set; }

    /// <summary>行政區，例如「安平區」</summary>
    public string town_name { get; set; }

    /// <summary>旅遊人數</summary>
    public int traveler_count { get; set; }

    /// <summary>旅遊偏好，例如 ["解謎深入", "文學建築"]</summary>
    public List<string> preferences { get; set; }

    /// <summary>交通方式，例如 ["步行"]</summary>
    public List<string> transportation { get; set; }

    /// <summary>景點（節點）數量</summary>
    public int node_count { get; set; }

    /// <summary>是否為夜間行程</summary>
    public bool is_night { get; set; }
}
public class ConfirmStoryRequest
{
    public int story_id { get; set; }
}

    // ==========================================
    // 🎯 StoryController 回傳給前端的結果（原本是匿名物件，改成具名類別讓 Swagger 能顯示欄位說明）
    // ==========================================

    /// <summary>Agent 即時推薦（spin）結果</summary>
    public class SpinScriptResult
    {
        /// <summary>這次推薦存入資料庫的代號</summary>
        public string recommendation_id { get; set; }

        /// <summary>AI Agent 推薦內容</summary>
        public AgentOrchestrateResponse agent_result { get; set; }
    }

    /// <summary>自然語言生成劇本（遊你說了算）結果</summary>
    public class GenerateByTextResult
    {
        /// <summary>劇本代號（story.s_id）</summary>
        public int story_id { get; set; }

        /// <summary>AI 解析出的城市</summary>
        public string detected_city { get; set; }

        /// <summary>AI 解析出的行政區</summary>
        public string detected_town { get; set; }

        /// <summary>AI 從這句話解析出的完整旅遊條件</summary>
        public ParsedIntent parsed_intent { get; set; }

        /// <summary>劇本完整內容</summary>
        public ScriptBlueprintData data { get; set; }
    }

    /// <summary>目前進行中的劇本</summary>
    public class CurrentPlayingStory
    {
        /// <summary>劇本代號（story.s_id）</summary>
        public int story_id { get; set; }

        /// <summary>劇本標題</summary>
        public string title { get; set; }

        /// <summary>目前進行到第幾個節點</summary>
        public int current_order { get; set; }
    }
}
