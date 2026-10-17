using Birko.AI.Resilience.Data.Models;
using Birko.AI.Resilience.Data.Stores;
using Birko.AI.Resilience.Services;
using Birko.AI.Resilience.Stores;
using Birko.Data.InMemory.Stores;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.AI.Resilience.Data.Tests;

/// <summary>
/// TASK-530 — the store-backed <see cref="ICircuitBreakerStore"/> and <see cref="IUsageRepository"/>, run against the
/// real <see cref="ProviderCircuitBreaker"/> and against each backend below. Both used to say "implement per platform",
/// and nothing did: DraCode's breaker ran with no store, so an open circuit closed on every restart.
/// </summary>
public abstract class ResilienceStoreTests
{
    protected abstract IAsyncBulkStore<CircuitBreakerStateModel> BreakerStore();
    protected abstract IAsyncBulkStore<UsageRecordModel> UsageStore();

    private static readonly DateTime Day = new(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);

    // ── circuit breaker ──

    [Fact]
    public async Task An_open_circuit_is_still_open_in_a_new_breaker_over_the_same_store()
    {
        var store = new StoreCircuitBreakerStore(BreakerStore());
        var first = new ProviderCircuitBreaker(failureThreshold: 2, store: store);
        first.RecordFailure("OpenAI");
        first.RecordFailure("OpenAI");
        first.RecordFailure("claude");
        await first.FlushPersistenceAsync();

        var second = new ProviderCircuitBreaker(failureThreshold: 2, store: store);
        await second.LoadPersistedStateAsync();

        second.GetState("openai").Should().Be(ProviderCircuitBreaker.CircuitState.Open);
        second.CanRetry("openai").Should().BeFalse();
        second.GetAllStates()["claude"].Failures.Should().Be(1);
        second.GetState("claude").Should().Be(ProviderCircuitBreaker.CircuitState.Closed);
    }

    [Fact]
    public async Task Saving_a_provider_again_replaces_its_state_instead_of_adding_a_row()
    {
        var raw = BreakerStore();
        var store = new StoreCircuitBreakerStore(raw);
        var breaker = new ProviderCircuitBreaker(failureThreshold: 5, store: store);
        for (var i = 0; i < 4; i++) breaker.RecordFailure("openai");
        breaker.RecordSuccess("openai");
        await breaker.FlushPersistenceAsync();

        var all = await store.LoadAllAsync();
        all.Should().ContainSingle();
        all[0].Provider.Should().Be("openai");
        all[0].ConsecutiveFailures.Should().Be(0);
        all[0].State.Should().Be((int)ProviderCircuitBreaker.CircuitState.Closed);
    }

    [Fact]
    public async Task A_reset_is_persisted_so_a_reloaded_breaker_does_not_reopen_it()
    {
        var store = new StoreCircuitBreakerStore(BreakerStore());
        var first = new ProviderCircuitBreaker(failureThreshold: 1, store: store);
        first.RecordFailure("openai");
        first.RecordFailure("gemini");
        first.Reset("openai");
        first.ResetAll();
        await first.FlushPersistenceAsync();

        var second = new ProviderCircuitBreaker(failureThreshold: 1, store: store);
        await second.LoadPersistedStateAsync();

        second.GetState("openai").Should().Be(ProviderCircuitBreaker.CircuitState.Closed);
        second.GetState("gemini").Should().Be(ProviderCircuitBreaker.CircuitState.Closed);
        second.CanRetry("gemini").Should().BeTrue();
    }

