using Birko.Data.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Async store wrapper that enforces only one entity can have IsDefault set to true.
/// When an entity is created or updated with IsDefault=true, all other entities
/// with IsDefault=true are automatically set to false.
/// Requires an async bulk store because enforcing the constraint needs bulk read and update.
/// </summary>
public class AsyncDefaultStoreWrapper<TStore, T> : IAsyncBulkStore<T>, IStoreWrapper<T>
    where TStore : IAsyncBulkStore<T>
    where T : AbstractModel, IDefault
{
    protected readonly TStore _innerStore;

    public AsyncDefaultStoreWrapper(TStore innerStore)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
    }

    public Task<T?> ReadAsync(Guid guid, CancellationToken ct = default) => ((IAsyncReadStore<T>)_innerStore).ReadAsync(guid, ct);
    public Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => ((IAsyncReadStore<T>)_innerStore).ReadAsync(filter, ct);
    public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => _innerStore.CountAsync(filter, ct);
    public Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default) => _innerStore.ReadAsync(ct);

    public Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        return _innerStore.ReadAsync(filter, orderBy, limit, offset, ct);
    }

    public async Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (data.IsDefault)
        {
            await UnsetOtherDefaultsAsync(data.Guid, ct: ct);
        }
        return await _innerStore.CreateAsync(data, processDelegate, ct);
    }

    public async Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        var items = data.ToList();
        if (items.Any(i => i.IsDefault))
        {
            await UnsetOtherDefaultsAsync(null, ct: ct);
            // Ensure only the last item marked as default stays default
            var defaultItems = items.Where(i => i.IsDefault).ToList();
            foreach (var item in defaultItems.SkipLast(1))
            {
                item.IsDefault = false;
            }
        }
        await _innerStore.CreateAsync(items, storeDelegate, ct);
    }

    public async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (data.IsDefault)
        {
            await UnsetOtherDefaultsAsync(data.Guid, ct: ct);
        }
        await _innerStore.UpdateAsync(data, processDelegate, ct);
    }

    public async Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        var items = data.ToList();
        if (items.Any(i => i.IsDefault))
        {
            var defaultItems = items.Where(i => i.IsDefault).ToList();
            // Keep only the last one as default
            foreach (var item in defaultItems.SkipLast(1))
            {
                item.IsDefault = false;
            }
            var lastDefault = defaultItems.Last();
            await UnsetOtherDefaultsAsync(lastDefault.Guid, items.Select(i => i.Guid).ToHashSet(), ct);
        }
        await _innerStore.UpdateAsync(items, storeDelegate, ct);
    }

    public async Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default)
    {
        var items = (await _innerStore.ReadAsync(filter, null, null, null, ct)).ToList();
        foreach (var item in items)
        {
            updateAction(item);
            if (item.IsDefault)
            {
                await UnsetOtherDefaultsAsync(item.Guid, ct: ct);
            }
            await _innerStore.UpdateAsync(item, ct: ct);
        }
    }

    public Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default) => _innerStore.UpdateAsync(filter, updates, ct);

    public Task DeleteAsync(T data, CancellationToken ct = default) => _innerStore.DeleteAsync(data, ct);
    public Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default) => _innerStore.DeleteAsync(data, ct);
    public Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default) => _innerStore.DeleteAsync(filter, ct);

    public async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (data.IsDefault)
        {
            await UnsetOtherDefaultsAsync(data.Guid, ct: ct);
        }

        if (data.Guid == null || data.Guid == Guid.Empty)
        {
            return await _innerStore.CreateAsync(data, processDelegate, ct);
        }
        else
        {
            await _innerStore.UpdateAsync(data, processDelegate, ct);
            return data.Guid ?? Guid.Empty;
        }
    }

    public Task InitAsync(CancellationToken ct = default) => _innerStore.InitAsync(ct);
    public Task DestroyAsync(CancellationToken ct = default) => _innerStore.DestroyAsync(ct);
    public T CreateInstance() => _innerStore.CreateInstance();

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;

    /// <summary>
    /// Finds all entities with IsDefault=true (excluding the given entity) and sets them to false.
    /// </summary>
    protected async Task UnsetOtherDefaultsAsync(Guid? excludeGuid, HashSet<Guid?>? alsoExcludeGuids = null, CancellationToken ct = default)
    {
        var allDefaults = await _innerStore.ReadAsync(e => e.IsDefault, orderBy: null, limit: null, offset: null, ct);
        var others = allDefaults.Where(e => e.Guid != excludeGuid);
        if (alsoExcludeGuids != null)
        {
            others = others.Where(e => !alsoExcludeGuids.Contains(e.Guid));
        }

        var toUpdate = others.ToList();
        if (toUpdate.Count == 0)
        {
            return;
        }

        foreach (var item in toUpdate)
        {
            item.IsDefault = false;
        }
        await _innerStore.UpdateAsync(toUpdate, storeDelegate: null, ct);
    }
}
