using Birko.Data.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Localization.Tests.TestResources;

/// <summary>
/// Simple in-memory bulk store for testing purposes.
/// </summary>
public class InMemoryBulkStore<T> : IBulkStore<T> where T : AbstractModel, new()
{
    private readonly Dictionary<Guid, T> _data = new();

    public T? Read(Guid guid) => _data.TryGetValue(guid, out var item) ? item : null;

    public T? Read(Expression<Func<T, bool>>? filter = null)
    {
        var items = _data.Values.AsQueryable();
        if (filter != null) items = items.Where(filter);
        return items.FirstOrDefault();
    }

    public IEnumerable<T> Read() => _data.Values;

    public IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
    {
        var items = _data.Values.AsQueryable();
        if (filter != null) items = items.Where(filter);
        if (offset.HasValue) items = items.Skip(offset.Value);
        if (limit.HasValue) items = items.Take(limit.Value);
        return items.ToList();
    }

    public long Count(Expression<Func<T, bool>>? filter = null)
    {
        var items = _data.Values.AsQueryable();
        if (filter != null) items = items.Where(filter);
        return items.Count();
    }

    public Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (storeDelegate != null) data = storeDelegate(data);
        data.Guid ??= Guid.NewGuid();
        _data[data.Guid.Value] = data;
        return data.Guid.Value;
    }

    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        foreach (var item in data) Create(item, storeDelegate);
    }

    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (storeDelegate != null) data = storeDelegate(data);
        if (data.Guid.HasValue) _data[data.Guid.Value] = data;
    }

    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        foreach (var item in data) Update(item, storeDelegate);
    }

    public void Delete(T data)
    {
        if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
    }

    public void Delete(IEnumerable<T> data)
    {
        foreach (var item in data) Delete(item);
    }

    public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (data.Guid == null || data.Guid == Guid.Empty) return Create(data, storeDelegate);
        Update(data, storeDelegate);
        return data.Guid.Value;
    }

    public void Init() { }
    public void Destroy() { }
    public T CreateInstance() => new();
}
