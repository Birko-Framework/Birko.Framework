using System;
using System.Collections.Generic;

namespace Birko.Health;

/// <summary>
/// The result of a health check.
/// </summary>
public readonly struct HealthCheckResult
{
    /// <summary>The health status.</summary>
    public HealthStatus Status { get; }

    /// <summary>Optional human-readable description.</summary>
    public string? Description { get; }

    /// <summary>Optional exception that caused an unhealthy status.</summary>
    public Exception? Exception { get; }

    /// <summary>Optional key-value data associated with the check.</summary>
    public IReadOnlyDictionary<string, object>? Data { get; }

    /// <summary>Duration of the health check execution.</summary>
    public TimeSpan Duration { get; }

    private HealthCheckResult(HealthStatus status, string? description, Exception? exception, IReadOnlyDictionary<string, object>? data, TimeSpan duration)
    {
        Status = status;
        Description = description;
        Exception = exception;
        Data = data;
        Duration = duration;
    }

    /// <summary>Creates a healthy result.</summary>
    public static HealthCheckResult Healthy(string? description = null, IReadOnlyDictionary<string, object>? data = null)
        => new(HealthStatus.Healthy, description, null, data, TimeSpan.Zero);

    /// <summary>Creates a degraded result.</summary>
    public static HealthCheckResult Degraded(string description, Exception? exception = null, IReadOnlyDictionary<string, object>? data = null)
        => new(HealthStatus.Degraded, description, exception, data, TimeSpan.Zero);

    /// <summary>Creates an unhealthy result.</summary>
    public static HealthCheckResult Unhealthy(string description, Exception? exception = null, IReadOnlyDictionary<string, object>? data = null)
        => new(HealthStatus.Unhealthy, description, exception, data, TimeSpan.Zero);

    /// <summary>Returns a copy with the duration set.</summary>
    internal HealthCheckResult WithDuration(TimeSpan duration)
        => new(Status, Description, Exception, Data, duration);
}
