using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Async bulk store wrapper that adds soft-delete behavior.
/// </summary>
public class AsyncSoftDeleteBulkStoreWrapper<TStore, T> : AsyncSoftDeleteStoreWrapper<TStore, T>, IAsyncBulkStore<T>, IAsyncAggregatableStore<T>
    where TStore : IAsyncBulkStore<T>
    where T : Data.Models.AbstractModel, ISoftDeletable
{
    /// <summary>Aggregates within this decorator's scope: the query's filter is narrowed as every read's is (TASK-631).</summary>
    public Task<IReadOnlyList<AggregateResult>> AggregateAsync(AggregateQuery<T> query, CancellationToken ct = default)
        => StoreChain.AggregateThrough(_innerStore, query.WithFilter(SoftDeleteFilter.CombineWithNotDeleted(query.Filter)), ct);

    public AsyncSoftDeleteBulkStoreWrapper(TStore innerStore, IDateTimeProvider clock) : base(innerStore, clock) { }

    public Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default)
    {
        return ReadAsync(null, null, null, null, ct);
    }

    public Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        return _innerStore.ReadAsync(SoftDeleteFilter.CombineWithNotDeleted(filter), orderBy, limit, offset, ct);
    }

    public Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        return _innerStore.CreateAsync(data.Select(item => { item.DeletedAt = null; return item; }).ToList(), storeDelegate, ct);
    }

    public async Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        // SH-M331 (TASK-159): a soft-deleted row is invisible to reads, so it is not found by an update either. Writing it
        // through let a caller resurrect it (DeletedAt back to null) past the Sluggable and Default probes, which read
        // through this wrapper and never saw it. TASK-555's shape: the live items are written, then the rest reported.
        var items = data.ToList();
        var ids = items.Select(i => i.Guid).ToList();
        var deleted = (await _innerStore.ReadAsync(x => ids.Contains(x.Guid) && x.DeletedAt != null, null, null, null, ct)).Select(x => x.Guid!.Value).ToHashSet();
        try
        {
            await _innerStore.UpdateAsync(items.Where(i => i.Guid == null || !deleted.Contains(i.Guid.Value)).ToList(), storeDelegate, ct);
        }
        catch (Birko.Data.Exceptions.EntityNotFoundException missing) when (deleted.Count > 0)
        {
            // TASK-649: the inner store reports the items it never held; the soft-deleted ones are named with them.
            throw new Birko.Data.Exceptions.EntityNotFoundException(typeof(T), missing.Guids.Concat(deleted));
        }
        if (deleted.Count > 0)
            throw new Birko.Data.Exceptions.EntityNotFoundException(typeof(T), deleted);
    }

    public Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default)
    {
        RequireCallerFilter(filter, "update");
        return _innerStore.UpdateAsync(SoftDeleteFilter.CombineWithNotDeleted(filter), updateAction, ct);
    }

    public Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default)
    {
        RequireCallerFilter(filter, "update");
        return _innerStore.UpdateAsync(SoftDeleteFilter.CombineWithNotDeleted(filter), updates, ct);
    }

    /// <summary>
    /// Soft-deletes multiple entities.
    /// </summary>
    public async Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default)
    {
        // SH-M325 (TASK-159): a row already deleted keeps its deletion instant; only live rows are stamped. SH-M323: once.
        var now = _clock.UtcNow;
        var items = data.ToList();
        var ids = items.Select(i => i.Guid).ToList();
        var already = (await _innerStore.ReadAsync(x => ids.Contains(x.Guid) && x.DeletedAt != null, null, null, null, ct)).Select(x => x.Guid!.Value).ToHashSet();
        var live = items.Where(i => i.Guid == null || !already.Contains(i.Guid.Value)).ToList();
        foreach (var item in live) SoftDeleteFilter.Stamp(item, now);
        if (live.Count > 0)
            await _innerStore.UpdateAsync(live, ct: ct);
    }

    /// <summary>
    /// Soft-deletes all entities matching the filter.
    /// </summary>
    public Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default)
    {
        // SH-M332 (TASK-159): a null filter became "soft-delete every active row". SH-M333: one native UPDATE, not a
        // per-row read-modify-write that a failure leaves half-applied.
        RequireCallerFilter(filter, "delete");
        return _innerStore.UpdateAsync(SoftDeleteFilter.CombineWithNotDeleted(filter), SoftDeleteFilter.Deletion<T>(_clock.UtcNow), ct);
    }

    private static void RequireCallerFilter(Expression<Func<T, bool>> filter, string operation)
    {
        if (filter is null)
            throw new ArgumentNullException(nameof(filter), $"A filtered soft {operation} on {typeof(T).Name} needs a filter.");
        Birko.Data.Expressions.BoundedFilterGuard.Require(filter, operation, typeof(T).Name,
            "a filter that names the rows (deleted rows are excluded for you)");
    }
}
