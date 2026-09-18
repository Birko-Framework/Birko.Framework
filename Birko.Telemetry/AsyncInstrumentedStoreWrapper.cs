using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Telemetry;

/// <summary>
/// Store wrapper that adds metrics and tracing instrumentation to all <see cref="IAsyncStore{T}"/> operations.
/// </summary>
public class AsyncInstrumentedStoreWrapper<TStore, T> : IAsyncStore<T>, IStoreWrapper<T>
    where TStore : IAsyncStore<T>
    where T : Data.Models.AbstractModel
{
    protected readonly TStore _innerStore;
    protected readonly string _storeType;
    protected readonly string _entityType;

    public AsyncInstrumentedStoreWrapper(TStore innerStore)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _storeType = typeof(TStore).FullName ?? typeof(TStore).Name;
        _entityType = typeof(T).FullName ?? typeof(T).Name;
    }

    public Task InitAsync(CancellationToken ct = default)
        => StoreInstrumentation.ExecuteAsync(_storeType, _entityType, "Init", false, () => _innerStore.InitAsync(ct));

    public Task DestroyAsync(CancellationToken ct = default)
        => StoreInstrumentation.ExecuteAsync(_storeType, _entityType, "Destroy", false, () => _innerStore.DestroyAsync(ct));

    public T CreateInstance() => _innerStore.CreateInstance();

    public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        => StoreInstrumentation.ExecuteAsync(_storeType, _entityType, "Count", false, () => _innerStore.CountAsync(filter, ct));

    public Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
        => StoreInstrumentation.ExecuteAsync(_storeType, _entityType, "Read", false, () => _innerStore.ReadAsync(guid, ct));

    public Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        => StoreInstrumentation.ExecuteAsync(_storeType, _entityType, "Read", false, () => _innerStore.ReadAsync(filter, ct));

    public Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        => StoreInstrumentation.ExecuteAsync(_storeType, _entityType, "Create", false, () => _innerStore.CreateAsync(data, processDelegate, ct));

    public Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        => StoreInstrumentation.ExecuteAsync(_storeType, _entityType, "Update", false, () => _innerStore.UpdateAsync(data, processDelegate, ct));

    public Task DeleteAsync(T data, CancellationToken ct = default)
        => StoreInstrumentation.ExecuteAsync(_storeType, _entityType, "Delete", false, () => _innerStore.DeleteAsync(data, ct));

    public Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        => StoreInstrumentation.ExecuteAsync(_storeType, _entityType, "Save", false, () => _innerStore.SaveAsync(data, processDelegate, ct));

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;
}
