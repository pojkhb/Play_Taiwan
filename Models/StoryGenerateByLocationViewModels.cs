// 檔案路徑：System\ViewModels\StoryGenerateByLocationViewModels.cs
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
        public string place_id { get; set; }
        public string place_name { get; set; }
        public string location_codename { get; set; }
        public double distance_km { get; set; }
    }

    #region 完整還原外部 AI 服務回傳的劇本藍圖結構（欄位一個都不能少）

    public class ScriptBlueprintApiResponse
    {
        public string status { get; set; }
        public ScriptBlueprintData data { get; set; }
    }

    public class ScriptBlueprintData
    {
        public string title { get; set; }
        public string preface { get; set; }
        public string synopsis { get; set; }
        public bool is_night_mode { get; set; }
        public ScriptBlueprintNpc npc { get; set; }
        public List<ScriptBlueprintNode> nodes { get; set; }
    }

    public class ScriptBlueprintNpc
    {
        public string name { get; set; }
        public string role { get; set; }
        public string intro { get; set; }
    }

    public class ScriptBlueprintNode
    {
        public int node_order { get; set; }
        public string place_name { get; set; }
        public string location_codename { get; set; }
        public string node_title { get; set; }
        public string task_type { get; set; }
        public string task_description { get; set; }
        public ScriptBlueprintDialogues dialogues { get; set; }
    }

    public class ScriptBlueprintDialogues
    {
        public string opening { get; set; }
        public string success { get; set; }
    }

    #endregion
}