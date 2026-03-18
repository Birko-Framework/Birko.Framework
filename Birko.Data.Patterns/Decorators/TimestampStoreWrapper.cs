using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Time;
using System;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Store wrapper that automatically sets CreatedAt/UpdatedAt/PrevUpdatedAt timestamps.
/// </summary>
public class TimestampStoreWrapper<TStore, T> : IStore<T>, IStoreWrapper<T>
    where TStore : IStore<T>
    where T : AbstractModel, ITimestamped
{
    protected readonly TStore _innerStore;
    protected readonly IDateTimeProvider _clock;

    public TimestampStoreWrapper(TStore innerStore, IDateTimeProvider clock)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public T? Read(Guid guid) => _innerStore.Read(guid);
    public T? Read(Expression<Func<T, bool>>? filter = null) => _innerStore.Read(filter);
    public long Count(Expression<Func<T, bool>>? filter = null) => _innerStore.Count(filter);

    public Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var now = _clock.UtcNow;
        data.CreatedAt = now;
        data.UpdatedAt = now;
        data.PrevUpdatedAt = null;
        return _innerStore.Create(data, storeDelegate);
    }

    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        data.PrevUpdatedAt = data.UpdatedAt;
        data.UpdatedAt = _clock.UtcNow;
        _innerStore.Update(data, storeDelegate);
    }

    public void Delete(T data) => _innerStore.Delete(data);

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
