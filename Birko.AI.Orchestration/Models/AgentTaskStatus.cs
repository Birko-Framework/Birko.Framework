namespace Birko.AI.Orchestration.Models
{
    /// <summary>
    /// ⚠ Numeric values pinned — a stored task status reads back as the same status. Never renumber.
    /// </summary>
    public enum AgentTaskStatus
    {
        Unassigned = 0,
        NotInitialized = 1,
        Working = 2,
        Done = 3,
        Failed = 4,
        BlockedByFailure = 5
    }

    public enum AgentTaskPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }
}
