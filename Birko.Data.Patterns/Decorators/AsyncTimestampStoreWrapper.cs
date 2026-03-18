using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Time;
using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Async store wrapper that automatically sets CreatedAt/UpdatedAt/PrevUpdatedAt timestamps.
/// </summary>
public class AsyncTimestampStoreWrapper<TStore, T> : IAsyncStore<T>, IStoreWrapper<T>
    where TStore : IAsyncStore<T>
    where T : AbstractModel, ITimestamped
{
    protected readonly TStore _innerStore;
    protected readonly IDateTimeProvider _clock;

    public AsyncTimestampStoreWrapper(TStore innerStore, IDateTimeProvider clock)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<T?> ReadAsync(Guid guid, CancellationToken ct = default) => _innerStore.ReadAsync(guid, ct);
    public Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => _innerStore.ReadAsync(filter, ct);
    public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => _innerStore.CountAsync(filter, ct);

    public Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        data.CreatedAt = now;
        data.UpdatedAt = now;
        data.PrevUpdatedAt = null;
        return _innerStore.CreateAsync(data, processDelegate, ct);
    }

    public Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        data.PrevUpdatedAt = data.UpdatedAt;
        data.UpdatedAt = _clock.UtcNow;
        return _innerStore.UpdateAsync(data, processDelegate, ct);
    }

    public Task DeleteAsync(T data, CancellationToken ct = default) => _innerStore.DeleteAsync(data, ct);

    public async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (data.Guid == null || data.Guid == Guid.Empty)
        {
            await CreateAsync(data, processDelegate, ct);
        }
        else
        {
            await UpdateAsync(data, processDelegate, ct);
        }
        return data.Guid ?? Guid.Empty;
    }

    public Task InitAsync(CancellationToken ct = default) => _innerStore.InitAsync(ct);
    public Task DestroyAsync(CancellationToken ct = default) => _innerStore.DestroyAsync(ct);
    public T CreateInstance() => _innerStore.CreateInstance();

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;
}
