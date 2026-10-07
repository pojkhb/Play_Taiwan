// 檔案路徑：System\ViewModels\Story\GameStoryViewModels.cs
// 劇本 + 任務一次生成（AI service /api/stories/tasks/batch），一次生成多份劇本讓使用者挑
using System.Collections.Generic;
using System.Text.Json.Serialization;

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

        /// <summary>是否為夜間劇本：1 = 夜間、0 = 白天，不帶時預設 0</summary>
        public int is_night_mode { get; set; }
    }

    /// <summary>
    /// 劇本生成條件（GenerateGameStory 使用，後端內部使用）。
    /// 中心點經緯度、城市/行政區由 Controller 補齊後傳入 StoryService.GenerateStoriesAsync。
    /// </summary>
    public class GameStoryPlan
    {
        /// <summary>行程規劃的中心點緯度</summary>
        public double lat { get; set; }

        /// <summary>行程規劃的中心點經度</summary>
        public double lng { get; set; }

        /// <summary>城市，例如「臺中市」</summary>
        public string city_name { get; set; }

        /// <summary>行政區，例如「西區」</summary>
        public string town_name { get; set; }

        /// <summary>隊伍人數</summary>
        public int party_size { get; set; }

        /// <summary>交通方式（可複選）</summary>
        public List<string> transportation { get; set; }

        /// <summary>偏好標籤</summary>
        public List<string> preferences { get; set; }

        /// <summary>是否為夜間劇本：1 = 夜間、0 = 白天</summary>
        public int is_night_mode { get; set; }

        /// <summary>一次生成幾份劇本讓使用者挑</summary>
        public int story_count { get; set; }

        /// <summary>每份劇本幾個景點（節點）</summary>
        public int places_per_story { get; set; }
    }

    #endregion


    #region 後端 → AI service（/api/stories/tasks/batch 的 request，AI 規格 StoryTaskRequest）

    public class AiGameStoryRequest
    {
        public string city_name { get; set; }
        public string district_name { get; set; }
        public int party_size { get; set; }
        public List<string> s_tag { get; set; }

        /// <summary>1 = 夜間劇本、2 = 白天劇本（AI 文件定義，沒有 0）</summary>
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


    #region AI service → 後端（/api/stories/tasks/batch 的 response，AI 規格 StoryTaskResponse）

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


    #region 後端回傳給前端（AI 回傳的結構，後端再補上 story_id、sn_id、task_id 與條件欄位）
    // 答案類欄位（correct_answer、task_hint、task_clue、選項的 is_correct）會讀取 AI 回傳並存進資料庫，
    // 但回傳給前端前會清成 null 並設定為 null 時不輸出，避免前端拿到答案、提示與隊友的線索。

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

        /// <summary>
        /// 完成後可抽的勳章類別，例如 ["島嶼城市", "台灣印記", "午夜台灣"]，劇本卡片「預計獲得」顯示用。
        /// 完成劇本後呼叫 POST /api/Badge/Draw 從這些類別抽一枚
        /// </summary>
        public List<string> story_badge { get; set; }

        /// <summary>預期可獲得的明信片數量（等於節點數）</summary>
        public int story_postcards { get; set; }

        /// <summary>是否為夜間劇本：1 = 夜間、0 = 白天</summary>
        public int is_night_mode { get; set; }

        /// <summary>
        /// 劇本的 NPC，每一站都由這個 NPC 說開場白與成功台詞。
        /// AI 從 NPC 名單（npc 表）挑一位，回傳 story.npc.npc_name 即可；沒挑或名字不在名單上時用預設的薯光。
        /// 其他欄位由後端從 npc 表帶出
        /// </summary>
        public GameStoryNpc npc { get; set; }
    }

    /// <summary>劇本 NPC（名單見 npc 表）</summary>
    public class GameStoryNpc
    {
        /// <summary>NPC 代號（npc.npc_id）</summary>
        public int? npc_id { get; set; }

        /// <summary>NPC 名稱，例如「墨先生」；AI 只要回傳這個欄位</summary>
        public string npc_name { get; set; }

        /// <summary>NPC 身分／角色設定</summary>
        public string npc_role { get; set; }

        /// <summary>NPC 自我介紹（劇本開始時對玩家說的話）</summary>
        public string npc_intro { get; set; }

        /// <summary>NPC 圖片（完整網址）</summary>
        public string npc_avatar_url { get; set; }

        /// <summary>NPC 語音的聲線，把台詞轉成語音時傳給 POST /api/Npc/Speak 的 voice</summary>
        public string npc_voice { get; set; }
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

        /// <summary>此節點的 NPC 代號（npc.npc_id，同一份劇本每一站都是同一個 NPC）</summary>
        public int? npc_id { get; set; }

        /// <summary>節點順序，從 1 開始</summary>
        public int sn_order { get; set; }

        /// <summary>節點標題（劇情化）</summary>
        public string sn_title { get; set; }

        /// <summary>地點代號（劇情中對這個地點的暗號/別稱）</summary>
        public string location_codename { get; set; }

        /// <summary>景點照片（完整網址），劇本檔案館的車票顯示用；景點沒有照片時為 null</summary>
        public string image_url { get; set; }

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
        /// <summary>寫入資料庫後的任務代號（task.task_id），進入節點遊玩畫面、作答時用這個</summary>
        public int task_id { get; set; }

        /// <summary>任務類型代號（type.type_id），例如 6 = 文化問答型</summary>
        public int task_type { get; set; }

        /// <summary>任務類型名稱，例如「文化問答型」</summary>
        public string type_name { get; set; }

        /// <summary>
        /// 商家題庫題目代號（store_question.question_id），只有商家知識問答（9）才有值。
        /// 題目與選項由商家題庫帶出、不經 AI，寫入 task.question_id。
        /// </summary>
        public int? question_id { get; set; }

        /// <summary>任務題目/情境描述</summary>
        public string task_describe { get; set; }

        /// <summary>任務提示（存進資料庫；回傳前端時不輸出，答錯後改用提示 API 取得）</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string task_hint { get; set; }

        /// <summary>正確答案，只有協作解謎型有值（存進資料庫；回傳前端時不輸出）</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string correct_answer { get; set; }

        /// <summary>選項，只有選擇題型（文化問答型、景點猜猜樂、商家知識問答）才有</summary>
        public List<GameStoryTaskOption> task_option { get; set; }

        /// <summary>分段線索，只有協作解謎型才有（存進資料庫；回傳前端時不輸出，自己座位的線索由節點遊玩畫面取得）</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<GameStoryTaskClue> task_clue { get; set; }
    }

    /// <summary>選擇題選項</summary>
    public class GameStoryTaskOption
    {
        /// <summary>選項代號，例如 A/B/C/D</summary>
        public string option_key { get; set; }

        /// <summary>選項文字</summary>
        public string option_context { get; set; }

        /// <summary>選項圖片網址，目前只有商家題庫的選項可能有值</summary>
        public string option_url { get; set; }

        /// <summary>是否為正確答案：1 = 正確、0 = 錯誤（存進資料庫；回傳前端時不輸出）</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? is_correct { get; set; }
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
