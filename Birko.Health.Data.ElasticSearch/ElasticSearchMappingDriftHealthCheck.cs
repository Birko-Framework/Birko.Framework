using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;

namespace Birko.Health.Data.ElasticSearch;

/// <summary>
/// Reports whether each store's Elasticsearch index is still mapped the way the store declares it (TASK-624).
/// </summary>
/// <remarks>
/// <para>
/// An index is mapped once and never revisited, so a model changed afterwards runs its queries against the old mapping
/// and silently matches nothing (TASK-613 was that). <c>DetectMappingDriftAsync</c> (TASK-621) answers the question on
/// demand; this check is what asks it.
/// </para>
/// <para>
/// <b>Degraded, never Unhealthy</b>, and <b>"could not determine" is never Healthy</b>: the same contract as
/// <c>SchemaDriftHealthCheck</c>. Drift is a disagreement a human must read, not unreachability; a store with no client
/// is a question that was never answered. An index that does not exist yet is expected (stores create it on first use),
/// so it is Healthy, but the description says it was not checked.
/// </para>
/// </remarks>
public sealed class ElasticSearchMappingDriftHealthCheck : IHealthCheck
{
    private readonly IReadOnlyList<Func<CancellationToken, Task<MappingDriftReport>>> _probes;

    /// <summary>Creates the check.</summary>
    /// <param name="probes">
    /// One per store to check, typically <c>ct =&gt; store.DetectMappingDriftAsync(ct)</c>. Delegates rather than
    /// stores, because Elasticsearch stores are generic per entity and a host checks several.
    /// </param>
    public ElasticSearchMappingDriftHealthCheck(IEnumerable<Func<CancellationToken, Task<MappingDriftReport>>> probes)
    {
        _probes = (probes ?? throw new ArgumentNullException(nameof(probes))).ToList();
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var reports = new List<MappingDriftReport>();
            foreach (var probe in _probes)
            {
                ct.ThrowIfCancellationRequested();
                reports.Add(await probe(ct).ConfigureAwait(false));
            }

            var drifts = reports.SelectMany(r => r.Drifts).Select(d => d.ToString()).ToList();
            var unsupported = reports.Where(r => !r.Supported).ToList();
            var absent = reports.Where(r => r.Supported && !r.IndexExists).ToList();
            var data = new Dictionary<string, object>
            {
                ["checked"] = reports.Count,
                ["drifted"] = drifts.Count,
                ["indexesNotYetCreated"] = absent.Count,
                ["unsupported"] = unsupported.Count,
            };
            if (drifts.Count > 0)
            {
                data["drift"] = drifts;
            }

            if (drifts.Count > 0)
            {
                return HealthCheckResult.Degraded(
                    $"Index mappings disagree with the stores: {drifts.Count} field(s) drifted.", null, data);
            }
            if (unsupported.Count > 0)
            {
                return HealthCheckResult.Degraded(
                    $"Mapping drift could not be determined for {unsupported.Count} of {reports.Count} store(s): "
                    + string.Join("; ", unsupported.Select(r => r.Reason ?? r.Index)), null, data);
            }
            if (absent.Count > 0)
            {
                return HealthCheckResult.Healthy(
                    $"Index mappings match for {reports.Count - absent.Count} of {reports.Count} store(s); "
                    + $"{absent.Count} index(es) not created yet, so they were not checked.", data);
            }
            return HealthCheckResult.Healthy($"Index mappings match the stores ({reports.Count} store(s) checked).", data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Mapping drift check failed: {ex.Message}", ex);
        }
    }
}
