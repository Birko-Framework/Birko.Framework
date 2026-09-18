using Birko.AI.Resilience.Configuration;
using Birko.AI.Resilience.Services;
using Birko.AI.Resilience.Stores;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Birko.AI.Resilience.Tests;

/// <summary>
/// Regression for CR-L012: RecordUsageAsync guards with "if (!_config.Enabled) return;" but
/// CheckBudgetAsync had no such guard, so budget enforcement kept running (and could report
/// "budget exceeded") even when cost tracking was disabled. CheckBudgetAsync must now short-circuit
/// to a within-budget status when Enabled is false, regardless of recorded spend.
/// </summary>
public class CostTrackingBudgetTests
{
    private sealed class OverBudgetRepository : IUsageRepository
    {
        public Task RecordUsageAsync(UsageRecordEntity entity) => Task.CompletedTask;
        // Report spend far above any budget so the guard is the only thing that can keep us within budget.
        public Task<double> GetTotalSpendAsync(DateTime from, DateTime to) => Task.FromResult(1_000_000d);
        public Task<double> GetProjectSpendAsync(string projectId) => Task.FromResult(1_000_000d);
        public Task<List<ProviderUsageSummary>> GetUsageByProviderAsync(DateTime from, DateTime to) => Task.FromResult(new List<ProviderUsageSummary>());
        public Task<ProjectUsageSummary?> GetUsageByProjectAsync(string projectId, DateTime from, DateTime to) => Task.FromResult<ProjectUsageSummary?>(null);
    }

    private static CostTrackingConfiguration Config(bool enabled) => new()
    {
        Enabled = enabled,
        Budget = new BudgetConfiguration { DailyBudgetUsd = 1 }
    };

    [Fact]
    public async Task CheckBudgetAsync_Disabled_ReturnsWithinBudget_EvenWhenSpendExceedsLimit()
    {
        var svc = new CostTrackingService(Config(enabled: false), NullLogger<CostTrackingService>.Instance, new OverBudgetRepository());

        var status = await svc.CheckBudgetAsync();

        status.IsWithinBudget.Should().BeTrue();
        status.IsWarning.Should().BeFalse();
        status.BudgetType.Should().Be("none");
    }

    [Fact]
    public async Task CheckBudgetAsync_Enabled_StillEnforcesBudget()
    {
        var svc = new CostTrackingService(Config(enabled: true), NullLogger<CostTrackingService>.Instance, new OverBudgetRepository());

        var status = await svc.CheckBudgetAsync();

        // With tracking enabled and spend over the daily limit, enforcement still fires.
        status.IsWithinBudget.Should().BeFalse();
        status.BudgetType.Should().Be("daily");
    }
}
