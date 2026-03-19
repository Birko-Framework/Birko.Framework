using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Linq.Expressions;

namespace Birko.Telemetry;

/// <summary>
/// Store wrapper that adds metrics and tracing instrumentation to all <see cref="IStore{T}"/> operations.
/// </summary>
public class InstrumentedStoreWrapper<TStore, T> : IStore<T>, IStoreWrapper<T>
    where TStore : IStore<T>
    where T : Data.Models.AbstractModel
{
    protected readonly TStore _innerStore;
    protected readonly string _storeType;
    protected readonly string _entityType;

    public InstrumentedStoreWrapper(TStore innerStore)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _storeType = typeof(TStore).FullName ?? typeof(TStore).Name;
        _entityType = typeof(T).FullName ?? typeof(T).Name;
    }

    public void Init() => StoreInstrumentation.Execute(_storeType, _entityType, "Init", false, () => _innerStore.Init());

    public void Destroy() => StoreInstrumentation.Execute(_storeType, _entityType, "Destroy", false, () => _innerStore.Destroy());

    public T CreateInstance() => _innerStore.CreateInstance();

    public long Count(Expression<Func<T, bool>>? filter = null)
        => StoreInstrumentation.Execute(_storeType, _entityType, "Count", false, () => _innerStore.Count(filter));

    public T? Read(Guid guid)
        => StoreInstrumentation.Execute(_storeType, _entityType, "Read", false, () => _innerStore.Read(guid));

    public T? Read(Expression<Func<T, bool>>? filter = null)
        => StoreInstrumentation.Execute(_storeType, _entityType, "Read", false, () => _innerStore.Read(filter));

    public Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
        => StoreInstrumentation.Execute(_storeType, _entityType, "Create", false, () => _innerStore.Create(data, storeDelegate));

    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
        => StoreInstrumentation.Execute(_storeType, _entityType, "Update", false, () => _innerStore.Update(data, storeDelegate));

    public void Delete(T data)
        => StoreInstrumentation.Execute(_storeType, _entityType, "Delete", false, () => _innerStore.Delete(data));

    public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null)
        => StoreInstrumentation.Execute(_storeType, _entityType, "Save", false, () => _innerStore.Save(data, storeDelegate));

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;
}
