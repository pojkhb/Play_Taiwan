using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http; /* 讓系統認識 IFormFile */
using System.Text.Json.Serialization; /* 讓系統認識 JsonPropertyName */
namespace backend.Models
{
    /* 接收前端 APP 傳來的生成明信片請求 (對應 Swagger 表單格式) */
    public class AiPostcardGenerateRequest
    {
        public IFormFile user_image { get; set; } /* 必須使用 IFormFile 接收前端上傳的圖片檔案 */
        public string spot_name { get; set; } /* 景點名稱 (例如：台北101) */
        public string user_prompt { get; set; } /* 使用者輸入的提示詞 (例如：復古水墨風) */
        public string story_id { get; set; } /* 額外傳遞的劇本代號，方便後端寫入 DB 時關聯 */
        public bool is_night_edition { get; set; }
    }

    /* 解析外部 vlog.angelalala.com 回傳的 JSON 結構 */
    public class AiPostcardApiResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } /* 回傳狀態 (例如：success) */

        [JsonPropertyName("task_id")]
        public string TaskId { get; set; } /* 任務識別碼 (例如：d1863bc6) */

        [JsonPropertyName("postcard_introduction")]
        public string PostcardIntroduction { get; set; } /* AI 生成的明信片介紹說明文字 */

        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; } /* AI 生成的明信片圖片下載網址 */
    }
    /// <summary>任務線索提示 (對應 md_task_hint)。</summary>
    public class TaskHint
    {
        public int HintId { get; set; }                    // 提示紀錄唯一編號
        public string TaskId { get; set; }                  // 對應的任務代號
        public int HintStage { get; set; }                  // 提示階段(第幾階段的提示，數字越大提示越明顯)
        public int TriggerWrongCount { get; set; }          // 累積答錯幾次後觸發此階段提示
        public string HintText { get; set; }                // 提示文字內容
        public string LlmPromptTemplate { get; set; }        // 給 LLM 動態生成提示用的提示詞範本
        public bool IsActive { get; set; }                   // 是否啟用此提示
    }

    /// <summary>動態難度 LLM 提示字 (對應 md_difficulty_prompt)。</summary>
    public class DifficultyPrompt
    {
        public int DifficultyStar { get; set; }              // 難度星級 1~5
        public string Title { get; set; }                     // 該難度等級的標題名稱
        public string LlmPromptTemplate { get; set; }          // 給 LLM 依此難度生成內容用的提示詞範本
        public int RaiseVisitThreshold { get; set; }            // 累積造訪次數達到此門檻後，難度自動提升
    }

    /// <summary>隱藏關卡 (對應 md_hidden_level)。</summary>
    public class HiddenLevel
    {
        public string HiddenLevelId { get; set; }            // 隱藏關卡代號
        public string RegionId { get; set; }                  // 所屬地區代號
        public string PlaceId { get; set; }                    // 所屬景點代號
        public string StoryId { get; set; }                     // 所屬劇本代號
        public string Title { get; set; }                        // 隱藏關卡標題
        public string CulturalBackground { get; set; }            // 在地歷史/文化背景說明
        public string Content { get; set; }                        // 支線劇情內容
        public decimal? TriggerLat { get; set; }                    // 觸發此隱藏關卡所需的GPS緯度
        public decimal? TriggerLng { get; set; }                     // 觸發此隱藏關卡所需的GPS經度
        public int TriggerRadiusM { get; set; }                       // 觸發範圍半徑(公尺)
        public string RewardBadgeId { get; set; }                     // 觸發後可能給予的徽章代號
        public string RewardPostcardId { get; set; }                   // 觸發後可能給予的明信片代號
        public bool IsActive { get; set; }                              // 是否啟用此隱藏關卡
    }

    /// <summary>獎章池項目 (對應 md_badge_pool)。</summary>
    public class BadgePoolItem
    {
        public int BadgePoolId { get; set; }                 // 獎章池紀錄唯一編號
        public string StoryId { get; set; }                    // 所屬劇本代號
        public string BadgeId { get; set; }                     // 徽章代號
        public string BadgeName { get; set; }                    // 徽章名稱
        public int Weight { get; set; }                            // 抽取權重，數字越大越容易被抽到
        public bool IsActive { get; set; }                          // 是否啟用此獎章池項目
    }

    /// <summary>探員造訪次數 (對應 ep_visit_count)，用於動態難度判定。</summary>
    public class VisitCount
    {
        public string EpId { get; set; }                     // 探員代號
        public string RegionId { get; set; }                  // 地區代號
        public int VisitCountValue { get; set; }                // 累積造訪次數
        public int CurrentDifficultyStar { get; set; }            // 目前套用的難度星級 1~5
    }
    
}