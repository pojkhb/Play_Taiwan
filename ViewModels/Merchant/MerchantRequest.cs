namespace backend.ViewModels
{
    /// <summary>
    /// 商家生成影音請求物件
    /// </summary>
    public class GenerateVlogRequest
    {
        /// <summary>故事語氣 (例如: 幽默詼諧、質感專業、溫情走心)</summary>
        public string tone { get; set; }
        /// <summary>推廣資訊/描述文字</summary>
        public string promotion_text { get; set; }
        /// <summary>上傳的素材網址</summary>
        public string media_url { get; set; }
    }
}