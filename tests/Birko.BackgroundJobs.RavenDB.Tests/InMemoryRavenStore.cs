using System.Collections.Concurrent;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;

namespace Birko.BackgroundJobs.RavenDB.Tests;

/// <summary>
/// In-memory test double for AsyncRavenDBStore that overrides the *Core methods with a
/// ConcurrentDictionary so the RavenDBJobQueue logic can be exercised deterministically offline,
/// with no live RavenDB server.
///
/// The RavenDB filter-based UpdateAsync(filter, PropertyUpdate&lt;T&gt;) is NOT overridden natively —
/// it uses the AbstractAsyncBulkStore read-modify-save fallback, which reads matching items via the
/// bulk ReadCoreAsync(filter) and writes each back via the single UpdateCoreAsync. Overriding those
/// two (plus ReadAsync(Guid), which the base overrides to hit the document store directly) is
/// therefore sufficient for the CR-M021 atomic-claim path to work here.
/// </summary>
public class InMemoryRavenStore<T> : AsyncRavenDBStore<T> where T : AbstractModel, new()
{
    private readonly ConcurrentDictionary<Guid, T> _items = new();

    protected override Task InitCoreAsync(CancellationToken ct = default) => Task.CompletedTask;

    // AsyncRavenDBStore overrides ReadAsync(Guid) to load from the document store directly
    // (bypassing ReadCoreAsync), so the double must override it too.
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
}
