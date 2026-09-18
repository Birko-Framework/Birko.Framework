using Birko.Data.Stores;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Sync.Tests.TestInfrastructure;

/// <summary>
/// Minimal in-memory <see cref="AbstractAsyncBulkStore{T}"/> for the AsyncSyncProvider tests.
/// Birko.Data.InMemory's store is sync-only (extends AbstractBulkStore), so the async provider —
/// which requires IAsyncBulkStore endpoints — needs an async double. Backed by a thread-safe
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>, mirroring AbstractInMemoryStore's core logic.
/// </summary>
public abstract class InMemoryAsyncStore<T> : AbstractAsyncBulkStore<T>
    where T : Birko.Data.Models.AbstractModel
{
    protected readonly ConcurrentDictionary<Guid, T> _items = new();

    public IReadOnlyDictionary<Guid, T> Items => _items;

    protected override Task InitCoreAsync(CancellationToken ct = default) => Task.CompletedTask;

    public override Task DestroyAsync(CancellationToken ct = default)
    {
        _items.Clear();
        return Task.CompletedTask;
    }

    protected override Task<Guid> CreateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (data == null)
        {
            return Task.FromResult(Guid.Empty);
        }
        data.Guid ??= Guid.NewGuid();
        processDelegate?.Invoke(data);
        _items[data.Guid.Value] = data;
        return Task.FromResult(data.Guid.Value);
    }

    protected override Task<T?> ReadCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        var predicate = filter?.Compile();
        return Task.FromResult(_items.Values.FirstOrDefault(x => predicate?.Invoke(x) ?? true));
    }

    protected override Task UpdateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (data?.Guid != null && _items.ContainsKey(data.Guid.Value))
        {
            processDelegate?.Invoke(data);
            _items[data.Guid.Value] = data;
        }
        return Task.CompletedTask;
    }

    protected override Task DeleteCoreAsync(T data, CancellationToken ct = default)
    {
        if (data?.Guid != null)
        {
            _items.TryRemove(data.Guid.Value, out _);
        }
        return Task.CompletedTask;
    }

    protected override Task<long> CountCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        if (filter == null)
        {
            return Task.FromResult((long)_items.Count);
        }
        return Task.FromResult((long)_items.Values.Count(filter.Compile()));
    }

    protected override Task CreateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        if (data == null)
        {
            return Task.CompletedTask;
        }
        foreach (var item in data.Where(x => x != null))
        {
            item.Guid ??= Guid.NewGuid();
            storeDelegate?.Invoke(item);
            _items[item.Guid.Value] = item;
        }
        return Task.CompletedTask;
    }

    protected override Task<IEnumerable<T>> ReadCoreAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        var predicate = filter?.Compile();
        IEnumerable<T> result = _items.Values;
        if (predicate != null)
        {
            result = result.Where(predicate);
        }
        if (offset.HasValue)
        {
            result = result.Skip(offset.Value);
        }
        if (limit.HasValue)
        {
            result = result.Take(limit.Value);
        }
        return Task.FromResult<IEnumerable<T>>(result.ToList());
    }

    protected override Task UpdateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        if (data == null)
        {
            return Task.CompletedTask;
        }
        foreach (var item in data.Where(x => x != null && x.Guid.HasValue && _items.ContainsKey(x.Guid.Value)))
        {
            storeDelegate?.Invoke(item);
            _items[item.Guid!.Value] = item;
        }
        return Task.CompletedTask;
    }

    protected override Task DeleteCoreAsync(IEnumerable<T> data, CancellationToken ct = default)
    {
        if (data == null)
        {
            return Task.CompletedTask;
        }
        foreach (var item in data.Where(x => x != null && x.Guid.HasValue))
        {
            _items.TryRemove(item.Guid!.Value, out _);
        }
        return Task.CompletedTask;
    }
}
