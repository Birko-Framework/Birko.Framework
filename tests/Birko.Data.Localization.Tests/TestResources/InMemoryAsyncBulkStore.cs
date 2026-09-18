using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Localization.Tests.TestResources;

/// <summary>
/// Simple in-memory async bulk store for testing purposes.
/// </summary>
public class InMemoryAsyncBulkStore<T> : IAsyncBulkStore<T> where T : AbstractModel, new()
{
    private readonly Dictionary<Guid, T> _data = new();

    public Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
        => Task.FromResult(_data.TryGetValue(guid, out var item) ? item : null);

    public Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        var items = _data.Values.AsQueryable();
        if (filter != null) items = items.Where(filter);
        return Task.FromResult(items.FirstOrDefault());
    }

    public Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default)
        => Task.FromResult<IEnumerable<T>>(_data.Values.ToList());

    public Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        var items = _data.Values.AsQueryable();
        if (filter != null) items = items.Where(filter);
        if (offset.HasValue) items = items.Skip(offset.Value);
        if (limit.HasValue) items = items.Take(limit.Value);
        return Task.FromResult<IEnumerable<T>>(items.ToList());
    }

    public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        var items = _data.Values.AsQueryable();
        if (filter != null) items = items.Where(filter);
        return Task.FromResult((long)items.Count());
    }

    public Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (processDelegate != null) data = processDelegate(data);
        data.Guid ??= Guid.NewGuid();
        _data[data.Guid.Value] = data;
        return Task.FromResult(data.Guid.Value);
    }

    public Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        foreach (var item in data)
        {
            if (storeDelegate != null) item.Guid ??= Guid.NewGuid();
            else item.Guid ??= Guid.NewGuid();
            _data[item.Guid!.Value] = item;
        }
        return Task.CompletedTask;
    }

    public Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (processDelegate != null) data = processDelegate(data);
        if (data.Guid.HasValue) _data[data.Guid.Value] = data;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        foreach (var item in data)
        {
            if (item.Guid.HasValue) _data[item.Guid.Value] = item;
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(T data, CancellationToken ct = default)
    {
        if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default)
    {
        var items = _data.Values.AsQueryable().Where(filter).ToList();
        foreach (var item in items)
        {
            updateAction(item);
        }
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default)
    {
        var items = _data.Values.AsQueryable().Where(filter).ToList();
        foreach (var item in items)
        {
            updates.ApplyTo(item);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default)
    {
        foreach (var item in data)
        {
            if (item.Guid.HasValue) _data.Remove(item.Guid.Value);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default)
    {
        var items = _data.Values.AsQueryable().Where(filter).ToList();
        foreach (var item in items)
        {
            if (item.Guid.HasValue) _data.Remove(item.Guid.Value);
        }
        return Task.CompletedTask;
    }

    public Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (data.Guid == null || data.Guid == Guid.Empty) return CreateAsync(data, processDelegate, ct);
        return UpdateAsync(data, processDelegate, ct).ContinueWith(_ => data.Guid!.Value);
    }

    public Task InitAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DestroyAsync(CancellationToken ct = default) => Task.CompletedTask;
    public T CreateInstance() => new();
}
