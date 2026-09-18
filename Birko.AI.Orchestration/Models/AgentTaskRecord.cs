namespace Birko.AI.Orchestration.Models
{
    /// <summary>
    /// Represents a tracked task in an agent orchestrator.
    /// </summary>
    public class AgentTaskRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Task { get; set; } = string.Empty;
        public string AssignedAgent { get; set; } = string.Empty;
        public string? ProjectId { get; set; }
        public AgentTaskStatus Status { get; set; } = AgentTaskStatus.Unassigned;
        public AgentTaskPriority Priority { get; set; } = AgentTaskPriority.Normal;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Task IDs that this task depends on.
        /// </summary>
        public List<string> Dependencies { get; set; } = new();

        /// <summary>
        /// Feature/work-item ID this task implements.
        /// </summary>
        public string? FeatureId { get; set; }

        /// <summary>
        /// Files created or modified by this task.
        /// </summary>
        public List<string> OutputFiles { get; set; } = new();

        /// <summary>
        /// Number of retry attempts made.
        /// </summary>
        public int RetryCount { get; set; } = 0;

        /// <summary>
        /// Error category for determining retry eligibility (transient vs permanent).
        /// </summary>
        public string? ErrorCategory { get; set; }

        /// <summary>
        /// LLM provider used for this task.
        /// </summary>
        public string? Provider { get; set; }
    }
}
