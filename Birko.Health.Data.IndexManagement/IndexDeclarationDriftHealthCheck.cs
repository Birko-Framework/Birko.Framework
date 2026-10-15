using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Patterns.IndexManagement;

namespace Birko.Health.Data.IndexManagement;

/// <summary>
/// Reports whether the indexes a host declares are present, and shaped as declared, on a store reached through an
/// <see cref="IIndexManager"/>: MongoDB, RavenDB, Cosmos DB (TASK-635).
/// </summary>
/// <remarks>
/// <para>
/// SQL checks its model-declared indexes in <c>SchemaDriftHealthCheck</c> (TASK-622); the document stores declare none,
/// so their declared side is the <see cref="IndexDefinition"/> list a host creates them from, compared by
/// <see cref="IndexDeclarationDriftDetector"/>. A missing unique index is a correctness failure there, not only a slow
/// query: duplicates are accepted silently.
/// </para>
/// <para>
/// <b>Degraded, never Unhealthy</b>, and <b>"could not compare" is never Healthy</b>: the contract of the SQL and
/// Elasticsearch drift checks. Drift needs a human; Unhealthy would take a serving instance out of a load balancer.
/// </para>
/// </remarks>
public sealed class IndexDeclarationDriftHealthCheck : IHealthCheck
{
    private readonly IReadOnlyList<Func<CancellationToken, Task<IndexDeclarationReport>>> _probes;

    /// <summary>Creates the check.</summary>
    /// <param name="probes">
    /// One per scope to check, typically
    /// <c>ct =&gt; IndexDeclarationDriftDetector.DetectAsync(manager, declared, "Orders", ct)</c>.
    /// </param>
    public IndexDeclarationDriftHealthCheck(IEnumerable<Func<CancellationToken, Task<IndexDeclarationReport>>> probes)
    {
        _probes = (probes ?? throw new ArgumentNullException(nameof(probes))).ToList();
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var reports = new List<IndexDeclarationReport>();
            foreach (var probe in _probes)
            {
                ct.ThrowIfCancellationRequested();
                reports.Add(await probe(ct).ConfigureAwait(false));
            }

            var declared = reports.Sum(r => r.Declared);
            var drifts = reports.SelectMany(r => r.Drifts).Select(d => d.ToString()).ToList();
            var unverified = reports.SelectMany(r => r.Unverified).Select(u => u.ToString()).ToList();
            var data = new Dictionary<string, object>
            {
                ["scopes"] = reports.Count,
                ["declared"] = declared,
                ["drifted"] = drifts.Count,
                ["unverified"] = unverified.Count,
            };
            if (drifts.Count > 0) data["drift"] = drifts;
            if (unverified.Count > 0) data["notCompared"] = unverified;

            if (drifts.Count > 0)
            {
                return HealthCheckResult.Degraded(
                    $"Indexes disagree with their declarations: {drifts.Count} drift(s) across {reports.Count} scope(s).", null, data);
            }
            if (unverified.Count > 0)
            {
                return HealthCheckResult.Degraded(
                    $"{unverified.Count} of {declared} declared index(es) could not be compared: " + string.Join("; ", unverified), null, data);
            }
            return HealthCheckResult.Healthy($"Indexes match their declarations ({declared} index(es) in {reports.Count} scope(s)).", data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Index drift check failed: {ex.Message}", ex);
        }
    }
}
