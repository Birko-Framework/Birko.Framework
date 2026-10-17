namespace Birko.AI.Resilience.Stores
{
    /// <summary>
    /// Abstraction for persisting LLM usage records. <c>Birko.AI.Resilience.Data</c> implements it over any
    /// <c>IAsyncBulkStore</c> that aggregates (TASK-530).
    /// </summary>
    /// <remarks>Time windows are half-open: <c>from</c> is included, <c>to</c> is not.</remarks>
    public interface IUsageRepository
    {
        Task RecordUsageAsync(UsageRecordEntity entity, CancellationToken ct = default);

        /// <summary>Total estimated cost in the window; for one provider only when <paramref name="provider"/> is given.</summary>
        Task<double> GetTotalSpendAsync(DateTime from, DateTime to, string? provider = null, CancellationToken ct = default);
        Task<double> GetProjectSpendAsync(string projectId, CancellationToken ct = default);
        Task<List<ProviderUsageSummary>> GetUsageByProviderAsync(DateTime from, DateTime to, CancellationToken ct = default);
        Task<ProjectUsageSummary?> GetUsageByProjectAsync(string projectId, DateTime from, DateTime to, CancellationToken ct = default);
    }

    public class UsageRecordEntity
    {
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
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
    }

    public class ProviderUsageSummary
    {
        public string Provider { get; set; } = "";
        public int TotalRequests { get; set; }
        public int TotalTokens { get; set; }
        public double TotalCostUsd { get; set; }
    }

    public class ProjectUsageSummary
    {
        public string ProjectId { get; set; } = "";
        public int TotalRequests { get; set; }
        public int TotalTokens { get; set; }
        public double TotalCostUsd { get; set; }
    }
}
