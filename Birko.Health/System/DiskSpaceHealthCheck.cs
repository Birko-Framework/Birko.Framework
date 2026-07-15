using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Checks;

/// <summary>
/// Health check that monitors available disk space on a specified drive.
/// </summary>
public sealed class DiskSpaceHealthCheck : IHealthCheck
{
    private readonly string _drivePath;
    private readonly long _warningThresholdBytes;
    private readonly long _criticalThresholdBytes;

    /// <summary>
    /// Creates a disk space health check.
    /// </summary>
    /// <param name="drivePath">Drive or path to check (e.g., "C:\", "/").</param>
    /// <param name="warningThresholdMb">Free space below this triggers Degraded. Default: 1024 MB.</param>
    /// <param name="criticalThresholdMb">Free space below this triggers Unhealthy. Default: 256 MB.</param>
    public DiskSpaceHealthCheck(string drivePath, long warningThresholdMb = 1024, long criticalThresholdMb = 256)
    {
        if (string.IsNullOrWhiteSpace(drivePath))
        {
            throw new ArgumentException("Drive path cannot be null or empty.", nameof(drivePath));
        }

        // CR-L262: critical (Unhealthy) must trigger at less free space than warning (Degraded); if
        // critical >= warning the Degraded tier can never be reached. Guard it like the drivePath check.
        if (criticalThresholdMb >= warningThresholdMb)
        {
            throw new ArgumentException(
                $"Critical threshold ({criticalThresholdMb} MB) must be less than the warning threshold ({warningThresholdMb} MB).",
                nameof(criticalThresholdMb));
        }

        _drivePath = drivePath;
        _warningThresholdBytes = warningThresholdMb * 1024 * 1024;
        _criticalThresholdBytes = criticalThresholdMb * 1024 * 1024;
    }

    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            // CR-L261: GetPathRoot returns null/empty for a rootless path (e.g. a bare relative path);
            // report a clear Unhealthy instead of masking the null with `!` and letting new DriveInfo throw.
            var root = Path.GetPathRoot(_drivePath);
            if (string.IsNullOrEmpty(root))
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Invalid drive path: '{_drivePath}' has no root."));
            }

            var driveInfo = new DriveInfo(root);

            if (!driveInfo.IsReady)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy($"Drive {driveInfo.Name} is not ready."));
            }

            var freeBytes = driveInfo.AvailableFreeSpace;
            var totalBytes = driveInfo.TotalSize;
            var freeMb = freeBytes / (1024 * 1024);
            var freePercent = totalBytes > 0 ? (double)freeBytes / totalBytes * 100 : 0;

            var data = new Dictionary<string, object>
            {
                ["drive"] = driveInfo.Name,
                ["freeSpaceMb"] = freeMb,
                ["totalSpaceMb"] = totalBytes / (1024 * 1024),
                ["freePercent"] = Math.Round(freePercent, 1)
            };

            if (freeBytes < _criticalThresholdBytes)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Critically low disk space: {freeMb} MB free ({freePercent:F1}%).", data: data));
            }

            if (freeBytes < _warningThresholdBytes)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Low disk space: {freeMb} MB free ({freePercent:F1}%).", data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"{freeMb} MB free ({freePercent:F1}%).", data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy($"Failed to check disk space: {ex.Message}", ex));
        }
    }
}
