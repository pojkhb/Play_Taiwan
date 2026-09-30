using System.Collections.Generic;

namespace backend.ViewModels
{
    public class QuestionOptionInput
    {
        public string option_context { get; set; }
        public string option_url { get; set; }
        public bool is_correct { get; set; }
        public string option_key { get; set; }
    }

    /// <summary>新增題目請求；所屬商家由 JWT 的 s_id 決定，不需帶 store_id。</summary>
    public class QuestionCreateRequest
    {
        public string question_describe { get; set; }
        public List<QuestionOptionInput> options { get; set; }
    }

    public class QuestionUpdateRequest
    {
        public string question_describe { get; set; }
        public List<QuestionOptionInput> options { get; set; }
    }

    public class QuestionOptionResponse
    {
        public int option_id { get; set; }
        public string option_context { get; set; }
        public string option_url { get; set; }
        public bool is_correct { get; set; }
        public string option_key { get; set; }
    }

    public class QuestionResponse
    {
        public int question_id { get; set; }
        public int store_id { get; set; }
        public string question_describe { get; set; }
        public List<QuestionOptionResponse> options { get; set; }
    }
}
