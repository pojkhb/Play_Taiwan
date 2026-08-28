using System;

namespace backend.Models
{
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