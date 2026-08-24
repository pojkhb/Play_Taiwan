using System.Collections.Generic;

namespace backend.Models
{
    public class AiStoryResult
    {
        public string title { get; set; } /* 劇本主標題 (例如：府城儒生的失落卷) */
        public string subtitle { get; set; } /* 劇本副標題 (例如：尋回百年記憶) */
        public string prologue { get; set; } /* 劇本前傳/引言 (例如：清朝年間...) */
        public string synopsis { get; set; } /* 劇本大綱/探索總覽 */
        public List<string> expected_badges { get; set; } /* 預期可獲得的徽章 ID 清單 */
        public int expected_postcards { get; set; } /* 預期可獲得的明信片數量 */
        public List<AiStoryNode> nodes { get; set; } /* 劇本包含的所有節點(景點)清單，限制為 5 到 7 個 */
    }

    public class AiStoryNode
    {
        public string place_id { get; set; } /* 景點代碼 (對應 md_place 的真實景點 ID，不可為 AI 幻覺) */
        public string node_title { get; set; } /* 節點名稱 (通常等於景點名稱，例如：臺南孔廟) */
        public int node_order { get; set; } /* 節點順序 (例如：1 代表第一站，2 代表第二站) */
    }
}