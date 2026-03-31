namespace Birko.AI.Orchestration.Models
{
    /// <summary>
    /// Message describing a task assignment to be dispatched to an agent worker.
    /// </summary>
    public class TaskAssignment
    {
        public string ProjectId { get; set; } = string.Empty;
        public string TaskId { get; set; } = string.Empty;
        public string TaskDescription { get; set; } = string.Empty;
        public string AgentType { get; set; } = string.Empty;
        public string? Provider { get; set; }
        public string? Model { get; set; }
        public string WorkspacePath { get; set; } = string.Empty;
        public List<string> AllowedExternalPaths { get; set; } = [];
        public int MaxIterations { get; set; } = 100;

        /// <summary>
        /// Constraints from upstream analysis.
        /// </summary>
        public List<string> Constraints { get; set; } = [];

        /// <summary>
        /// Context passed to the agent (specification, dependency output, etc.).
        /// </summary>
        public string? Context { get; set; }

        /// <summary>
        /// Correlation ID for request-response tracking.
        /// </summary>
        public string CorrelationId { get; set; } = Guid.NewGuid().ToString();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
