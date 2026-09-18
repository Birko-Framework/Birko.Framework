using Birko.AI.Resilience.Services;
using Birko.AI.Resilience.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Resilience.Tests;

/// <summary>
/// Regression for CR-M013: PersistState launched an unawaited Task.Run per state change, so two
/// rapid transitions could have their SaveAsync calls complete out of order — a stale Open landing
/// after a newer Closed. Persistence is now serialized per provider and awaitable via
/// FlushPersistenceAsync, so the store always ends with the newest state.
/// </summary>
public class ProviderCircuitBreakerPersistenceTests
{
    private sealed class RecordingStore : ICircuitBreakerStore
    {
        private readonly object _lock = new();
        public readonly List<CircuitBreakerState> Saved = new();

        public Task<List<CircuitBreakerState>> LoadAllAsync() => Task.FromResult(new List<CircuitBreakerState>());

        public async Task SaveAsync(CircuitBreakerState state)
        {
            // Make the earlier (Open) write slower than the later (Closed) write. If writes were not
            // serialized, the stale Open would be recorded last; serialization keeps submission order.
            await Task.Delay(state.State == (int)ProviderCircuitBreaker.CircuitState.Open ? 60 : 1);
            lock (_lock)
            {
                Saved.Add(state);
            }
        }
    }

    [Fact]
    public async Task Persistence_KeepsSubmissionOrder_NewestStateLast()
    {
        var store = new RecordingStore();
        var breaker = new ProviderCircuitBreaker(failureThreshold: 1, store: store);

        breaker.RecordFailure("p"); // Closed -> Open   (persist #1, slow)
        breaker.RecordSuccess("p"); // Open   -> Closed (persist #2, fast)

        await breaker.FlushPersistenceAsync();

        store.Saved.Should().HaveCount(2);
        store.Saved[0].State.Should().Be((int)ProviderCircuitBreaker.CircuitState.Open);
        store.Saved[^1].State.Should().Be((int)ProviderCircuitBreaker.CircuitState.Closed,
            "the newest state must be persisted last, never overwritten by a stale in-flight write");
    }

    [Fact]
    public async Task FlushPersistence_NoStore_DoesNotThrow()
    {
        var breaker = new ProviderCircuitBreaker(failureThreshold: 1);
        breaker.RecordFailure("p");

        var act = async () => await breaker.FlushPersistenceAsync();
        await act.Should().NotThrowAsync();
    }
}
