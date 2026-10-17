using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Bulk store wrapper that adds soft-delete behavior.
/// </summary>
public class SoftDeleteBulkStoreWrapper<TStore, T> : SoftDeleteStoreWrapper<TStore, T>, IBulkStore<T>
    where TStore : IBulkStore<T>
    where T : Data.Models.AbstractModel, ISoftDeletable
{
    public SoftDeleteBulkStoreWrapper(TStore innerStore, IDateTimeProvider clock) : base(innerStore, clock) { }

    public IEnumerable<T> Read()
    {
        return Read(null, null, null, null);
    }

    public IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
    {
        return _innerStore.Read(SoftDeleteFilter.CombineWithNotDeleted(filter), orderBy, limit, offset);
    }

    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        _innerStore.Create(data.Select(item => { item.DeletedAt = null; return item; }).ToList(), storeDelegate);
    }

    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        // SH-M331 (TASK-159): a soft-deleted row is invisible to reads, so it is not found by an update either. Writing it
        // through let a caller resurrect it (DeletedAt back to null) past the Sluggable and Default probes, which read
        // through this wrapper and never saw it. TASK-555's shape: the live items are written, then the rest reported.
        var items = data.ToList();
        var ids = items.Select(i => i.Guid).ToList();
        var deleted = _innerStore.Read(x => ids.Contains(x.Guid) && x.DeletedAt != null, null, null, null).Select(x => x.Guid!.Value).ToHashSet();
        _innerStore.Update(items.Where(i => i.Guid == null || !deleted.Contains(i.Guid.Value)).ToList(), storeDelegate);
        if (deleted.Count > 0)
            throw new Birko.Data.Exceptions.EntityNotFoundException(typeof(T), deleted);
    }

    public void Update(Expression<Func<T, bool>> filter, Action<T> updateAction)
    {
        RequireCallerFilter(filter, "update");
        _innerStore.Update(SoftDeleteFilter.CombineWithNotDeleted(filter), updateAction);
    }

    public void Update(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates)
    {
        RequireCallerFilter(filter, "update");
        _innerStore.Update(SoftDeleteFilter.CombineWithNotDeleted(filter), updates);
    }

    /// <summary>
    /// Soft-deletes multiple entities.
    /// </summary>
    public void Delete(IEnumerable<T> data)
    {
        // SH-M325 (TASK-159): a row already deleted keeps its deletion instant; only live rows are stamped. SH-M323: once.
        var now = _clock.UtcNow;
        var items = data.ToList();
        var ids = items.Select(i => i.Guid).ToList();
        var already = _innerStore.Read(x => ids.Contains(x.Guid) && x.DeletedAt != null, null, null, null).Select(x => x.Guid!.Value).ToHashSet();
        var live = items.Where(i => i.Guid == null || !already.Contains(i.Guid.Value)).ToList();
        foreach (var item in live) SoftDeleteFilter.Stamp(item, now);
        if (live.Count > 0)
            _innerStore.Update(live);
    }

    /// <summary>
    /// Soft-deletes all entities matching the filter.
    /// </summary>
    public void Delete(Expression<Func<T, bool>> filter)
    {
        // SH-M332 (TASK-159): a null filter became "soft-delete every active row". SH-M333: one native UPDATE, not a
        // per-row read-modify-write that a failure leaves half-applied.
        RequireCallerFilter(filter, "delete");
        _innerStore.Update(SoftDeleteFilter.CombineWithNotDeleted(filter), SoftDeleteFilter.Deletion<T>(_clock.UtcNow));
    }

    private static void RequireCallerFilter(Expression<Func<T, bool>> filter, string operation)
    {
        if (filter is null)
            throw new ArgumentNullException(nameof(filter), $"A filtered soft {operation} on {typeof(T).Name} needs a filter.");
        Birko.Data.Expressions.BoundedFilterGuard.Require(filter, operation, typeof(T).Name,
            "a filter that names the rows (deleted rows are excluded for you)");
    }
}
