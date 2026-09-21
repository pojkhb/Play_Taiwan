namespace backend.ViewModels
{
    /// <summary>取得線索提示的回應內容。</summary>
    public class HintResponse
    {
        public bool Available { get; set; }
        public int HintStage { get; set; }
        public string HintText { get; set; }
        public string LlmPromptTemplate { get; set; }
    }

    /// <summary>取得線索提示的請求內容。</summary>
    public class HintRequest
    {
        public string TaskId { get; set; }
        public int WrongCount { get; set; }
    }
}