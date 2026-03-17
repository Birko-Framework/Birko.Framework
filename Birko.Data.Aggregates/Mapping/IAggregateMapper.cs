using Birko.Data.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Aggregates.Mapping;

/// <summary>
/// Maps between flat relational data and nested aggregate documents.
/// </summary>
public interface IAggregateMapper<T> where T : AbstractModel
{
    /// <summary>
    /// Flattens a root entity into an aggregate by resolving all related data.
    /// </summary>
    FlattenResult<T> Flatten(T root, IRelatedDataProvider dataProvider);

    /// <summary>
    /// Flattens a root entity into an aggregate by resolving all related data (async).
    /// </summary>
    Task<FlattenResult<T>> FlattenAsync(T root, IAsyncRelatedDataProvider dataProvider, CancellationToken ct = default);

    /// <summary>
    /// Flattens multiple root entities into aggregates.
    /// </summary>
    IEnumerable<FlattenResult<T>> FlattenMany(IEnumerable<T> roots, IRelatedDataProvider dataProvider);

    /// <summary>
    /// Flattens multiple root entities into aggregates (async).
    /// </summary>
    Task<IEnumerable<FlattenResult<T>>> FlattenManyAsync(IEnumerable<T> roots, IAsyncRelatedDataProvider dataProvider, CancellationToken ct = default);

    /// <summary>
    /// Expands an aggregate back into relational operations by diffing the desired state
    /// (from the aggregate) against the current state (from the data provider).
    /// Returns insert/delete operations for child and junction table entities.
    /// </summary>
    IEnumerable<SyncOperation> Expand(FlattenResult<T> aggregate, IRelatedDataProvider currentStateProvider);

    /// <summary>
    /// Expands an aggregate back into relational operations (async).
    /// </summary>
    Task<IEnumerable<SyncOperation>> ExpandAsync(FlattenResult<T> aggregate, IAsyncRelatedDataProvider currentStateProvider, CancellationToken ct = default);
}
