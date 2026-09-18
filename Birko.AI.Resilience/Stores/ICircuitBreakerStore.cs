namespace Birko.AI.Resilience.Stores
{
    /// <summary>
    /// Abstraction for persisting circuit breaker state. Implement per platform if persistence is needed.
    /// </summary>
    public interface ICircuitBreakerStore
    {
        Task<List<CircuitBreakerState>> LoadAllAsync();
        Task SaveAsync(CircuitBreakerState state);
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
