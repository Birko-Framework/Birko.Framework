using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health;

/// <summary>
/// Represents a health check for a single component.
/// </summary>
public interface IHealthCheck
{
    /// <summary>
    /// Runs the health check and returns the result.
    /// </summary>
    Task<HealthCheckResult> CheckAsync(CancellationToken ct = default);
}
