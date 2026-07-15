using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Checks;

/// <summary>
/// Health check that monitors process memory usage.
/// </summary>
public sealed class MemoryHealthCheck : IHealthCheck
{
    private readonly long _warningThresholdBytes;
    private readonly long _criticalThresholdBytes;

    /// <summary>
    /// Creates a memory health check.
    /// </summary>
    /// <param name="warningThresholdMb">Working set above this triggers Degraded. Default: 1024 MB.</param>
    /// <param name="criticalThresholdMb">Working set above this triggers Unhealthy. Default: 2048 MB.</param>
    public MemoryHealthCheck(long warningThresholdMb = 1024, long criticalThresholdMb = 2048)
    {
        // CR-L262: critical (Unhealthy) must trigger at higher usage than warning (Degraded); if
        // critical <= warning the Degraded tier can never be reached.
        if (criticalThresholdMb <= warningThresholdMb)
        {
            throw new ArgumentException(
                $"Critical threshold ({criticalThresholdMb} MB) must be greater than the warning threshold ({warningThresholdMb} MB).",
                nameof(criticalThresholdMb));
        }

        _warningThresholdBytes = warningThresholdMb * 1024 * 1024;
        _criticalThresholdBytes = criticalThresholdMb * 1024 * 1024;
    }

    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            var workingSet = Environment.WorkingSet;
            var workingSetMb = workingSet / (1024 * 1024);
            var gcHeapMb = GC.GetTotalMemory(false) / (1024 * 1024);
            var totalAvailableMb = gcInfo.TotalAvailableMemoryBytes / (1024 * 1024);

            var data = new Dictionary<string, object>
            {
                ["workingSetMb"] = workingSetMb,
                ["gcHeapMb"] = gcHeapMb,
                ["totalAvailableMemoryMb"] = totalAvailableMb,
                ["gen0Collections"] = GC.CollectionCount(0),
                ["gen1Collections"] = GC.CollectionCount(1),
                ["gen2Collections"] = GC.CollectionCount(2)
            };

            if (workingSet > _criticalThresholdBytes)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Critical memory usage: {workingSetMb} MB working set.", data: data));
            }

            if (workingSet > _warningThresholdBytes)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"High memory usage: {workingSetMb} MB working set.", data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"{workingSetMb} MB working set, {gcHeapMb} MB GC heap.", data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy($"Failed to check memory: {ex.Message}", ex));
        }
    }
}
