using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health;

/// <summary>
/// Runs registered health checks and produces an aggregated <see cref="HealthReport"/>.
/// </summary>
public sealed class HealthCheckRunner
{
    private readonly List<HealthCheckRegistration> _registrations = new();
    private readonly TimeSpan _defaultTimeout;

    /// <summary>
    /// Creates a runner with the specified default timeout per check.
    /// </summary>
    /// <param name="defaultTimeout">Default timeout. Defaults to 30 seconds.</param>
    public HealthCheckRunner(TimeSpan? defaultTimeout = null)
    {
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>Registers a health check.</summary>
    public HealthCheckRunner Register(HealthCheckRegistration registration)
    {
        if (registration == null)
        {
            throw new ArgumentNullException(nameof(registration));
        }
        _registrations.Add(registration);
        return this;
    }

    /// <summary>Convenience: register a check by name and instance.</summary>
    public HealthCheckRunner Register(string name, IHealthCheck check, params string[] tags)
    {
        return Register(new HealthCheckRegistration(name, check, tags));
    }

    /// <summary>Convenience: register a check by name and factory.</summary>
    public HealthCheckRunner Register(string name, Func<IHealthCheck> factory, params string[] tags)
    {
        return Register(new HealthCheckRegistration(name, factory, tags));
    }

    /// <summary>
    /// Runs all registered health checks (or filtered by tag) and returns an aggregated report.
    /// Checks run concurrently.
    /// </summary>
    /// <param name="tag">Optional tag filter. Only checks with this tag will run.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<HealthReport> RunAsync(string? tag = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var checks = tag == null
            ? _registrations
            : _registrations.Where(r => r.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList();

        var results = new ConcurrentDictionary<string, HealthCheckResult>();
        var tasks = checks.Select(reg => RunCheckAsync(reg, results, ct));
        await Task.WhenAll(tasks).ConfigureAwait(false);

        sw.Stop();
        return new HealthReport(results, sw.Elapsed);
    }

    /// <summary>Returns the list of registered checks.</summary>
    public IReadOnlyList<HealthCheckRegistration> Registrations => _registrations.AsReadOnly();

    private async Task RunCheckAsync(
        HealthCheckRegistration registration,
        ConcurrentDictionary<string, HealthCheckResult> results,
        CancellationToken ct)
    {
        var timeout = registration.Timeout ?? _defaultTimeout;
        var sw = Stopwatch.StartNew();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var check = registration.Factory();
            var result = await check.CheckAsync(cts.Token).ConfigureAwait(false);

            sw.Stop();
            results[registration.Name] = result.WithDuration(sw.Elapsed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            var status = registration.TimeoutStatus;
            var result = status == HealthStatus.Degraded
                ? HealthCheckResult.Degraded($"Timed out after {timeout.TotalSeconds:F1}s")
                : HealthCheckResult.Unhealthy($"Timed out after {timeout.TotalSeconds:F1}s");
            results[registration.Name] = result.WithDuration(sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            results[registration.Name] = HealthCheckResult.Unhealthy(ex.Message, ex).WithDuration(sw.Elapsed);
        }
    }
}
