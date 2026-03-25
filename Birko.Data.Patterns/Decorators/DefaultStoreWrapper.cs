using Birko.Data.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Store wrapper that enforces only one entity can have IsDefault set to true.
/// When an entity is created or updated with IsDefault=true, all other entities
/// with IsDefault=true are automatically set to false.
/// Requires a bulk store because enforcing the constraint needs bulk read and update.
/// </summary>
public class DefaultStoreWrapper<TStore, T> : IBulkStore<T>, IStoreWrapper<T>
    where TStore : IBulkStore<T>
    where T : AbstractModel, IDefault
{
    protected readonly TStore _innerStore;

    public DefaultStoreWrapper(TStore innerStore)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
    }

    public T? Read(Guid guid) => ((IReadStore<T>)_innerStore).Read(guid);
    public T? Read(Expression<Func<T, bool>>? filter = null) => ((IReadStore<T>)_innerStore).Read(filter);
    public long Count(Expression<Func<T, bool>>? filter = null) => _innerStore.Count(filter);
    public IEnumerable<T> Read() => _innerStore.Read();

    public IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
    {
        return _innerStore.Read(filter, orderBy, limit, offset);
    }

    public Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (data.IsDefault)
        {
            UnsetOtherDefaults(data.Guid);
        }
        return _innerStore.Create(data, storeDelegate);
    }

    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var items = data.ToList();
        if (items.Any(i => i.IsDefault))
        {
            UnsetOtherDefaults(null);
            // Ensure only the last item marked as default stays default
            var defaultItems = items.Where(i => i.IsDefault).ToList();
            foreach (var item in defaultItems.SkipLast(1))
            {
                item.IsDefault = false;
            }
        }
        _innerStore.Create(items, storeDelegate);
    }

    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (data.IsDefault)
        {
            UnsetOtherDefaults(data.Guid);
        }
        _innerStore.Update(data, storeDelegate);
    }

    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
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
            UnsetOtherDefaults(lastDefault.Guid, items.Select(i => i.Guid).ToHashSet());
        }
        _innerStore.Update(items, storeDelegate);
    }

    public void Delete(T data) => _innerStore.Delete(data);
    public void Delete(IEnumerable<T> data) => _innerStore.Delete(data);

    public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (data.IsDefault)
        {
            UnsetOtherDefaults(data.Guid);
        }

        if (data.Guid == null || data.Guid == Guid.Empty)
        {
            return _innerStore.Create(data, storeDelegate);
        }
        else
        {
            _innerStore.Update(data, storeDelegate);
            return data.Guid ?? Guid.Empty;
        }
    }

    public void Init() => _innerStore.Init();
    public void Destroy() => _innerStore.Destroy();
    public T CreateInstance() => _innerStore.CreateInstance();

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;

    /// <summary>
    /// Finds all entities with IsDefault=true (excluding the given entity) and sets them to false.
    /// </summary>
    protected void UnsetOtherDefaults(Guid? excludeGuid, HashSet<Guid?>? alsoExcludeGuids = null)
    {
        var others = _innerStore.Read(e => e.IsDefault, orderBy: null, limit: null, offset: null).Where(e => e.Guid != excludeGuid);
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
        _innerStore.Update(toUpdate);
    }
}
