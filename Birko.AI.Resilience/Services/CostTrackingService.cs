using Birko.AI.Resilience.Configuration;
using Birko.AI.Resilience.Stores;
using Microsoft.Extensions.Logging;

namespace Birko.AI.Resilience.Services
{
    /// <summary>
    /// Tracks LLM API usage costs, persists records, and enforces budgets.
    /// </summary>
    public class CostTrackingService
    {
        private readonly IUsageRepository? _repository;
        private readonly CostTrackingConfiguration _config;
        private readonly ILogger _logger;
        private readonly Dictionary<string, ProviderPricing> _pricingLookup;

        public CostTrackingService(
            CostTrackingConfiguration config,
            ILogger<CostTrackingService> logger,
            IUsageRepository? repository = null)
        {
            _config = config;
            _logger = logger;
            _repository = repository;

            _pricingLookup = new Dictionary<string, ProviderPricing>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in config.Pricing)
            {
                var key = $"{p.Provider}:{p.Model}";
                _pricingLookup[key] = p;
            }
        }

        public async Task RecordUsageAsync(UsageRecord record, CancellationToken ct = default)
        {
            if (!_config.Enabled) return;

            var cost = CalculateCost(record.Provider, record.Model, record.PromptTokens, record.CompletionTokens);

            _logger.LogDebug("Usage: {Provider}/{Model} | {PromptTokens}+{CompletionTokens} tokens | ${Cost:F6}",
                record.Provider, record.Model, record.PromptTokens, record.CompletionTokens, cost);

            if (_repository != null)
            {
                var entity = new UsageRecordEntity
                {
                    Provider = record.Provider,
                    Model = record.Model,
                    PromptTokens = record.PromptTokens,
                    CompletionTokens = record.CompletionTokens,
                    TotalTokens = record.PromptTokens + record.CompletionTokens,
                    EstimatedCostUsd = cost,
                    ProjectId = record.ProjectId,
                    TaskId = record.TaskId,
                    AgentType = record.AgentType,
                    CallerContext = record.CallerContext,
                    RecordedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _repository.RecordUsageAsync(entity, ct);
            }
        }

        public double CalculateCost(string provider, string model, int promptTokens, int completionTokens)
        {
            var pricing = FindPricing(provider, model);
            if (pricing == null) return 0;

            var inputCost = (promptTokens / 1_000_000.0) * pricing.InputPricePerMillionTokens;
            var outputCost = (completionTokens / 1_000_000.0) * pricing.OutputPricePerMillionTokens;
            return inputCost + outputCost;
        }

        public async Task<BudgetStatus> CheckBudgetAsync(string? projectId = null, CancellationToken ct = default)
        {
            // When cost tracking is disabled, budget enforcement must not run either —
            // otherwise CheckBudgetAsync could still return "Budget exceeded" and block calls,
            // inconsistent with RecordUsageAsync's Enabled guard (CR-L012).
            if (!_config.Enabled)
                return new BudgetStatus(true, false, 0, 0, "none");

            var budget = _config.Budget;
            if (_repository == null)
                return new BudgetStatus(true, false, 0, 0, "none");

            // TASK-715: every scope is checked before a warning is answered. The first scope in its warning band used to
            // return at once, so an exceeded monthly or project budget behind a warning daily one let the call go ahead.
            BudgetStatus? warning = null;
            BudgetStatus? Assess(double spend, double limit, string type)
            {
                if (spend >= limit)
                    return new BudgetStatus(false, true, spend, limit, type);
                if (warning == null && spend >= limit * (budget.WarningThresholdPercent / 100.0))
                    warning = new BudgetStatus(true, true, spend, limit, type);
                return null;
            }

            if (budget.DailyBudgetUsd > 0)
            {
                var today = DateTime.UtcNow.Date;
                var dailySpend = await _repository.GetTotalSpendAsync(today, today.AddDays(1), ct: ct);
                if (Assess(dailySpend, budget.DailyBudgetUsd, "daily") is { } exceeded)
                    return exceeded;
            }

            if (budget.MonthlyBudgetUsd > 0)
            {
                var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var monthEnd = monthStart.AddMonths(1);
                var monthlySpend = await _repository.GetTotalSpendAsync(monthStart, monthEnd, ct: ct);
                if (Assess(monthlySpend, budget.MonthlyBudgetUsd, "monthly") is { } exceeded)
                    return exceeded;
            }

            if (budget.ProjectBudgetUsd > 0 && !string.IsNullOrEmpty(projectId))
            {
                var projectSpend = await _repository.GetProjectSpendAsync(projectId, ct);
                if (Assess(projectSpend, budget.ProjectBudgetUsd, "project") is { } exceeded)
                    return exceeded;
            }

            return warning ?? new BudgetStatus(true, false, 0, 0, "none");
        }

        public async Task<List<ProviderUsageSummary>> GetUsageSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default)
        {
            if (_repository == null) return new();
            return await _repository.GetUsageByProviderAsync(from, to, ct);
        }

        public async Task<ProjectUsageSummary?> GetProjectUsageAsync(string projectId, DateTime from, DateTime to, CancellationToken ct = default)
        {
            if (_repository == null) return null;
            return await _repository.GetUsageByProjectAsync(projectId, from, to, ct);
        }

        private ProviderPricing? FindPricing(string provider, string model)
        {
            if (_pricingLookup.TryGetValue($"{provider}:{model}", out var exact))
                return exact;
            if (_pricingLookup.TryGetValue($"{provider}:*", out var wildcard))
                return wildcard;
            return null;
        }
    }

    public record UsageRecord(
        string Provider,
        string Model,
        int PromptTokens,
        int CompletionTokens,
        string? ProjectId = null,
        string? TaskId = null,
        string? AgentType = null,
        string? CallerContext = null);

    public record BudgetStatus(
        bool IsWithinBudget,
        bool IsWarning,
        double CurrentSpend,
        double BudgetLimit,
        string BudgetType);
}
