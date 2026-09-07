using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SchemaDrift;

namespace Birko.Health.Data.SQL;

/// <summary>
/// Reports whether the database's columns are still the ones the models declare, and surfaces any index
/// this connector failed to build.
/// </summary>
/// <remarks>
/// <para>
/// TASK-269. The framework does <b>not</b> repair an existing database — <c>CREATE TABLE</c> is guarded
/// by <c>IF NOT EXISTS</c> and schema-ensure never reconciles an existing table's columns — so a model
/// change, or an upgrade past one of the column-typing fixes (TASK-257, TASK-264, TASK-265, TASK-266,
/// TASK-275), leaves the old column in place. Until this existed, the only signal was an exception at
/// the call site, on whichever request happened to touch the column first.
/// </para>
/// <para>
/// ⚠ <b>It reports <see cref="AbstractConnector.IndexCreationFailures"/> too, and that is the point
/// rather than a convenience.</b> TASK-204 made schema-ensure record an unbuildable index instead of
/// taking the entity's whole surface down — and measured across all 16 consumer repos on 2026-09-07,
/// <c>OnIndexCreationFailed +=</c> has <b>zero</b> subscribers, so every index failure since has been
/// silent, and TASK-245, TASK-248 and TASK-257 each found real long-standing ones hiding behind it.
/// Shipping a second drift channel while leaving the first unread would repeat that defect inside the
/// change that exists to close it. One door for "is my schema what my models think it is?".
/// </para>
/// <para>
/// <b>Degraded, never Unhealthy.</b> Drift means the database disagrees with the models, not that it is
/// unreachable — the application is almost certainly still serving, and most drift affects one column of
/// one entity. Reporting it Unhealthy would pull an instance out of a load balancer for a condition a
/// human needs to read and act on, which is how a diagnostic becomes an outage. Reachability is
/// <c>SqlHealthCheck</c>'s question, and it is deliberately a different check.
/// </para>
/// </remarks>
public sealed class SchemaDriftHealthCheck : IHealthCheck
{
    private readonly Func<AbstractConnector> _connector;
    private readonly IReadOnlyList<Type> _entityTypes;

    /// <summary>
    /// Creates the check.
    /// </summary>
    /// <param name="connector">
    /// Supplies the connector to interrogate. A factory rather than an instance, because connectors are
    /// cached process-wide per (type, settings id) and a host resolves its own lazily — capturing one at
    /// registration time is the shape § Conventions records as putting per-caller state on a shared
    /// object.
    /// </param>
    /// <param name="entityTypes">
    /// The entity types to compare. Explicit rather than found by assembly scanning: a host knows which
    /// of its types are actually stored, and scanning would report every view model and DTO as a table
    /// that does not exist.
    /// </param>
    public SchemaDriftHealthCheck(Func<AbstractConnector> connector, IEnumerable<Type> entityTypes)
    {
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        _entityTypes = (entityTypes ?? throw new ArgumentNullException(nameof(entityTypes))).ToList();
    }

    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var connector = _connector();
            if (connector == null)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Schema drift check has no connector: its factory returned null."));
            }

            var reports = new List<SchemaDriftReport>();
            foreach (var type in _entityTypes)
            {
                ct.ThrowIfCancellationRequested();
                reports.Add(connector.DetectDrift(type));
            }

            var drifts = reports.SelectMany(r => r.Drifts).ToList();
            var indexFailures = connector.IndexCreationFailures;

            // An unsupported provider is NOT a clean bill of health -- see SchemaDriftReport.Supported.
            // Reporting Healthy for a question that was never answered is the silence TASK-204's channel
            // already demonstrates, where "nobody reported anything" and "nothing is wrong" were
            // indistinguishable for the life of the framework.
            var unsupported = reports.Where(r => !r.Supported).ToList();
            var absent = reports.Where(r => r.Supported && !r.TableExists).ToList();

            var data = new Dictionary<string, object>
            {
                ["checked"] = reports.Count,
                ["drifted"] = drifts.Count,
                ["indexFailures"] = indexFailures.Count,
                ["tablesNotYetCreated"] = absent.Count,
                ["unsupported"] = unsupported.Count,
            };

            if (drifts.Count > 0)
            {
                data["drift"] = drifts.Select(d => d.ToString()).ToList();
            }
            if (indexFailures.Count > 0)
            {
                data["indexFailure"] = indexFailures
                    .Select(f => $"{f.TableName}.{f.IndexName}: {f.Error.Message}")
                    .ToList();
            }

            if (drifts.Count > 0 || indexFailures.Count > 0)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Schema disagrees with the models: {drifts.Count} column(s) drifted, " +
                    $"{indexFailures.Count} index(es) not built.",
                    null,
                    data));
            }

            if (unsupported.Count > 0)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Schema drift could not be determined for {unsupported.Count} of {reports.Count} " +
                    "type(s): this provider exposes no column catalogue this framework knows how to read.",
                    null,
                    data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Schema matches the models ({reports.Count} type(s) checked).", data));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Schema drift check failed: {ex.Message}", ex));
        }
    }
}
