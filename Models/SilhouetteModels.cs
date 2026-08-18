using System;

namespace backend.Models
{
    public class Silhouette
    {
        public string silhouette_id { get; set; }
        public string name { get; set; }
        public string image_url { get; set; }
        public string city { get; set; }
        public string category { get; set; }
        public bool is_active { get; set; }
        public int sort_order { get; set; }
    }
}
