using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.AI.Resilience.Data.Models;
using Birko.AI.Resilience.Stores;
using Birko.Data.Stores;

namespace Birko.AI.Resilience.Data.Stores
{
    /// <summary>
    /// <see cref="ICircuitBreakerStore"/> over any <see cref="IAsyncBulkStore{T}"/> (TASK-530): pass it to
    /// <c>ProviderCircuitBreaker</c> and call <c>LoadPersistedStateAsync</c> at startup, and an open circuit stays open
    /// across a restart.
    /// </summary>
    /// <remarks>
    /// A save is an upsert by a Guid derived from the provider name (<see cref="CircuitBreakerStateModel.GuidFor"/>),
    /// through <c>SaveAsync</c>, which creates an entity whose Guid is not stored under that Guid. Saves for one
    /// provider are already serialized by the breaker in one process; two processes saving a provider's first state at
    /// the same moment can collide on the key, and the loser's save throws (the breaker logs it).
    /// </remarks>
    public class StoreCircuitBreakerStore : ICircuitBreakerStore
    {
        private readonly IAsyncBulkStore<CircuitBreakerStateModel> _store;

        public StoreCircuitBreakerStore(IAsyncBulkStore<CircuitBreakerStateModel> store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public async Task<List<CircuitBreakerState>> LoadAllAsync(CancellationToken ct = default)
        {
            var rows = await _store.ReadAsync(ct: ct).ConfigureAwait(false);
            return rows.Select(r => r.ToState()).ToList();
        }

        public Task SaveAsync(CircuitBreakerState state, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(state);
            if (string.IsNullOrWhiteSpace(state.Provider))
                throw new ArgumentException("A circuit breaker state needs a provider.", nameof(state));
            return _store.SaveAsync(CircuitBreakerStateModel.FromState(state), ct: ct);
        }
    }
}
