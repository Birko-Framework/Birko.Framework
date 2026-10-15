using System;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.Patterns.Concurrency;

/// <summary>
/// Wraps an <see cref="IStore{T}"/> to enforce optimistic concurrency on entities
/// implementing <see cref="IVersioned"/>. Increments the version on create/update
/// and checks the version before updates to detect conflicts.
/// </summary>
/// <typeparam name="T">The type of entity, must implement <see cref="IVersioned"/>.</typeparam>
public class VersionedStoreWrapper<T> : IStore<T>, IStoreWrapper
    where T : AbstractModel, IVersioned
{
    private readonly IStore<T> _inner;

    public VersionedStoreWrapper(IStore<T> inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public void Init() => _inner.Init();
    public void Destroy() => _inner.Destroy();
    public T CreateInstance() => _inner.CreateInstance();
    public long Count(Expression<Func<T, bool>>? filter = null) => _inner.Count(filter);
    public T? Read(Guid guid) => _inner.Read(guid);
    public T? Read(Expression<Func<T, bool>>? filter = null) => _inner.Read(filter);
    public void Delete(T data) => _inner.Delete(data);

    public Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        data.Version = 1;
        return _inner.Create(data, storeDelegate);
    }

    // Best-effort optimistic concurrency: read-check-write at the wrapper level, NOT atomic. True
    // locking requires the inner store to enforce the version in its update predicate.
    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var existing = _inner.Read(data.Guid ?? Guid.Empty);
        // CR-M125: a missing row is a conflict, not a silent pass (which would lose-update via Version++).
        // SH-M435 (TASK-159): a missing row is NOT FOUND, the contract every store's Update keeps (TASK-555) and the one
        // StoreSave falls back to create on. As a conflict, Save of a new Guid threw instead of creating.
        if (existing == null)
        {
            throw new Birko.Data.Exceptions.EntityNotFoundException(typeof(T), data.Guid);
        }
        if (existing.Version != data.Version)
        {
            throw new ConcurrentUpdateException(typeof(T), data.Guid ?? Guid.Empty, data.Version);
        }

        // SH-M335 (TASK-159): a write that fails leaves the caller's entity at the version storage still has, so a retry
        // is a retry and not a permanent conflict.
        var version = data.Version;
        data.Version++;
        try
        {
            _inner.Update(data, storeDelegate);
        }
        catch
        {
            data.Version = version;
            throw;
        }
    }

    public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        // TASK-555: the create-or-update decision has one producer; an unstored Guid is created, not lost.
        return StoreSave.Save(data, storeDelegate, Create, Update);
    }

    public object? GetInnerStore() => _inner;
}
