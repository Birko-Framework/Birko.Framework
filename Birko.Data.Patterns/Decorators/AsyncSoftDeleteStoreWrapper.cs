using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using Birko.Time;
using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Async store wrapper that adds soft-delete behavior.
/// </summary>
public class AsyncSoftDeleteStoreWrapper<TStore, T> : IAsyncStore<T>, IStoreWrapper<T>
    where TStore : IAsyncStore<T>
    where T : Data.Models.AbstractModel, ISoftDeletable
{
    protected readonly TStore _innerStore;
    protected readonly IDateTimeProvider _clock;

    public AsyncSoftDeleteStoreWrapper(TStore innerStore, IDateTimeProvider clock)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
    {
        var entity = await _innerStore.ReadAsync(guid, ct);
        return entity?.DeletedAt == null ? entity : null;
    }

    public Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        return _innerStore.ReadAsync(SoftDeleteFilter.CombineWithNotDeleted(filter), ct);
    }

    public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        return _innerStore.CountAsync(SoftDeleteFilter.CombineWithNotDeleted(filter), ct);
    }

    public Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        data.DeletedAt = null;
        return _innerStore.CreateAsync(data, processDelegate, ct);
    }

    public Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        return _innerStore.UpdateAsync(data, processDelegate, ct);
    }

    /// <summary>
    /// Soft-deletes the entity by setting DeletedAt to UtcNow.
    /// </summary>
    public Task DeleteAsync(T data, CancellationToken ct = default)
    {
        data.DeletedAt = _clock.UtcNow;
        return _innerStore.UpdateAsync(data, ct: ct);
    }

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
