using System.Collections.Concurrent;
using System.Linq.Expressions;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;

namespace Birko.BackgroundJobs.ElasticSearch.Tests;

/// <summary>
/// In-memory test double for AsyncElasticSearchStore that overrides the *Core methods with a
/// ConcurrentDictionary so the ElasticSearchJobQueue logic (atomic claim / FailAsync / PurgeAsync)
/// can be exercised deterministically offline, with no live Elasticsearch cluster.
///
/// Crucially it also overrides the native filter-based <see cref="UpdateAsync(Expression{Func{T,bool}}, PropertyUpdate{T}, CancellationToken)"/>
/// — the base implements it via UpdateByQuery against the cluster, which the CR-M016 atomic-claim
/// fix in DequeueAsync relies on. The double applies the PropertyUpdate to matching dictionary
/// entries so the conditional-claim + re-read-verify path works offline.
/// </summary>
public class InMemoryElasticStore<T> : AsyncElasticSearchStore<T> where T : AbstractModel, new()
{
    private readonly ConcurrentDictionary<Guid, T> _items = new();

    protected override Task InitCoreAsync(CancellationToken ct = default) => Task.CompletedTask;

    protected override Task<Guid> CreateCoreAsync(T data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        if (data.Guid == null || data.Guid == Guid.Empty) data.Guid = Guid.NewGuid();
        storeDelegate?.Invoke(data);
        _items[data.Guid.Value] = data;
        return Task.FromResult(data.Guid.Value);
    }

    protected override Task<T?> ReadCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        var q = _items.Values.AsEnumerable();
        if (filter != null) q = q.Where(filter.Compile());
        return Task.FromResult(q.FirstOrDefault());
    }

    protected override Task<IEnumerable<T>> ReadCoreAsync(
        Expression<Func<T, bool>>? filter = null,
        OrderBy<T>? orderBy = null,
        int? limit = null,
        int? offset = null,
        CancellationToken ct = default)
    {
        var q = _items.Values.AsEnumerable();
        if (filter != null) q = q.Where(filter.Compile());
        if (orderBy != null) q = OrderByHelper.ApplyTo(q, orderBy);
        if (offset.HasValue) q = q.Skip(offset.Value);
        if (limit.HasValue) q = q.Take(limit.Value);
        return Task.FromResult(q.ToList().AsEnumerable());
    }

    protected override Task UpdateCoreAsync(T data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        storeDelegate?.Invoke(data);
        if (data.Guid.HasValue) _items[data.Guid.Value] = data;
        return Task.CompletedTask;
    }

    // The base overrides the native filter-based update via UpdateByQuery; the atomic-claim fix
    // (CR-M016) depends on it. Apply the assignments to matching entries in the dictionary.
    public override Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default)
    {
        var matches = _items.Values.Where(filter.Compile()).ToList();
        foreach (var item in matches)
        {
            updates.ApplyTo(item);
            if (item.Guid.HasValue) _items[item.Guid.Value] = item;
        }
        return Task.CompletedTask;
    }

    protected override Task DeleteCoreAsync(T data, CancellationToken ct = default)
    {
        if (data.Guid.HasValue) _items.TryRemove(data.Guid.Value, out _);
        return Task.CompletedTask;
    }

    protected override Task DeleteCoreAsync(IEnumerable<T> data, CancellationToken ct = default)
    {
        foreach (var d in data)
            if (d.Guid.HasValue) _items.TryRemove(d.Guid.Value, out _);
        return Task.CompletedTask;
    }

    protected override Task<long> CountCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        var q = _items.Values.AsEnumerable();
        if (filter != null) q = q.Where(filter.Compile());
        return Task.FromResult((long)q.Count());
    }
}
