// 檔案路徑：System\ViewModels\Story\StoryGenerateByLocationViewModels.cs
using System.Collections.Generic;

namespace backend.ViewModels
{
    /// <summary>依 GPS 座標生成劇本的請求。</summary>
    public class StoryGenerateByLocationRequest
    {
        public double lat { get; set; }
        public double lng { get; set; }
        public int traveler_count { get; set; } = 2;
        public List<string> preferences { get; set; }
        public List<string> transportation { get; set; }
        public int node_count { get; set; } = 4;
        public bool is_night { get; set; } = false;

        /// <summary>要一次生成幾份劇本，預設 1。與 GenerateAi 的 story_count 功能相同。</summary>
        public int story_count { get; set; } = 1;
    }

    /// <summary>
    /// 依玩家 GPS 座標查詢附近地點（依實際距離排序），回傳的單筆結果。
    /// 命名刻意避開 MapController 既有的 NearbyPlaceResponse（欄位不同、用途不同：周邊好去 vs 距離排序）。
    /// </summary>
    public class NearbyPlaceDistanceResponse
    {
        /// <summary>地點代號</summary>
        public int place_id { get; set; }

        /// <summary>地點名稱</summary>
        public string place_name { get; set; }

        /// <summary>地點代號（劇情中對這個地點的暗號/別稱）</summary>
        public string location_codename { get; set; }

        /// <summary>與使用者的直線距離（公里）</summary>
        public double distance_km { get; set; }
    }

    #region 完整還原外部 AI 服務回傳的劇本藍圖結構（欄位一個都不能少）

    public class ScriptBlueprintApiResponse
    {
        public string status { get; set; }
        public ScriptBlueprintData data { get; set; }
    }

    /// <summary>劇本完整內容（與 AI 原始生成格式相同）</summary>
    public class ScriptBlueprintData
    {
        /// <summary>劇本標題</summary>
        public string title { get; set; }

        /// <summary>前傳</summary>
        public string preface { get; set; }

        /// <summary>內文簡介</summary>
        public string synopsis { get; set; }

        /// <summary>是否為夜間模式劇本</summary>
        public bool is_night_mode { get; set; }

        /// <summary>劇本的 NPC</summary>
        public ScriptBlueprintNpc npc { get; set; }

        /// <summary>劇本節點（景點）清單，依順序排列</summary>
        public List<ScriptBlueprintNode> nodes { get; set; }
    }

    /// <summary>劇本 NPC</summary>
    public class ScriptBlueprintNpc
    {
        /// <summary>NPC 名稱</summary>
        public string name { get; set; }

        /// <summary>NPC 身分/角色設定</summary>
        public string role { get; set; }

        /// <summary>NPC 自我介紹</summary>
        public string intro { get; set; }
    }

    /// <summary>劇本中的一個節點（景點）</summary>
    public class ScriptBlueprintNode
    {
        /// <summary>節點順序，從 1 開始</summary>
        public int node_order { get; set; }

        /// <summary>景點名稱</summary>
        public string place_name { get; set; }

        /// <summary>地點代號（劇情中對這個地點的暗號/別稱）</summary>
        public string location_codename { get; set; }

        /// <summary>節點標題</summary>
        public string node_title { get; set; }

        /// <summary>任務類型，例如拍照、問答</summary>
        public string task_type { get; set; }

        /// <summary>任務說明</summary>
        public string task_description { get; set; }

        /// <summary>NPC 對話</summary>
        public ScriptBlueprintDialogues dialogues { get; set; }
    }

    /// <summary>節點的 NPC 對話</summary>
    public class ScriptBlueprintDialogues
    {
        /// <summary>抵達時的開場對話</summary>
        public string opening { get; set; }

        /// <summary>完成任務後的成功對話</summary>
        public string success { get; set; }
    }

    #endregion
}
