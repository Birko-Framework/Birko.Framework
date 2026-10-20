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

    /// <summary>Runs before every single-entity create is stored; a test uses it to simulate a racing writer.</summary>
    public Action<T>? BeforeCreate { get; set; }

    /// <summary>Runs before every single-entity update is stored; a test makes it throw to fail the write.</summary>
    public Action<T>? BeforeUpdate { get; set; }

    /// <summary>False models a store that returns the new key without writing it onto the entity.</summary>
    public bool AssignsGuidOnCreate { get; set; } = true;

    /// <summary>
    /// Items a collection update treats as not stored: the rest are written, then <see cref="Birko.Data.Exceptions.EntityNotFoundException"/>
    /// names these, as a SQL store's bulk update does (TASK-555).
    /// </summary>
    public Func<T, bool>? MissingOnUpdate { get; set; }

    /// <summary>Runs before every delete call — single, collection or filter; a test makes it throw to fail the delete.</summary>
    public Action? BeforeDelete { get; set; }

    /// <summary>Every filter this store was handed (reads, counts, filter updates and deletes) — TASK-686 inspects their IN lists.</summary>
    public List<System.Linq.Expressions.LambdaExpression?> SeenFilters { get; } = new();

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

    public Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
        => Task.FromResult(_data.TryGetValue(guid, out var item) ? item : null);

    public Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        SeenFilters.Add(filter);
        var items = _data.Values.AsQueryable();
        if (filter != null) items = items.Where(filter);
        return Task.FromResult(items.FirstOrDefault());
    }

    public Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default)
        => Task.FromResult<IEnumerable<T>>(_data.Values.ToList());

    public Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        SeenFilters.Add(filter);
        FilteredReadCalls++;
        var items = _data.Values.AsQueryable();
        if (filter != null) items = items.Where(filter);
        if (offset.HasValue) items = items.Skip(offset.Value);
        if (limit.HasValue) items = items.Take(limit.Value);
        var result = items.ToList();
        RowsRead += result.Count;
        return Task.FromResult<IEnumerable<T>>(result);
    }

    public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        SeenFilters.Add(filter);
        var items = _data.Values.AsQueryable();
        if (filter != null) items = items.Where(filter);
        return Task.FromResult((long)items.Count());
    }

    public Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        CreateCalls++;
        if (processDelegate != null) data = processDelegate(data);
        BeforeCreate?.Invoke(data);
        var guid = data.Guid ?? Guid.NewGuid();
        if (AssignsGuidOnCreate) data.Guid = guid;
        _data[guid] = data;
        return Task.FromResult(guid);
    }

    /// <summary>All or nothing, as a SQL store's collection create is: every <see cref="BeforeCreate"/> runs first.</summary>
    public Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        CreateCalls++;
        var items = data.ToList();
        foreach (var item in items) BeforeCreate?.Invoke(item);
        foreach (var item in items)
        {
            item.Guid ??= Guid.NewGuid();
            _data[item.Guid!.Value] = item;
        }
        return Task.CompletedTask;
    }

    public Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        UpdateCalls++;
        if (processDelegate != null) data = processDelegate(data);
        BeforeUpdate?.Invoke(data);
        if (data.Guid.HasValue) _data[data.Guid.Value] = data;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        UpdateCalls++;
        var missing = new List<Guid>();
        foreach (var item in data)
        {
            if (MissingOnUpdate?.Invoke(item) == true) { missing.Add(item.Guid!.Value); continue; }
            if (item.Guid.HasValue) _data[item.Guid.Value] = item;
        }
        if (missing.Count > 0) throw new Birko.Data.Exceptions.EntityNotFoundException(typeof(T), missing);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(T data, CancellationToken ct = default)
    {
        DeleteCalls++;
        ct.ThrowIfCancellationRequested();
        BeforeDelete?.Invoke();
        if (data.Guid.HasValue) _data.Remove(data.Guid.Value);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default)
    {
        SeenFilters.Add(filter);
        UpdateCalls++;
        var items = _data.Values.AsQueryable().Where(filter).ToList();
        foreach (var item in items)
        {
            updateAction(item);
        }
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default)
    {
        SeenFilters.Add(filter);
        UpdateCalls++;
        var items = _data.Values.AsQueryable().Where(filter).ToList();
        foreach (var item in items)
        {
            updates.ApplyTo(item);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default)
    {
        DeleteCalls++;
        ct.ThrowIfCancellationRequested();
        BeforeDelete?.Invoke();
        foreach (var item in data)
        {
            if (item.Guid.HasValue) _data.Remove(item.Guid.Value);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default)
    {
        SeenFilters.Add(filter);
        DeleteCalls++;
        ct.ThrowIfCancellationRequested();
        BeforeDelete?.Invoke();
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

    public Task InitAsync(CancellationToken ct = default)
    {
        InitCalls++;
        return Task.CompletedTask;
    }
    public Task DestroyAsync(CancellationToken ct = default) => Task.CompletedTask;
    public T CreateInstance()
    {
        CreateInstanceCalls++;
        return new();
    }
}
