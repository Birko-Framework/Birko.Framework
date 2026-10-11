namespace Birko.AI.Orchestration.Models
{
    /// <summary>
    /// Represents an escalation from a worker agent to the orchestrator.
    /// </summary>
    public class EscalationAlert
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ProjectId { get; set; } = "";
        public string? TaskId { get; set; }

        /// <summary>
        /// The worker that raised it — the same id <see cref="ImplementationPlan.AssignedStepIndices"/> is keyed by,
        /// so parallel workers on one plan can be told apart. Null when the source is not a worker.
        /// </summary>
        public string? WorkerId { get; set; }

        public string AgentType { get; set; } = "";
        public EscalationSource Source { get; set; }
        public EscalationType Type { get; set; }
        public string Summary { get; set; } = "";
        public List<ReflectionEntry> ReflectionHistory { get; set; } = new();
        public EscalationStatus Status { get; set; } = EscalationStatus.Pending;
        public string? Resolution { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }
    }

    public class ReflectionEntry
    {
        public int Iteration { get; set; }

        /// <summary>
        /// The step being worked on (1-based, <see cref="ImplementationStep.Index"/>).
        /// </summary>
        public int StepIndex { get; set; }

        /// <summary>0–100.</summary>
        public int ProgressPercent { get; set; }
        public string? Blockers { get; set; }

        /// <summary>0–100.</summary>
        public int ConfidencePercent { get; set; }
        public ReflectionDecision Decision { get; set; }
        public EscalationType? EscalationType { get; set; }
        public string? Adjustment { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    // These are stored inside a persisted plan (Reflections, Escalations), so their numeric values are pinned like
    // PlanStatus / StepStatus. Never renumber.

    public enum EscalationSource { ReflectionTool = 0, ReasoningMonitor = 1 }
    public enum EscalationStatus { Pending = 0, InProgress = 1, Resolved = 2, Failed = 3 }
    public enum ReflectionDecision { Continue = 0, Pivot = 1, Escalate = 2 }

    public enum EscalationType
    {
        TaskInfeasible = 0,
        MissingDependency = 1,
        NeedsSplit = 2,
        WrongApproach = 3,
        WrongAgentType = 4,
    }
}
