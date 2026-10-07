using System;

namespace backend.Models
{
    /// <summary>節點的 NPC 互動內容</summary>
    public class NpcInteractionResponse
    {
        /// <summary>節點代號（story_node.sn_id）</summary>
        public int node_id { get; set; }

        // 景點資訊
        /// <summary>景點名稱</summary>
        public string location_name { get; set; }

        /// <summary>景點副標題</summary>
        public string location_subtitle { get; set; }

        /// <summary>情境背景圖片網址</summary>
        public string scene_image_url { get; set; }

        // NPC
        /// <summary>NPC 代號（npc.npc_id）；劇本沒有指定 NPC 時是預設的薯光</summary>
        public string npc_id { get; set; }

        /// <summary>NPC 名稱，例如「墨先生」</summary>
        public string npc_name { get; set; }

        /// <summary>NPC 身分／角色設定，例如「博學嚴謹的文史工作者，擅長解讀古地圖與歷史檔案」</summary>
        public string npc_role { get; set; }

        /// <summary>NPC 圖片（完整網址）</summary>
        public string npc_avatar_url { get; set; }

        /// <summary>NPC 語音的聲線，把台詞轉成語音時傳給 POST /api/Npc/Speak 的 voice</summary>
        public string npc_voice { get; set; }

        // 對話
        /// <summary>NPC 對話內容</summary>
        public string npc_dialogue { get; set; }

        /// <summary>NPC 表情：normal（一般）/ happy（開心）/ hint（提示）</summary>
        public string emotion { get; set; }

        // 畫面按鈕文字與動作
        /// <summary>略過按鈕上顯示的文字</summary>
        public string skip_button_text { get; set; }

        /// <summary>按下按鈕後要前往的任務代號</summary>
        public string next_task_id { get; set; }
    }

    public class LocationRequest
    {
        public double lat { get; set; }
        public double lng { get; set; }
        public double? accuracy { get; set; }
    }


    /// <summary>定位回報結果</summary>
    public class LocationResponse
    {
        /// <summary>緯度（原樣帶回）</summary>
        public double lat { get; set; }

        /// <summary>經度（原樣帶回）</summary>
        public double lng { get; set; }

        /// <summary>定位精準度（公尺，原樣帶回）</summary>
        public double? accuracy { get; set; }

        /// <summary>座標所在的縣市，例如「臺中市」</summary>
        public string city_name { get; set; }

        /// <summary>座標所在的鄉鎮市區，例如「西區」</summary>
        public string district_name { get; set; }

        /// <summary>後端收到定位的時間（UTC）</summary>
        public DateTime received_at { get; set; }
    }
}
