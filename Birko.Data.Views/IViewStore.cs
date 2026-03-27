using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;

namespace Birko.Data.Views;

/// <summary>
/// Read-only store for querying views. Views are not writable.
/// Follows the IAsyncBulkReadStore signature pattern from Birko.Data.Stores.
/// </summary>
public interface IViewStore<TView> where TView : class
{
    /// <summary>Queries the view with optional filter, ordering, and pagination.</summary>
    Task<IEnumerable<TView>> QueryAsync(
        Expression<Func<TView, bool>>? filter = null,
        OrderBy<TView>? orderBy = null,
        int? limit = null,
        int? offset = null,
        CancellationToken ct = default);

    /// <summary>Returns the first result matching the filter, or null.</summary>
    Task<TView?> QueryFirstAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default);

    /// <summary>Counts results matching the filter.</summary>
    Task<long> CountAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default);
}
