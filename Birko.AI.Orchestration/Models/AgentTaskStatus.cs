namespace Birko.AI.Orchestration.Models
{
    public enum AgentTaskStatus
    {
        Unassigned,
        NotInitialized,
        Working,
        Done,
        Failed,
        BlockedByFailure
    }

    public enum AgentTaskPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }
}
