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

    public class QuestionCreateRequest
    {
        public int store_id { get; set; }
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
