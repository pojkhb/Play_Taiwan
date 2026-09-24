// 檔案路徑：System\ViewModels\Story\GameStoryViewModels.cs
// 劇本 + 任務一次生成（AI service /api/v1/generate），一次生成多份劇本讓使用者挑
using System.Collections.Generic;

namespace backend.ViewModels
{
    #region 前端 → 後端

    /// <summary>劇本任務生成的查詢條件</summary>
    public class GameStoryGenerateRequest
    {
        /// <summary>中心點緯度（使用者當前定位）。沒有定位時可改傳 city_name/town_name</summary>
        public double lat { get; set; }

        /// <summary>中心點經度</summary>
        public double lng { get; set; }

        /// <summary>城市，例如「臺中市」。有帶 lat/lng 時可不填，後端會自動判斷</summary>
        public string city_name { get; set; }

        /// <summary>行政區，例如「西區」。有帶 lat/lng 時可不填，後端會自動判斷</summary>
        public string town_name { get; set; }

        /// <summary>隊伍人數，不帶時預設 2。1 人時不會出協作解謎型任務</summary>
        public int party_size { get; set; }

        /// <summary>
        /// 交通方式（可複選），例如 ["步行", "公車"]。
        /// 挑景點的交通圈只算步行/腳踏車/機車/汽車；選了「公車」、「客運」或「台灣好行」時，
        /// 會另外找出景點之間可直達的公車路線與站牌
        /// </summary>
        public List<string> transportation { get; set; }

        /// <summary>偏好標籤，例如 ["好山好水", "美食"]</summary>
        public List<string> preferences { get; set; }

        /// <summary>是否為夜間劇本：1 = 是、2 = 否，不帶時預設 2</summary>
        public int is_night_mode { get; set; }
    }

    #endregion


    #region 後端 → AI service（/api/v1/generate 的 request）

    public class AiGameStoryRequest
    {
        public string city_name { get; set; }
        public string district_name { get; set; }
        public int party_size { get; set; }
        public List<string> s_tag { get; set; }
        public int is_night_mode { get; set; }

        /// <summary>各份劇本的生成條件，每份各自一組景點與敘事語氣</summary>
        public List<AiStoryCondition> stories { get; set; }
    }

    public class AiStoryCondition
    {
        /// <summary>劇本編號，從 1 開始，AI 回傳時原樣帶回</summary>
        public int story_no { get; set; }

        /// <summary>敘事語氣（narrative_tone.nt_name）</summary>
        public string nt_name { get; set; }

        /// <summary>景點清單，已由後端排好順路的參觀順序，陣列順序即節點順序</summary>
        public List<AiGamePlace> places { get; set; }
    }

    /// <summary>傳給 AI 的景點。只傳會影響劇本的欄位，挑景點、排順序、交通都由後端處理</summary>
    public class AiGamePlace
    {
        public string place_id { get; set; }
        public string p_name { get; set; }
        public int is_hotel { get; set; }
        public int is_hidden { get; set; }
        public List<AiTaskTypeRef> type_list { get; set; }
    }

    public class AiTaskTypeRef
    {
        public int type_id { get; set; }
        public string type_name { get; set; }
    }

    /// <summary>公車路線簡要資訊</summary>
    public class BusRouteBrief
    {
        /// <summary>路線代號（bus_route.br_id），查路線詳情用</summary>
        public int br_id { get; set; }

        /// <summary>路線名稱，例如「300」</summary>
        public string route_name { get; set; }

        /// <summary>路線類型：1 = 市區公車、2 = 公路客運、3 = 台灣好行</summary>
        public int route_type { get; set; }

        /// <summary>路線類型名稱：市區公車 / 公路客運 / 台灣好行</summary>
        public string route_type_name { get; set; }
    }

    #endregion


    #region AI service → 後端（/api/v1/generate 的 response）

    public class AiGameStoryResponse
    {
        public List<AiGeneratedStory> stories { get; set; }
    }

