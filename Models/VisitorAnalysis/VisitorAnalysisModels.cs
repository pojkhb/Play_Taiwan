using System.Collections.Generic;

namespace backend.Models
{
    /// <summary>
    /// 客群分析儀表板完整回應。
    /// 資料來自獨立資料庫 <c>visitor_analysis</c> 的 attraction、domestic_analysis、inbound_analysis。
    /// </summary>
    public class VisitorAnalysisDashboardResponse
    {
        /// <summary>景點總數（attraction 列數）</summary>
        public int attraction_count { get; set; }

        /// <summary>有國內旅客分析的景點數</summary>
        public int domestic_count { get; set; }

        /// <summary>有來臺旅客分析的景點數</summary>
        public int inbound_count { get; set; }

        /// <summary>每個景點及其國內／來臺客群分析；沒有該類樣本時對應欄位為 null</summary>
        public List<VisitorAnalysisAttraction> attractions { get; set; }
    }

    /// <summary>一個景點頁的客群分析，對應 attraction 一列，並帶入國內與來臺分析。</summary>
    public class VisitorAnalysisAttraction
    {
        /// <summary>景點頁主鍵，對應 Neo4j Attraction.uid（UUID）</summary>
        public string uid { get; set; }

        /// <summary>景點名稱</summary>
        public string name { get; set; }

        /// <summary>縣市，來自調查 county</summary>
        public string city { get; set; }

        /// <summary>國內旅客分析；沒有國內樣本時為 null</summary>
        public DomesticVisitorAnalysis domestic { get; set; }

        /// <summary>來臺旅客分析；沒有來臺樣本時為 null</summary>
        public InboundVisitorAnalysis inbound { get; set; }
    }

    /// <summary>該景點的國內旅客分析，對應 <c>domestic_analysis</c> 一列。</summary>
    public class DomesticVisitorAnalysis
    {
        /// <summary>對應 attraction.uid</summary>
        public string attraction_uid { get; set; }

        /// <summary>國內調查旅次數（未加權，多年合計）</summary>
        public int? n_trips { get; set; }

        /// <summary>國內加權旅次數；來臺沒有此欄</summary>
        public decimal? weighted_trips { get; set; }

        /// <summary>有樣本的民國年，逗號分隔，例如 108,109,110</summary>
        public string years { get; set; }

        /// <summary>有樣本的年數</summary>
        public int? n_years { get; set; }

        /// <summary>最早有樣本的民國年</summary>
        public int? first_year { get; set; }

        /// <summary>最晚有樣本的民國年</summary>
        public int? last_year { get; set; }

        /// <summary>平均停留天數</summary>
        public decimal? mean_days { get; set; }

        /// <summary>平均停留夜數</summary>
        public decimal? mean_nights { get; set; }

        /// <summary>旅次最多的年齡層標籤，例如 30～39歲</summary>
        public string main_age { get; set; }

        /// <summary>主年齡層占該景點國內旅次百分比</summary>
        public decimal? main_age_pct { get; set; }

        /// <summary>男性百分比</summary>
        public decimal? male_pct { get; set; }

        /// <summary>女性百分比</summary>
        public decimal? female_pct { get; set; }

        /// <summary>旅次最多的旅遊目的，例如 休閒／觀光</summary>
        public string main_purpose { get; set; }

        /// <summary>主目的占該景點國內旅次百分比</summary>
        public decimal? main_purpose_pct { get; set; }

        /// <summary>目的：休閒／觀光百分比</summary>
        public decimal? purpose_leisure_pct { get; set; }

        /// <summary>目的：商務百分比</summary>
        public decimal? purpose_business_pct { get; set; }

        /// <summary>目的：探親訪友百分比</summary>
        public decimal? purpose_vfr_pct { get; set; }

        /// <summary>前幾名交通方式與百分比，分號分隔</summary>
        public string top_transports { get; set; }

        /// <summary>前幾名活動與百分比，分號分隔</summary>
        public string top_activities { get; set; }

        /// <summary>國內分群編號；未入群為空</summary>
        public int? cluster_id { get; set; }

        /// <summary>國內分群名稱；未入群為空</summary>
        public string cluster_name { get; set; }

        /// <summary>1=有入群；0=樣本不足未入群</summary>
        public int in_cluster { get; set; }

        /// <summary>據點人流合計；對不到人流為空</summary>
        public decimal? ff_visitors_sum { get; set; }

        /// <summary>據點人流年平均；對不到人流為空</summary>
        public decimal? ff_visitors_mean { get; set; }

        /// <summary>人流有資料的年數；對不到人流為空</summary>
        public int? ff_years { get; set; }
    }

    /// <summary>該景點的來臺旅客分析，對應 <c>inbound_analysis</c> 一列。</summary>
    public class InboundVisitorAnalysis
    {
        /// <summary>對應 attraction.uid</summary>
        public string attraction_uid { get; set; }

        /// <summary>來臺調查旅次數（來臺無加權，即旅次數）</summary>
        public int? n_trips { get; set; }

