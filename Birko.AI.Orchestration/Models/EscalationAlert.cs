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
        public int StepIndex { get; set; }
        public int ProgressPercent { get; set; }
        public string? Blockers { get; set; }
        public int ConfidencePercent { get; set; }
        public ReflectionDecision Decision { get; set; }
        public EscalationType? EscalationType { get; set; }
        public string? Adjustment { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public enum EscalationSource { ReflectionTool, ReasoningMonitor }
    public enum EscalationStatus { Pending, InProgress, Resolved, Failed }
    public enum ReflectionDecision { Continue, Pivot, Escalate }

    public enum EscalationType
    {
        TaskInfeasible,
        MissingDependency,
        NeedsSplit,
        WrongApproach,
        WrongAgentType
    }
}
