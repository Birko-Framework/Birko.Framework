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

    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var existing = _inner.Read(data.Guid ?? Guid.Empty);
        if (existing != null && existing.Version != data.Version)
        {
            throw new ConcurrentUpdateException(typeof(T), data.Guid ?? Guid.Empty, data.Version);
        }

        data.Version++;
        _inner.Update(data, storeDelegate);
    }

    public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (data.Guid == null || data.Guid == Guid.Empty)
        {
            return Create(data, storeDelegate);
        }

        Update(data, storeDelegate);
        return data.Guid.Value;
    }

    public object? GetInnerStore() => _inner;
}
