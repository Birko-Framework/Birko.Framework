using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Views;

/// <summary>
/// Manages persistent view lifecycle. Follows the IIndexManager pattern from Birko.Data.Patterns.
/// </summary>
public interface IViewManager
{
    /// <summary>Creates or updates the persistent view. No-op for OnTheFly views.</summary>
    Task EnsureAsync(ViewDefinition definition, CancellationToken ct = default);

    /// <summary>Drops a persistent view by name.</summary>
    Task DropAsync(string viewName, CancellationToken ct = default);

    /// <summary>Checks if a persistent view exists.</summary>
    Task<bool> ExistsAsync(string viewName, CancellationToken ct = default);

    /// <summary>
    /// Refreshes a persistent/materialized view (e.g., REFRESH MATERIALIZED VIEW for PostgreSQL).
    /// No-op on platforms that auto-maintain views.
    /// </summary>
    Task RefreshAsync(string viewName, CancellationToken ct = default);
}
