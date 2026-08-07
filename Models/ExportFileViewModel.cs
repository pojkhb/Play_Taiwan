using System.Collections.Generic;

namespace backend.ViewModels
{
    public class ExportFileViewModel
    {
        public string FileName { get; set; }
        public string Extension { get; set; }
        public string FullFileName { get; set; }
        public string FileContent { get; set; }
    }
}