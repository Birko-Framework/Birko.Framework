using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Configuration;
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

    /// <summary>Runs before every single-entity create is stored; a test uses it to simulate a racing writer.</summary>
    public Action<T>? BeforeCreate { get; set; }

    /// <summary>Runs before every single-entity update is stored; a test makes it throw to fail the write.</summary>
    public Action<T>? BeforeUpdate { get; set; }

    /// <summary>False models a store that returns the new key without writing it onto the entity.</summary>
    public bool AssignsGuidOnCreate { get; set; } = true;

    public int InitCalls { get; private set; }

    /// <summary>How many filtered collection reads ran — what a test counts to prove a read is batched.</summary>
    public int FilteredReadCalls { get; private set; }

    /// <summary>How many rows the filtered collection reads returned — what proves a lookup did not scan.</summary>
    public int RowsRead { get; private set; }

    /// <summary>
    /// Write calls made on this store, one per call whatever its size: a single, collection or filter write counts once.
    /// What a test counts to prove a write is batched (TASK-690).
    /// </summary>
    public int CreateCalls { get; private set; }

    /// <inheritdoc cref="CreateCalls"/>
    public int UpdateCalls { get; private set; }

    /// <inheritdoc cref="CreateCalls"/>
    public int DeleteCalls { get; private set; }

    public int CreateInstanceCalls { get; private set; }

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
        FilteredReadCalls++;
        var items = _data.Values.AsQueryable();
        if (filter != null) items = items.Where(filter);
        if (offset.HasValue) items = items.Skip(offset.Value);
        if (limit.HasValue) items = items.Take(limit.Value);
        var result = items.ToList();
        RowsRead += result.Count;
        return result;
    }

    public long Count(Expression<Func<T, bool>>? filter = null)
    {
        var items = _data.Values.AsQueryable();
        if (filter != null) items = items.Where(filter);
        return items.Count();
    }

    public Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        CreateCalls++;
        if (storeDelegate != null) data = storeDelegate(data);
        BeforeCreate?.Invoke(data);
        return Store(data);
    }

    /// <summary>All or nothing, as a SQL store's collection create is: every <see cref="BeforeCreate"/> runs first.</summary>
    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        CreateCalls++;
        var items = data.Select(item => storeDelegate != null ? storeDelegate(item) : item).ToList();
        foreach (var item in items) BeforeCreate?.Invoke(item);
        foreach (var item in items) Store(item);
    }

    private Guid Store(T data)
    {
        var guid = data.Guid ?? Guid.NewGuid();
        if (AssignsGuidOnCreate) data.Guid = guid;
        _data[guid] = data;
        return guid;
    }

    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        UpdateCalls++;
        UpdateOne(data, storeDelegate);
    }

    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        UpdateCalls++;
        foreach (var item in data) UpdateOne(item, storeDelegate);
    }

    private void UpdateOne(T data, StoreDataDelegate<T>? storeDelegate)
    {
        if (storeDelegate != null) data = storeDelegate(data);
        BeforeUpdate?.Invoke(data);
        if (data.Guid.HasValue) _data[data.Guid.Value] = data;
    }

    public void Delete(T data)
    {
        DeleteCalls++;
        Remove(data);
    }

    private void Remove(T data)
    {
        if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
    }

    public void Update(Expression<Func<T, bool>> filter, Action<T> updateAction)
    {
        // Mirrors AbstractBulkStore: the callback runs, then each entity is written through Update(T).
        UpdateCalls++;
        var items = _data.Values.AsQueryable().Where(filter).ToList();
        foreach (var item in items)
        {
            updateAction(item);
            UpdateOne(item, null);
        }
    }

    public void Update(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates)
    {
        UpdateCalls++;
        var items = _data.Values.AsQueryable().Where(filter).ToList();
        foreach (var item in items)
        {
            updates.ApplyTo(item);
        }
    }

    public void Delete(IEnumerable<T> data)
    {
        DeleteCalls++;
        foreach (var item in data) Remove(item);
    }

    public void Delete(Expression<Func<T, bool>> filter)
    {
        DeleteCalls++;
        var items = _data.Values.AsQueryable().Where(filter).ToList();
        foreach (var item in items) Remove(item);
    }

    public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (data.Guid == null || data.Guid == Guid.Empty) return Create(data, storeDelegate);
        Update(data, storeDelegate);
        return data.Guid.Value;
    }

    public void Init() => InitCalls++;
    public void Destroy() { }
    public T CreateInstance()
    {
        CreateInstanceCalls++;
        return new();
    }
}
