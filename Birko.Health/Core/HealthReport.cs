using System;
using System.Collections.Generic;

namespace Birko.Health;

/// <summary>
/// Aggregated result of running multiple health checks.
/// </summary>
public sealed class HealthReport
{
    /// <summary>Overall status (worst status of all entries).</summary>
    public HealthStatus Status { get; }

    /// <summary>Total duration of the health check run.</summary>
    public TimeSpan TotalDuration { get; }

    /// <summary>Individual check results keyed by registration name.</summary>
    public IReadOnlyDictionary<string, HealthCheckResult> Entries { get; }

    public HealthReport(IReadOnlyDictionary<string, HealthCheckResult> entries, TimeSpan totalDuration)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        TotalDuration = totalDuration;

        var worst = HealthStatus.Healthy;
        foreach (var entry in entries.Values)
        {
            if (entry.Status > worst)
            {
                worst = entry.Status;
            }
        }
        Status = worst;
    }
}