        /// <summary>有樣本的民國年，逗號分隔；111年無調查不納入</summary>
        public string years { get; set; }

        /// <summary>有樣本的年數</summary>
        public int? n_years { get; set; }

        /// <summary>最早有樣本的民國年</summary>
        public int? first_year { get; set; }

        /// <summary>最晚有樣本的民國年</summary>
        public int? last_year { get; set; }

        /// <summary>平均停留天數；僅 103 年調查為天</summary>
        public decimal? mean_days { get; set; }

        /// <summary>平均停留夜數；103 年以外用夜</summary>
        public decimal? mean_nights { get; set; }

        /// <summary>主年齡粗組：29歲以下／30-49歲／50歲以上</summary>
        public string main_age { get; set; }

        /// <summary>主年齡粗組占該景點來臺旅次百分比</summary>
        public decimal? main_age_pct { get; set; }

        /// <summary>男性百分比</summary>
        public decimal? male_pct { get; set; }

        /// <summary>女性百分比</summary>
        public decimal? female_pct { get; set; }

        /// <summary>年齡粗組：29歲以下百分比</summary>
        public decimal? age_u29_pct { get; set; }

        /// <summary>年齡粗組：30–49歲百分比</summary>
        public decimal? age_3049_pct { get; set; }

        /// <summary>年齡粗組：50歲以上百分比</summary>
        public decimal? age_50p_pct { get; set; }

        /// <summary>旅次最多的來臺目的，例如 觀光、探親訪友、商務</summary>
        public string main_purpose { get; set; }

        /// <summary>主目的占該景點來臺旅次百分比</summary>
        public decimal? main_purpose_pct { get; set; }

        /// <summary>目的：觀光百分比</summary>
        public decimal? purpose_sightseeing_pct { get; set; }

        /// <summary>目的：商務百分比</summary>
        public decimal? purpose_business_pct { get; set; }

        /// <summary>目的：探親訪友百分比</summary>
        public decimal? purpose_vfr_pct { get; set; }

        /// <summary>目的：會議百分比</summary>
        public decimal? purpose_meeting_pct { get; set; }

        /// <summary>目的：求學百分比</summary>
        public decimal? purpose_study_pct { get; set; }

        /// <summary>目的：醫療百分比</summary>
        public decimal? purpose_medical_pct { get; set; }

        /// <summary>前幾名交通方式與百分比，分號分隔</summary>
        public string top_transports { get; set; }

        /// <summary>前幾名活動與百分比，分號分隔</summary>
        public string top_activities { get; set; }

        /// <summary>來臺分群編號；未入群為空</summary>
        public int? cluster_id { get; set; }

        /// <summary>來臺分群名稱；未入群為空</summary>
        public string cluster_name { get; set; }

        /// <summary>1=有入群；0=樣本不足未入群</summary>
        public int in_cluster { get; set; }

        /// <summary>據點人流合計；對不到人流為空</summary>
        public decimal? ff_visitors_sum { get; set; }

        /// <summary>據點人流年平均；對不到人流為空</summary>
        public decimal? ff_visitors_mean { get; set; }

        /// <summary>人流有資料的年數；對不到人流為空</summary>
        public int? ff_years { get; set; }
    }

    // ===================== Neo4j / AI 經營策略（經 FastAPI） =====================

    /// <summary>景點清單分頁回應（去重後的顯示列）。</summary>
    public class VisitorAttractionListResponse
    {
        public int total { get; set; }
        public int page { get; set; }
        public int page_size { get; set; }
        public List<VisitorAttractionListItem> items { get; set; }
    }

    /// <summary>
    /// 同名同地址去重後的一列；<see cref="uids"/> 保留全部 UID 對應。
    /// </summary>
    public class VisitorAttractionListItem
    {
        public string name { get; set; }
        public string address { get; set; }
        /// <summary>代表 UID（uids 的第一個）</summary>
        public string uid { get; set; }
        /// <summary>同名同地址下的全部 Attraction.uid</summary>
        public List<string> uids { get; set; }
        public bool has_analysis { get; set; }
    }

    /// <summary>AI 商家經營策略對話請求。</summary>
    public class VisitorStrategyChatRequest
    {
        public string attraction_uid { get; set; }
        public string business_type { get; set; }
        public string goal { get; set; }
        public string budget { get; set; }
        /// <summary>domestic / inbound / 空白=自動</summary>
        public string visitor_type { get; set; }
        public string question { get; set; }
    }

    /// <summary>AI 商家經營策略對話回應（轉自 FastAPI）。</summary>
    public class VisitorStrategyChatResponse
    {
        /// <summary>ok / not_ready / attraction_not_found</summary>
        public string status { get; set; }
        public string message { get; set; }
        /// <summary>給前端聊天室顯示的回覆文字（後端組 prompt，使用者只送 question）。</summary>
        public string reply { get; set; }
        public object audience_summary { get; set; }
        public List<object> strategies { get; set; }
        public List<object> sources { get; set; }
        public string rag_status { get; set; }
        public string llm_status { get; set; }
    }
}
