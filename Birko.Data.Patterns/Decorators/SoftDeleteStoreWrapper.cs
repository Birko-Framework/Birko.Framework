using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Time;
using System;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Store wrapper that adds soft-delete behavior.
/// Read/Count operations auto-filter out deleted entities.
/// Delete operations set DeletedAt instead of removing.
/// </summary>
public class SoftDeleteStoreWrapper<TStore, T> : IStore<T>, IStoreWrapper<T>, ISoftDeleteStoreWrapper
    where TStore : IStore<T>
    where T : Data.Models.AbstractModel, ISoftDeletable
{
    protected readonly TStore _innerStore;
    protected readonly IDateTimeProvider _clock;

    public SoftDeleteStoreWrapper(TStore innerStore, IDateTimeProvider clock)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
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
        // SH-M331 (TASK-159): a soft-deleted row is not found, as reads do not see it; updating it would resurrect it.
        if (data.Guid != null && _innerStore.Read(data.Guid.Value)?.DeletedAt != null)
            throw new Birko.Data.Exceptions.EntityNotFoundException(typeof(T), data.Guid);
        _innerStore.Update(data, storeDelegate);
    }

    /// <summary>
    /// Soft-deletes the entity by setting DeletedAt to UtcNow.
    /// </summary>
    public void Delete(T data)
    {
        // SH-M325 (TASK-159): an already-deleted row keeps its original deletion instant.
        if (data.Guid != null && _innerStore.Read(data.Guid.Value)?.DeletedAt != null)
            return;
        SoftDeleteFilter.Stamp(data, _clock.UtcNow);
        _innerStore.Update(data);
    }

    public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        // TASK-649: a soft-deleted entity is refused, not created again under its Guid; Restore brings it back.
        if (data?.Guid is Guid guid && guid != Guid.Empty && _innerStore.Read(guid)?.DeletedAt != null)
            throw new EntitySoftDeletedException(typeof(T), guid);
        // TASK-555: the create-or-update decision has one producer; an unstored Guid is created, not lost.
        return StoreSave.Save(data, storeDelegate, Create, Update);
    }

    /// <summary>
    /// Brings a soft-deleted entity back: clears <c>DeletedAt</c> and writes <paramref name="data"/> as it now stands (TASK-649).
    /// A live entity is simply written.
    /// </summary>
    /// <exception cref="Birko.Data.Exceptions.EntityNotFoundException">No row is stored under the entity's Guid.</exception>
    public void Restore(T data)
    {
        if (data?.Guid is not Guid guid || guid == Guid.Empty)
            throw new ArgumentException("Restore needs the Guid of a stored entity.", nameof(data));
        if (_innerStore.Read(guid) == null)
            throw new Birko.Data.Exceptions.EntityNotFoundException(typeof(T), guid);
        data.DeletedAt = null;
        _innerStore.Update(data);
    }

    public void Init() => _innerStore.Init();
    public void Destroy() => _innerStore.Destroy();
    public T CreateInstance() => _innerStore.CreateInstance();

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;
}