    public class AiGeneratedStory
    {
        /// <summary>對應 request 的 stories[].story_no</summary>
        public int story_no { get; set; }
        public GameStoryInfo story { get; set; }
        public List<GameStoryNode> nodes { get; set; }
    }

    #endregion


    #region 後端回傳給前端（AI 回傳的結構，後端再補上 story_id、sn_id、task_db_id 與條件欄位）

    /// <summary>一份生成好的劇本</summary>
    public class GameStoryResult
    {
        /// <summary>寫入資料庫後的劇本代號（story.s_id），之後查詳情、確認選卷都用這個</summary>
        public int story_id { get; set; }

        /// <summary>這次生成的第幾份劇本（1、2、3）</summary>
        public int story_no { get; set; }

        /// <summary>這份劇本的敘事語氣，例如「懸疑推理」</summary>
        public string nt_name { get; set; }

        /// <summary>劇本基本資料</summary>
        public GameStoryInfo story { get; set; }

        /// <summary>劇本節點（景點）清單，依 sn_order 排序</summary>
        public List<GameStoryNode> nodes { get; set; }
    }

    /// <summary>劇本基本資料</summary>
    public class GameStoryInfo
    {
        /// <summary>城市名稱</summary>
        public string city_name { get; set; }

        /// <summary>行政區名稱</summary>
        public string district_name { get; set; }

        /// <summary>隊伍人數</summary>
        public int party_size { get; set; }

        /// <summary>交通方式</summary>
        public List<string> sd_transport { get; set; }

        /// <summary>劇本標題</summary>
        public string story_title { get; set; }

        /// <summary>劇本前導文字（前傳）</summary>
        public string story_prologue { get; set; }

        /// <summary>劇本簡介</summary>
        public string story_synopsis { get; set; }

        /// <summary>預期可獲得的勳章清單</summary>
        public List<string> story_badge { get; set; }

        /// <summary>預期可獲得的明信片數量（等於節點數）</summary>
        public int story_postcards { get; set; }

        /// <summary>是否為夜間劇本：1 = 是、2 = 否</summary>
        public int is_night_mode { get; set; }
    }

    /// <summary>劇本節點（景點）</summary>
    public class GameStoryNode
    {
        /// <summary>寫入資料庫後的節點代號（story_node.sn_id）</summary>
        public int sn_id { get; set; }

        /// <summary>景點唯一代號（Neo4j uuid）</summary>
        public string place_id { get; set; }

        /// <summary>景點真實名稱</summary>
        public string p_name { get; set; }

        /// <summary>此節點的 NPC 代號，沒有時為 null</summary>
        public int? npc_id { get; set; }

        /// <summary>節點順序，從 1 開始</summary>
        public int sn_order { get; set; }

        /// <summary>節點標題（劇情化）</summary>
        public string sn_title { get; set; }

        /// <summary>地點代號（劇情中對這個地點的暗號/別稱）</summary>
        public string location_codename { get; set; }

        /// <summary>此節點包含的任務類型名稱，多個以半形逗號分隔</summary>
        public string sn_task_type { get; set; }

        /// <summary>是否為隱藏節點：1 = 是、2 = 否</summary>
        public int is_hidden { get; set; }

        /// <summary>是否僅限夜間解鎖：1 = 夜間、0 = 白天</summary>
        public int is_night_only { get; set; }

        /// <summary>抵達時的開場劇情文字</summary>
        public string sn_opening_text { get; set; }

        /// <summary>完成任務後的劇情文字</summary>
        public string sn_success_text { get; set; }

        /// <summary>此節點的任務清單</summary>
        public List<GameStoryTask> tasks { get; set; }

        /// <summary>從上一個節點搭公車到這個節點的方式（有轉乘時多段）；第一個節點、沒選公車或找不到直達公車時為空陣列</summary>
        public List<BusTransitLeg> transit { get; set; }
    }

    /// <summary>節點任務</summary>
    public class GameStoryTask
    {
        /// <summary>寫入資料庫後的任務代號（task.task_id），作答時用這個</summary>
        public int task_db_id { get; set; }

