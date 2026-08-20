using System;

namespace backend.Models
{
    /// <summary>任務線索提示 (對應 md_task_hint)。</summary>
    public class TaskHint
    {
        public int HintId { get; set; }
        public string TaskId { get; set; }
        public int HintStage { get; set; }
        public int TriggerWrongCount { get; set; }
        public string HintText { get; set; }
        public string LlmPromptTemplate { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>動態難度 LLM 提示字 (對應 md_difficulty_prompt)。</summary>
    public class DifficultyPrompt
    {
        public int DifficultyStar { get; set; }
        public string Title { get; set; }
        public string LlmPromptTemplate { get; set; }
        public int RaiseVisitThreshold { get; set; }
    }

    /// <summary>隱藏關卡 (對應 md_hidden_level)。</summary>
    public class HiddenLevel
    {
        public string HiddenLevelId { get; set; }
        public string RegionId { get; set; }
        public string PlaceId { get; set; }
        public string StoryId { get; set; }
        public string Title { get; set; }
        public string CulturalBackground { get; set; }
        public string Content { get; set; }
        public decimal? TriggerLat { get; set; }
        public decimal? TriggerLng { get; set; }
        public int TriggerRadiusM { get; set; }
        public string RewardBadgeId { get; set; }
        public string RewardPostcardId { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>獎章池項目 (對應 md_badge_pool)。</summary>
    public class BadgePoolItem
    {
        public int BadgePoolId { get; set; }
        public string StoryId { get; set; }
        public string BadgeId { get; set; }
        public string BadgeName { get; set; }
        public int Weight { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>探員造訪次數 (對應 ep_visit_count)，用於動態難度判定。</summary>
    public class VisitCount
    {
        public string EpId { get; set; }
        public string RegionId { get; set; }
        public int VisitCountValue { get; set; }
        public int CurrentDifficultyStar { get; set; }
    }
}