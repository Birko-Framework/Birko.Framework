using Birko.Data.Aggregates.Core;
using Birko.Data.Aggregates.Mapping;
using Birko.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Aggregates.Extensions;

/// <summary>
/// Extension methods for integrating aggregate mapping with Birko.Data.Sync pipelines.
/// </summary>
public static class SyncPipelineExtensions
{
    /// <summary>
    /// Creates an <see cref="AggregateMapper{T}"/> from an aggregate definition.
    /// </summary>
    public static AggregateMapper<T> CreateMapper<T>(this AggregateDefinition<T> definition) where T : AbstractModel
    {
        return new AggregateMapper<T>(definition);
    }

    /// <summary>
    /// Flattens a batch of root entities into aggregate documents for sync upload to a NoSQL store.
    /// </summary>
    /// <param name="mapper">The aggregate mapper.</param>
    /// <param name="entities">Root entities to flatten.</param>
    /// <param name="dataProvider">Provider that resolves related data from the source store.</param>
    /// <returns>Flattened aggregates ready for NoSQL insertion.</returns>
    public static IEnumerable<FlattenResult<T>> FlattenForSync<T>(
        this IAggregateMapper<T> mapper,
        IEnumerable<T> entities,
        IRelatedDataProvider dataProvider) where T : AbstractModel
    {
        return mapper.FlattenMany(entities, dataProvider);
    }

    /// <summary>
    /// Flattens a batch of root entities into aggregate documents for sync upload to a NoSQL store (async).
    /// </summary>
    public static Task<IEnumerable<FlattenResult<T>>> FlattenForSyncAsync<T>(
        this IAggregateMapper<T> mapper,
        IEnumerable<T> entities,
        IAsyncRelatedDataProvider dataProvider,
        CancellationToken ct = default) where T : AbstractModel
    {
        return mapper.FlattenManyAsync(entities, dataProvider, ct);
    }

    /// <summary>
    /// Expands an aggregate document into relational sync operations (inserts/deletes on child/junction tables).
    /// </summary>
    /// <param name="mapper">The aggregate mapper.</param>
    /// <param name="aggregate">The aggregate to expand.</param>
    /// <param name="currentStateProvider">Provider that resolves current related data from the target relational store.</param>
    /// <returns>Sync operations to apply to the relational store.</returns>
    public static IEnumerable<SyncOperation> ExpandFromSync<T>(
        this IAggregateMapper<T> mapper,
        FlattenResult<T> aggregate,
        IRelatedDataProvider currentStateProvider) where T : AbstractModel
    {
        return mapper.Expand(aggregate, currentStateProvider);
    }

    /// <summary>
    /// Expands an aggregate document into relational sync operations (async).
    /// </summary>
    public static Task<IEnumerable<SyncOperation>> ExpandFromSyncAsync<T>(
        this IAggregateMapper<T> mapper,
        FlattenResult<T> aggregate,
        IAsyncRelatedDataProvider currentStateProvider,
        CancellationToken ct = default) where T : AbstractModel
    {
        return mapper.ExpandAsync(aggregate, currentStateProvider, ct);
    }

    /// <summary>
    /// Expands multiple aggregates into relational sync operations.
    /// </summary>
    public static IEnumerable<SyncOperation> ExpandManyFromSync<T>(
        this IAggregateMapper<T> mapper,
        IEnumerable<FlattenResult<T>> aggregates,
        IRelatedDataProvider currentStateProvider) where T : AbstractModel
    {
        return aggregates.SelectMany(aggregate => mapper.Expand(aggregate, currentStateProvider));
    }

    /// <summary>
    /// Expands multiple aggregates into relational sync operations (async).
    /// </summary>
    public static async Task<IEnumerable<SyncOperation>> ExpandManyFromSyncAsync<T>(
        this IAggregateMapper<T> mapper,
        IEnumerable<FlattenResult<T>> aggregates,
        IAsyncRelatedDataProvider currentStateProvider,
        CancellationToken ct = default) where T : AbstractModel
    {
        var allOperations = new List<SyncOperation>();
        foreach (var aggregate in aggregates)
        {
            ct.ThrowIfCancellationRequested();
            var ops = await mapper.ExpandAsync(aggregate, currentStateProvider, ct);
            allOperations.AddRange(ops);
        }
        return allOperations;
    }
}
