using System;
using Birko.AI.Resilience.Stores;
using Birko.Data.Models;

namespace Birko.AI.Resilience.Data.Models
{
    /// <summary>One row per LLM call: the persisted <see cref="UsageRecordEntity"/> (TASK-530).</summary>
    [System.ComponentModel.DataAnnotations.Schema.Table("AiUsageRecords")]
    public class UsageRecordModel : AbstractModel
    {
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
        public double EstimatedCostUsd { get; set; }
        public string? ProjectId { get; set; }
        public string? TaskId { get; set; }
        public string? AgentType { get; set; }
        public string? CallerContext { get; set; }
        public DateTime RecordedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public static UsageRecordModel FromEntity(UsageRecordEntity entity) => new()
        {
            Provider = entity.Provider,
            Model = entity.Model,
            PromptTokens = entity.PromptTokens,
            CompletionTokens = entity.CompletionTokens,
            TotalTokens = entity.TotalTokens,
            EstimatedCostUsd = entity.EstimatedCostUsd,
            ProjectId = entity.ProjectId,
            TaskId = entity.TaskId,
            AgentType = entity.AgentType,
            CallerContext = entity.CallerContext,
            RecordedAt = entity.RecordedAt,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
    }
}
