using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Time;
using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Async store wrapper that adds soft-delete behavior.
/// </summary>
public class AsyncSoftDeleteStoreWrapper<TStore, T> : IAsyncStore<T>, IStoreWrapper<T>, ISoftDeleteStoreWrapper
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

    public async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        // SH-M331 (TASK-159): a soft-deleted row is not found, as reads do not see it; updating it would resurrect it.
        if (data.Guid != null && (await _innerStore.ReadAsync(data.Guid.Value, ct))?.DeletedAt != null)
            throw new Birko.Data.Exceptions.EntityNotFoundException(typeof(T), data.Guid);
        await _innerStore.UpdateAsync(data, processDelegate, ct);
    }

    /// <summary>
    /// Soft-deletes the entity by setting DeletedAt to UtcNow.
    /// </summary>
    public async Task DeleteAsync(T data, CancellationToken ct = default)
    {
        // SH-M325 (TASK-159): an already-deleted row keeps its original deletion instant.
        if (data.Guid != null && (await _innerStore.ReadAsync(data.Guid.Value, ct))?.DeletedAt != null)
            return;
        SoftDeleteFilter.Stamp(data, _clock.UtcNow);
        await _innerStore.UpdateAsync(data, ct: ct);
    }

    public async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        // TASK-649: a soft-deleted entity is refused, not created again under its Guid; RestoreAsync brings it back.
        if (data?.Guid is Guid guid && guid != Guid.Empty && (await _innerStore.ReadAsync(guid, ct))?.DeletedAt != null)
            throw new EntitySoftDeletedException(typeof(T), guid);
        // TASK-555: the create-or-update decision has one producer; an unstored Guid is created, not lost.
        return await StoreSave.SaveAsync(data, processDelegate, CreateAsync, UpdateAsync, ct);
    }

    /// <summary>
    /// Brings a soft-deleted entity back: clears <c>DeletedAt</c> and writes <paramref name="data"/> as it now stands (TASK-649).
    /// A live entity is simply written.
    /// </summary>
    /// <exception cref="Birko.Data.Exceptions.EntityNotFoundException">No row is stored under the entity's Guid.</exception>
    public async Task RestoreAsync(T data, CancellationToken ct = default)
    {
        if (data?.Guid is not Guid guid || guid == Guid.Empty)
            throw new ArgumentException("Restore needs the Guid of a stored entity.", nameof(data));
        if (await _innerStore.ReadAsync(guid, ct) == null)
            throw new Birko.Data.Exceptions.EntityNotFoundException(typeof(T), guid);
        data.DeletedAt = null;
        await _innerStore.UpdateAsync(data, ct: ct);
    }

    public Task InitAsync(CancellationToken ct = default) => _innerStore.InitAsync(ct);
    public Task DestroyAsync(CancellationToken ct = default) => _innerStore.DestroyAsync(ct);
    public T CreateInstance() => _innerStore.CreateInstance();

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;
}
