using System;
using System.Collections.Generic;

namespace Birko.Health;

/// <summary>
/// Registration of a named health check with optional tags and timeout.
/// </summary>
public sealed class HealthCheckRegistration
{
    /// <summary>Unique name for this health check (e.g., "sql-primary", "redis", "disk").</summary>
    public string Name { get; }

    /// <summary>Factory that creates the health check instance.</summary>
    public Func<IHealthCheck> Factory { get; }

    /// <summary>Tags for filtering (e.g., "db", "cache", "system", "ready", "live").</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>Timeout for this check. Null means use runner default.</summary>
    public TimeSpan? Timeout { get; }

    /// <summary>The status to report when the check times out. Defaults to Unhealthy.</summary>
    public HealthStatus TimeoutStatus { get; }

    public HealthCheckRegistration(
        string name,
        Func<IHealthCheck> factory,
        IReadOnlyList<string>? tags = null,
        TimeSpan? timeout = null,
        HealthStatus timeoutStatus = HealthStatus.Unhealthy)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Health check name cannot be null or empty.", nameof(name));
        }

        Name = name;
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Tags = tags ?? Array.Empty<string>();
        Timeout = timeout;
        TimeoutStatus = timeoutStatus;
    }

    /// <summary>Convenience: register a singleton instance.</summary>
    public HealthCheckRegistration(
        string name,
        IHealthCheck instance,
        IReadOnlyList<string>? tags = null,
        TimeSpan? timeout = null,
        HealthStatus timeoutStatus = HealthStatus.Unhealthy)
        : this(name, () => instance, tags, timeout, timeoutStatus)
    {
        if (instance == null)
        {
            throw new ArgumentNullException(nameof(instance));
        }
    }
}
