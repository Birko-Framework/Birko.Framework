namespace Birko.AI.Orchestration.Models
{
    /// <summary>
    /// An implementation plan consisting of ordered steps with file dependencies.
    /// </summary>
    public class ImplementationPlan
    {
        public string TaskId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string TaskDescription { get; set; } = string.Empty;
        public string? FeatureId { get; set; }

        /// <summary>
        /// Ordered list of implementation steps.
        /// </summary>
        public List<ImplementationStep> Steps { get; set; } = new();

        /// <summary>
        /// Lessons captured during execution for future tasks.
        /// </summary>
        public List<string> LessonsLearned { get; set; } = new();

        /// <summary>
        /// Patterns that worked well during execution.
        /// </summary>
        public List<string> SuccessfulPatterns { get; set; } = new();

        public PlanStatus Status { get; set; } = PlanStatus.Created;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }

    public class ImplementationStep
    {
        public int Index { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public StepStatus Status { get; set; } = StepStatus.Pending;

        /// <summary>
        /// Files expected to be created in this step.
        /// </summary>
        public List<string> FilesToCreate { get; set; } = new();

        /// <summary>
        /// Files expected to be modified in this step.
        /// </summary>
        public List<string> FilesToModify { get; set; } = new();

        /// <summary>
        /// Expected output/content descriptions for verification.
        /// </summary>
        public List<string> ExpectedContent { get; set; } = new();

        public string? ErrorMessage { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public enum PlanStatus { Created, InProgress, Completed, Failed }
    public enum StepStatus { Pending, InProgress, Completed, Failed, Skipped }
}
