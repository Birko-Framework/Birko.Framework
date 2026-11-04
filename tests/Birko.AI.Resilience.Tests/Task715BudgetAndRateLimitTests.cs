using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Resilience.Configuration;
using Birko.AI.Resilience.Services;
using Birko.AI.Resilience.Stores;
using Birko.AI.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Birko.AI.Resilience.Tests;

/// <summary>
/// TASK-715. Measured before the fix: <c>CheckBudgetAsync</c> returned at the first scope that only warned, so with the
/// daily budget in its warning band an exceeded monthly or project budget was never checked and the call went ahead;
/// <c>TrackedLlmProvider</c> counted a request against the rate limiter only when the response carried token usage, which
/// the OpenAI-compatible providers do not send, so their requests-per-minute limit never applied.
/// </summary>
public class Task715BudgetAndRateLimitTests
{
    private sealed class Spend : IUsageRepository
    {
        public double Total { get; init; }
        public double Project { get; init; }
        public Task RecordUsageAsync(UsageRecordEntity entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task<double> GetTotalSpendAsync(DateTime from, DateTime to, string? provider = null, CancellationToken ct = default) => Task.FromResult(Total);
        public Task<double> GetProjectSpendAsync(string projectId, CancellationToken ct = default) => Task.FromResult(Project);
        public Task<List<ProviderUsageSummary>> GetUsageByProviderAsync(DateTime from, DateTime to, CancellationToken ct = default) => Task.FromResult(new List<ProviderUsageSummary>());
        public Task<ProjectUsageSummary?> GetUsageByProjectAsync(string projectId, DateTime from, DateTime to, CancellationToken ct = default) => Task.FromResult<ProjectUsageSummary?>(null);
    }

    private static CostTrackingService Service(BudgetConfiguration budget, Spend spend)
        => new(new CostTrackingConfiguration { Enabled = true, Budget = budget }, NullLogger<CostTrackingService>.Instance, spend);

    [Fact]
    public async Task A_daily_warning_does_not_hide_an_exceeded_monthly_budget()
    {
        // 9 spent: within the daily 10 but past its 80% warning, over the monthly 5.
        var status = await Service(new BudgetConfiguration { DailyBudgetUsd = 10, MonthlyBudgetUsd = 5, WarningThresholdPercent = 80 }, new Spend { Total = 9 })
            .CheckBudgetAsync();

        status.IsWithinBudget.Should().BeFalse();
        status.BudgetType.Should().Be("monthly");
    }

    [Fact]
    public async Task A_daily_warning_does_not_hide_an_exceeded_project_budget()
    {
        var status = await Service(new BudgetConfiguration { DailyBudgetUsd = 10, ProjectBudgetUsd = 3, WarningThresholdPercent = 80 }, new Spend { Total = 9, Project = 4 })
            .CheckBudgetAsync("p1");

        status.IsWithinBudget.Should().BeFalse();
        status.BudgetType.Should().Be("project");
    }

    [Fact]
    public async Task With_nothing_exceeded_the_first_warning_is_reported()
    {
        var status = await Service(new BudgetConfiguration { DailyBudgetUsd = 10, MonthlyBudgetUsd = 100, WarningThresholdPercent = 80 }, new Spend { Total = 9 })
            .CheckBudgetAsync();

        status.IsWithinBudget.Should().BeTrue();
        status.IsWarning.Should().BeTrue();
        status.BudgetType.Should().Be("daily");
    }

    private sealed class NoUsageProvider : ILlmProvider
    {
        public int Calls { get; private set; }
        public string Name => "openai";
        public Action<string, string>? MessageCallback { get; set; }
        public Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new LlmResponse { StopReason = "end_turn", Content = [] });   // no Usage, as the OpenAI-compatible parser
        }
        public Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_request_without_usage_still_counts_against_the_request_limit()
    {
        var limiter = new ProviderRateLimiter(new RateLimitConfiguration
        {
            Enabled = true,
            ProviderLimits = { new ProviderRateLimit { Provider = "openai", RequestsPerMinute = 1 } }
        });
        var inner = new NoUsageProvider();
        var tracked = new TrackedLlmProvider(inner, rateLimiter: limiter)
        {
            MaxRateLimitWaitAttempts = 1,
            MaxSingleRateLimitWait = TimeSpan.FromMilliseconds(5)
        };

        await tracked.SendMessageAsync(new List<Message>(), new List<Tool>(), "sys");
        var second = await tracked.SendMessageAsync(new List<Message>(), new List<Tool>(), "sys");

        inner.Calls.Should().Be(1, "one request per minute is allowed");
        second.StopReason.Should().Be("error");
    }

    [Fact]
    public void RecordTokens_adds_tokens_without_counting_a_request()
    {
        var limiter = new ProviderRateLimiter(new RateLimitConfiguration
        {
            Enabled = true,
            ProviderLimits = { new ProviderRateLimit { Provider = "openai", RequestsPerMinute = 10 } }
        });
        limiter.RecordRequest("openai");

        limiter.RecordTokens("openai", 120);

        var status = limiter.GetAllStatuses()["openai"];
        status.RequestsThisMinute.Should().Be(1);
        status.TokensThisMinute.Should().Be(120);
        status.TokensToday.Should().Be(120);
    }
}