        /// <summary>任務類型代號（type.type_id），例如 6 = 文化問答型</summary>
        public int task_type { get; set; }

        /// <summary>任務類型名稱，例如「文化問答型」</summary>
        public string type_name { get; set; }

        /// <summary>任務題目/情境描述</summary>
        public string task_describe { get; set; }

        /// <summary>任務提示</summary>
        public string task_hint { get; set; }

        /// <summary>正確答案，只有協作解謎型有值（不要直接顯示給玩家），其他類型為 null</summary>
        public string correct_answer { get; set; }

        /// <summary>選項，只有選擇題型（文化問答型、景點猜猜樂）才有</summary>
        public List<GameStoryTaskOption> task_option { get; set; }

        /// <summary>分段線索，只有協作解謎型才有，每位玩家依座位編號看到不同線索</summary>
        public List<GameStoryTaskClue> task_clue { get; set; }
    }

    /// <summary>選擇題選項</summary>
    public class GameStoryTaskOption
    {
        /// <summary>選項代號，例如 A/B/C/D</summary>
        public string option_key { get; set; }

        /// <summary>選項文字</summary>
        public string option_context { get; set; }

        /// <summary>是否為正確答案：1 = 正確、0 = 錯誤</summary>
        public int is_correct { get; set; }
    }

    /// <summary>協作解謎線索</summary>
    public class GameStoryTaskClue
    {
        /// <summary>座位編號，對應隊伍中第幾位玩家（1 ~ 人數）</summary>
        public int seat_no { get; set; }

        /// <summary>這個座位的玩家看到的線索</summary>
        public string clue_text { get; set; }
    }

    #endregion


    #region 公車交通方案

    /// <summary>一段公車搭乘</summary>
    public class BusTransitLeg
    {
        /// <summary>出發節點代號（story_node.sn_id）</summary>
        public int from_sn_id { get; set; }

        /// <summary>抵達節點代號（story_node.sn_id）</summary>
        public int to_sn_id { get; set; }

        /// <summary>第幾段搭乘（要轉乘時 1、2、3…）</summary>
        public int leg_order { get; set; }

        /// <summary>路線名稱，例如「300」</summary>
        public string route_name { get; set; }

        /// <summary>子路線名稱，例如「300 區間車」</summary>
        public string sub_route_name { get; set; }

        /// <summary>路線類型：1 = 市區公車、2 = 公路客運、3 = 台灣好行</summary>
        public int route_type { get; set; }

        /// <summary>路線類型名稱：市區公車 / 公路客運 / 台灣好行</summary>
        public string route_type_name { get; set; }

        /// <summary>台灣好行路線主題，例如「山林湖光」，一般公車為 null</summary>
        public string tripper_theme { get; set; }

        /// <summary>車頭往向，例如「往 臺中車站」</summary>
        public string headsign { get; set; }

        /// <summary>上車站牌名稱</summary>
        public string board_stop_name { get; set; }

        /// <summary>下車站牌名稱</summary>
        public string alight_stop_name { get; set; }

        /// <summary>搭乘站數</summary>
        public int stop_count { get; set; }

        /// <summary>預估乘車時間（分鐘，不含等車，粗估每站 2 分鐘）</summary>
        public double? est_ride_minutes { get; set; }

        /// <summary>從上車站到下車站依序經過的所有站牌（含上下車站）</summary>
        public List<BusTransitStop> stops { get; set; }

        /// <summary>公車實際行駛路線 [經度, 緯度]（沒有線形資料時退回依序連接站牌）</summary>
        public List<double[]> coordinates { get; set; }
    }

    /// <summary>搭乘途中經過的站牌</summary>
    public class BusTransitStop
    {
        /// <summary>站序</summary>
        public int stop_sequence { get; set; }

        /// <summary>站牌名稱</summary>
        public string stop_name { get; set; }

        /// <summary>站牌緯度</summary>
        public double lat { get; set; }

        /// <summary>站牌經度</summary>
        public double lng { get; set; }
    }

    #endregion
}