    [Fact]
    public async Task A_state_without_a_provider_is_refused()
    {
        var store = new StoreCircuitBreakerStore(BreakerStore());
        var act = () => store.SaveAsync(new CircuitBreakerState { Provider = " " });
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── usage ──

    private async Task<StoreUsageRepository> Seeded()
    {
        var repo = new StoreUsageRepository(UsageStore());
        await repo.RecordUsageAsync(Usage("openai", 0.25, 100, Day.AddHours(1), "p1"));
        await repo.RecordUsageAsync(Usage("openai", 0.50, 200, Day.AddHours(2), "p2"));
        await repo.RecordUsageAsync(Usage("claude", 1.00, 300, Day.AddHours(3), "p1"));
        await repo.RecordUsageAsync(Usage("claude", 8.00, 400, Day.AddDays(1), "p1"));   // the next day: outside
        await repo.RecordUsageAsync(Usage("openai", 16.00, 500, Day.AddTicks(-1), null)); // the day before: outside
        return repo;
    }

    private static UsageRecordEntity Usage(string provider, double cost, int tokens, DateTime at, string? project) => new()
    {
        Provider = provider,
        Model = "m",
        PromptTokens = tokens / 2,
        CompletionTokens = tokens - tokens / 2,
        TotalTokens = tokens,
        EstimatedCostUsd = cost,
        ProjectId = project,
        RecordedAt = at,
        CreatedAt = at,
        UpdatedAt = at,
    };

    [Fact]
    public async Task Total_spend_counts_the_half_open_window_only()
    {
        var repo = await Seeded();
        (await repo.GetTotalSpendAsync(Day, Day.AddDays(1))).Should().Be(1.75);
    }

    [Fact]
    public async Task Total_spend_for_one_provider_counts_that_provider_only()
    {
        var repo = await Seeded();
        (await repo.GetTotalSpendAsync(Day, Day.AddDays(1), "openai")).Should().Be(0.75);
        (await repo.GetTotalSpendAsync(Day, Day.AddDays(1), "claude")).Should().Be(1.00);
        (await repo.GetTotalSpendAsync(Day, Day.AddDays(1), "gemini")).Should().Be(0);
    }

    [Fact]
    public async Task An_empty_window_spends_nothing()
    {
        var repo = new StoreUsageRepository(UsageStore());
        (await repo.GetTotalSpendAsync(Day, Day.AddDays(1))).Should().Be(0);
        (await repo.GetProjectSpendAsync("p1")).Should().Be(0);
        (await repo.GetUsageByProviderAsync(Day, Day.AddDays(1))).Should().BeEmpty();
        (await repo.GetUsageByProjectAsync("p1", Day, Day.AddDays(1))).Should().BeNull();
    }

    [Fact]
    public async Task Usage_by_provider_groups_requests_tokens_and_cost()
    {
        var repo = await Seeded();
        var summary = await repo.GetUsageByProviderAsync(Day, Day.AddDays(1));

        summary.Should().BeEquivalentTo(new[]
        {
            new ProviderUsageSummary { Provider = "claude", TotalRequests = 1, TotalTokens = 300, TotalCostUsd = 1.00 },
            new ProviderUsageSummary { Provider = "openai", TotalRequests = 2, TotalTokens = 300, TotalCostUsd = 0.75 },
        }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Project_spend_is_all_time_and_project_usage_is_windowed()
    {
        var repo = await Seeded();
        (await repo.GetProjectSpendAsync("p1")).Should().Be(9.25);

        var p1 = await repo.GetUsageByProjectAsync("p1", Day, Day.AddDays(1));
        p1.Should().BeEquivalentTo(new ProjectUsageSummary { ProjectId = "p1", TotalRequests = 2, TotalTokens = 400, TotalCostUsd = 1.25 });
    }

    [Fact]
    public async Task The_cost_tracker_enforces_a_daily_budget_from_the_stored_spend()
    {
        var repo = new StoreUsageRepository(UsageStore());
        var config = new Birko.AI.Resilience.Configuration.CostTrackingConfiguration
        {
            Enabled = true,
            Budget = new() { DailyBudgetUsd = 1.0, WarningThresholdPercent = 80 },
        };
        var tracker = new CostTrackingService(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<CostTrackingService>.Instance, repo);
        var now = DateTime.UtcNow;
        await repo.RecordUsageAsync(Usage("openai", 1.5, 10, now, null));

        var status = await tracker.CheckBudgetAsync();

        status.IsWithinBudget.Should().BeFalse();
        status.BudgetType.Should().Be("daily");
        status.CurrentSpend.Should().Be(1.5);
    }

    [Fact]
    public void A_store_that_cannot_aggregate_is_refused()
    {
        var act = () => new StoreUsageRepository(NonAggregating.Create());
        act.Should().Throw<ArgumentException>().WithMessage("*does not aggregate*");
    }

    [Fact]
    public void Null_arguments_are_refused()
    {
        ((Action)(() => new StoreUsageRepository(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new StoreCircuitBreakerStore(null!))).Should().Throw<ArgumentNullException>();
    }

    /// <summary>A bulk store and nothing else: a proxy implementing only <see cref="IAsyncBulkStore{T}"/>.</summary>
    public class NonAggregating : System.Reflection.DispatchProxy
    {
        public static IAsyncBulkStore<UsageRecordModel> Create() => Create<IAsyncBulkStore<UsageRecordModel>, NonAggregating>();
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException();
    }
}

public sealed class InMemoryResilienceStoreTests : ResilienceStoreTests
{
    protected override IAsyncBulkStore<CircuitBreakerStateModel> BreakerStore() => _breakers;
    protected override IAsyncBulkStore<UsageRecordModel> UsageStore() => _usage;

    private readonly AsyncInMemoryStore<CircuitBreakerStateModel> _breakers = new();
    private readonly AsyncInMemoryStore<UsageRecordModel> _usage = new();
}

public sealed class SqLiteResilienceStoreTests : ResilienceStoreTests, IDisposable
{
    private readonly string _dir;
    private readonly SqLiteSettings _settings;

    public SqLiteResilienceStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "birko_resilience_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _settings = new SqLiteSettings(_dir, "resilience.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* not the assertion */ }
    }

    // A new store object each call over the same file: a second process, as far as the store can tell.
    protected override IAsyncBulkStore<CircuitBreakerStateModel> BreakerStore() => Sql<CircuitBreakerStateModel>();
    protected override IAsyncBulkStore<UsageRecordModel> UsageStore() => Sql<UsageRecordModel>();

    private AsyncSQLiteStore<T> Sql<T>() where T : Birko.Data.Models.AbstractModel
    {
        var store = new AsyncSQLiteStore<T>();
        store.SetSettings(_settings);
        return store;
    }
}
