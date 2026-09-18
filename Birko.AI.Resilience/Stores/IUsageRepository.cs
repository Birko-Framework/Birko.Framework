namespace Birko.AI.Resilience.Stores
{
    /// <summary>
    /// Abstraction for persisting LLM usage records. Implement per platform (SQL, CosmosDB, etc.).
    /// </summary>
    public interface IUsageRepository
    {
        Task RecordUsageAsync(UsageRecordEntity entity);
        Task<double> GetTotalSpendAsync(DateTime from, DateTime to);
        Task<double> GetProjectSpendAsync(string projectId);
        Task<List<ProviderUsageSummary>> GetUsageByProviderAsync(DateTime from, DateTime to);
        Task<ProjectUsageSummary?> GetUsageByProjectAsync(string projectId, DateTime from, DateTime to);
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
