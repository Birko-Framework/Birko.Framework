namespace Birko.AI.Resilience.Stores
{
    /// <summary>
    /// Abstraction for persisting circuit breaker state. <c>Birko.AI.Resilience.Data</c> implements it over any
    /// <c>IAsyncBulkStore</c> (TASK-530).
    /// </summary>
    /// <remarks>
    /// <see cref="SaveAsync"/> keeps one state per <see cref="CircuitBreakerState.Provider"/>: a second save for the same
    /// provider replaces the first.
    /// </remarks>
    public interface ICircuitBreakerStore
    {
        Task<List<CircuitBreakerState>> LoadAllAsync(CancellationToken ct = default);
        Task SaveAsync(CircuitBreakerState state, CancellationToken ct = default);
    }

    public class CircuitBreakerState
    {
        public string Provider { get; set; } = "";
        public int State { get; set; }
        public int ConsecutiveFailures { get; set; }
        public DateTime? OpenedAt { get; set; }
        public DateTime? LastFailureAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
