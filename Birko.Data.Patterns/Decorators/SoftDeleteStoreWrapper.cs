using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using System;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Store wrapper that adds soft-delete behavior.
/// Read/Count operations auto-filter out deleted entities.
/// Delete operations set DeletedAt instead of removing.
/// </summary>
public class SoftDeleteStoreWrapper<TStore, T> : IStore<T>, IStoreWrapper<T>
    where TStore : IStore<T>
    where T : Data.Models.AbstractModel, ISoftDeletable
{
    protected readonly TStore _innerStore;

    public SoftDeleteStoreWrapper(TStore innerStore)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
    }

    public T? Read(Guid guid)
    {
        var entity = _innerStore.Read(guid);
        return entity?.DeletedAt == null ? entity : null;
    }

    public T? Read(Expression<Func<T, bool>>? filter = null)
    {
        return _innerStore.Read(SoftDeleteFilter.CombineWithNotDeleted(filter));
    }

    public long Count(Expression<Func<T, bool>>? filter = null)
    {
        return _innerStore.Count(SoftDeleteFilter.CombineWithNotDeleted(filter));
    }

    public Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        data.DeletedAt = null;
        return _innerStore.Create(data, storeDelegate);
    }

    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        _innerStore.Update(data, storeDelegate);
    }

    /// <summary>
    /// Soft-deletes the entity by setting DeletedAt to UtcNow.
    /// </summary>
    public void Delete(T data)
    {
        data.DeletedAt = DateTime.UtcNow;
        _innerStore.Update(data);
    }

    public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (data.Guid == null || data.Guid == Guid.Empty)
        {
            return Create(data, storeDelegate);
        }
        else
        {
            Update(data, storeDelegate);
            return data.Guid ?? Guid.Empty;
        }
    }

    public void Init() => _innerStore.Init();
    public void Destroy() => _innerStore.Destroy();
    public T CreateInstance() => _innerStore.CreateInstance();

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;
}
