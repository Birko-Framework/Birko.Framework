using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.Grants;
using Birko.Security.Grants.Services;
using Birko.Security.Grants.Stores;

namespace Birko.Health.Security.Grants;

/// <summary>
/// Reports the stored resource grants whose role or resource type the <see cref="GrantRoleTable"/> no longer declares
/// (TASK-654), through <see cref="StaleGrantDetector.FindAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Since TASK-641 a stale grant grants nothing instead of making every check of it throw. That is the safe answer, and a
/// quiet one: a role renamed in configuration silently removes everyone's access through the old name. This check is
/// where it becomes visible. It reports; a person migrates or revokes. The reserved admin grant is never reported.
/// </para>
/// <para>
/// <b>Degraded, never Unhealthy, and "could not read" is never Healthy</b>: the contract of the schema and index drift
/// checks. A stale grant needs a human, and Unhealthy would take a serving instance out of a load balancer for it.
/// </para>
/// </remarks>
public sealed class StaleGrantHealthCheck : IHealthCheck
{
    /// <summary>The most stale grants described in <c>Data["staleGrants"]</c>; <c>Data["stale"]</c> is always the full count.</summary>
    public const int MaxListed = 50;

    private readonly IResourceGrantStore _store;
    private readonly GrantRoleTable _table;

    /// <summary>Creates the check over the grant store and the role table the host's grant service uses.</summary>
    public StaleGrantHealthCheck(IResourceGrantStore store, GrantRoleTable table)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _table = table ?? throw new ArgumentNullException(nameof(table));
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        IReadOnlyList<StaleGrant> stale;
        try
        {
            stale = await StaleGrantDetector.FindAsync(_store, _table, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Stored grants could not be checked: {ex.Message}", ex);
        }

        if (stale.Count == 0)
        {
            return HealthCheckResult.Healthy("Every stored grant names a declared role and resource type.",
                new Dictionary<string, object> { ["stale"] = 0 });
        }

        var data = new Dictionary<string, object>
        {
            ["stale"] = stale.Count,
            ["roleNotDeclared"] = stale.Count(s => s.Reason == StaleGrantReason.RoleNotDeclared),
            ["resourceTypeNotDeclared"] = stale.Count(s => s.Reason == StaleGrantReason.ResourceTypeNotDeclared),
            ["staleGrants"] = stale.Take(MaxListed).Select(s => s.ToString()).ToList(),
        };
        var roles = string.Join(", ", stale.Where(s => s.Reason == StaleGrantReason.RoleNotDeclared)
            .Select(s => $"'{s.Grant.Role}'").Distinct());
        return HealthCheckResult.Degraded(
            $"{stale.Count} stored grant(s) grant nothing: their role or resource type is not declared"
            + (roles.Length == 0 ? "." : $" (undeclared roles: {roles}).")
            + " Migrate or revoke them.",
            null, data);
    }
}
