using System.Collections.Concurrent;
using Birko.AI.Resilience.Stores;
using Microsoft.Extensions.Logging;

namespace Birko.AI.Resilience.Services
{
    /// <summary>
    /// Implements circuit breaker pattern to prevent retrying when a provider is experiencing widespread failures.
    /// Optionally persists state via ICircuitBreakerStore.
    /// </summary>
    public class ProviderCircuitBreaker
    {
        public enum CircuitState
        {
            Closed,
            Open,
            HalfOpen
        }

        private class ProviderCircuit
        {
            public CircuitState State { get; set; } = CircuitState.Closed;
            public int ConsecutiveFailures { get; set; } = 0;
            public DateTime? OpenedAt { get; set; }
            public DateTime? LastFailureAt { get; set; }
        }

        private readonly ConcurrentDictionary<string, ProviderCircuit> _circuits = new();
        private readonly int _failureThreshold;
        private readonly TimeSpan _openDuration;
        private readonly TimeSpan _resetAfterSuccess;
        private readonly ICircuitBreakerStore? _store;
        private readonly ILogger? _logger;

        // Per-provider persistence serialization: each provider's SaveAsync calls run one after
        // another in submission order, so a stale snapshot can never overwrite a newer one (CR-M013).
        private readonly object _persistLock = new();
        private readonly Dictionary<string, Task> _persistTails = new();

        public ProviderCircuitBreaker(
            int failureThreshold = 3,
            TimeSpan? openDuration = null,
            TimeSpan? resetAfterSuccess = null,
            ICircuitBreakerStore? store = null,
            ILogger? logger = null)
        {
            _failureThreshold = failureThreshold;
            _openDuration = openDuration ?? TimeSpan.FromMinutes(10);
            _resetAfterSuccess = resetAfterSuccess ?? TimeSpan.FromMinutes(5);
            _store = store;
            _logger = logger;
        }

        /// <summary>
        /// Loads persisted state from the store. Call once at startup.
        /// </summary>
        public async Task LoadPersistedStateAsync()
        {
            if (_store == null) return;

            var states = await _store.LoadAllAsync();
            foreach (var s in states)
            {
                var circuit = _circuits.GetOrAdd(s.Provider, _ => new ProviderCircuit());
                lock (circuit)
                {
                    circuit.State = (CircuitState)s.State;
                    circuit.ConsecutiveFailures = s.ConsecutiveFailures;
                    circuit.OpenedAt = s.OpenedAt;
                    circuit.LastFailureAt = s.LastFailureAt;
                }
            }

            _logger?.LogInformation("Loaded circuit breaker state for {Count} provider(s)", states.Count);
        }

        public void RecordFailure(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider)) return;

            var key = provider.ToLowerInvariant();
            var circuit = _circuits.GetOrAdd(key, _ => new ProviderCircuit());

            lock (circuit)
            {
                circuit.ConsecutiveFailures++;
                circuit.LastFailureAt = DateTime.UtcNow;

                if (circuit.State == CircuitState.Closed && circuit.ConsecutiveFailures >= _failureThreshold)
                {
                    circuit.State = CircuitState.Open;
                    circuit.OpenedAt = DateTime.UtcNow;
                }
                else if (circuit.State == CircuitState.HalfOpen)
                {
                    circuit.State = CircuitState.Open;
                    circuit.OpenedAt = DateTime.UtcNow;
                }
            }

            PersistState(key, circuit);
        }

        public void RecordSuccess(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider)) return;

            var key = provider.ToLowerInvariant();
            var circuit = _circuits.GetOrAdd(key, _ => new ProviderCircuit());

            lock (circuit)
            {
                circuit.ConsecutiveFailures = 0;
                circuit.State = CircuitState.Closed;
                circuit.OpenedAt = null;
            }

            PersistState(key, circuit);
        }

        public bool CanRetry(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider)) return true;

            var circuit = _circuits.GetOrAdd(provider.ToLowerInvariant(), _ => new ProviderCircuit());

            lock (circuit)
            {
                var now = DateTime.UtcNow;

                if (circuit.State == CircuitState.Open &&
                    circuit.OpenedAt.HasValue &&
                    now - circuit.OpenedAt.Value >= _openDuration)
                {
                    circuit.State = CircuitState.HalfOpen;
                }

                if (circuit.State == CircuitState.Closed &&
                    circuit.LastFailureAt.HasValue &&
                    now - circuit.LastFailureAt.Value >= _resetAfterSuccess)
                {
                    circuit.ConsecutiveFailures = 0;
                }

                return circuit.State != CircuitState.Open;
            }
        }

        public CircuitState GetState(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider)) return CircuitState.Closed;

            if (_circuits.TryGetValue(provider.ToLowerInvariant(), out var circuit))
            {
                lock (circuit) { return circuit.State; }
            }

            return CircuitState.Closed;
        }

        public Dictionary<string, (CircuitState State, int Failures, DateTime? LastFailure)> GetAllStates()
        {
            var result = new Dictionary<string, (CircuitState, int, DateTime?)>();

            foreach (var kvp in _circuits)
            {
                lock (kvp.Value)
                {
                    result[kvp.Key] = (kvp.Value.State, kvp.Value.ConsecutiveFailures, kvp.Value.LastFailureAt);
                }
            }

            return result;
        }

        public void Reset(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider)) return;

            if (_circuits.TryGetValue(provider.ToLowerInvariant(), out var circuit))
            {
                lock (circuit)
                {
                    circuit.State = CircuitState.Closed;
                    circuit.ConsecutiveFailures = 0;
                    circuit.OpenedAt = null;
                    circuit.LastFailureAt = null;
                }
            }
        }

        public void ResetAll()
        {
            _circuits.Clear();
        }

        private void PersistState(string provider, ProviderCircuit circuit)
        {
            if (_store == null) return;

            // Snapshot synchronously (stamping UpdatedAt now) so the persisted order matches the
            // order in which state actually changed — not the order background tasks happen to run.
            CircuitBreakerState state;
            lock (circuit)
            {
                state = new CircuitBreakerState
                {
                    Provider = provider,
                    State = (int)circuit.State,
                    ConsecutiveFailures = circuit.ConsecutiveFailures,
                    OpenedAt = circuit.OpenedAt,
                    LastFailureAt = circuit.LastFailureAt,
                    UpdatedAt = DateTime.UtcNow
                };
            }

            // Chain this save after the provider's previous save so writes never race/reorder.
            lock (_persistLock)
            {
                var previous = _persistTails.TryGetValue(provider, out var tail) ? tail : Task.CompletedTask;
                var next = previous.ContinueWith(async _ =>
                {
                    try
                    {
                        await _store.SaveAsync(state);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to persist circuit breaker state for {Provider}", provider);
                    }
                }, TaskScheduler.Default).Unwrap();
                _persistTails[provider] = next;
            }
        }

        /// <summary>
        /// Awaits all in-flight persistence writes. Call before shutdown (or in tests) to ensure the
        /// store has durably received the latest circuit state.
        /// </summary>
        public Task FlushPersistenceAsync()
        {
            Task[] tails;
            lock (_persistLock)
            {
                tails = _persistTails.Values.ToArray();
            }
            return Task.WhenAll(tails);
        }
    }
}
