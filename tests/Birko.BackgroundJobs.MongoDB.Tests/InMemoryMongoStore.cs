using System.Collections.Concurrent;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Stores;

namespace Birko.BackgroundJobs.MongoDB.Tests;

/// <summary>
/// In-memory test double for AsyncMongoDBStore that overrides the *Core methods with a
/// ConcurrentDictionary so the MongoDBJobQueue logic (atomic claim / FailAsync / PurgeAsync)
/// can be exercised deterministically offline, with no live MongoDB / replica set.
/// </summary>
public class InMemoryMongoStore<T> : AsyncMongoDBStore<T> where T : AbstractModel, new()
{
    private readonly ConcurrentDictionary<Guid, T> _items = new();

    protected override Task InitCoreAsync(CancellationToken ct = default) => Task.CompletedTask;

    // AsyncMongoDBStore overrides ReadAsync(Guid) to hit the collection directly (bypassing
    // ReadCoreAsync), so the double must override it too.
    public override Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
        => Task.FromResult(_items.TryGetValue(guid, out var v) ? v : null);

    protected override Task<Guid> CreateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (data.Guid == null || data.Guid == Guid.Empty) data.Guid = Guid.NewGuid();
        processDelegate?.Invoke(data);
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
        q = OrderByHelper.ApplyTo(q, orderBy);
        if (offset.HasValue) q = q.Skip(offset.Value);
        if (limit.HasValue) q = q.Take(limit.Value);
        return Task.FromResult(q.ToList().AsEnumerable());
    }

    protected override Task UpdateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        processDelegate?.Invoke(data);
        if (data.Guid.HasValue) _items[data.Guid.Value] = data;
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

    // AsyncMongoDBStore overrides the native filter-based Update to hit UpdateManyAsync directly
    // (bypassing the *Core read-modify-save path). The atomic-claim fix (CR-M020) depends on it,
    // so the double must reproduce it against the dictionary: read matching items, apply the
    // property assignments, and store them back.
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
}
