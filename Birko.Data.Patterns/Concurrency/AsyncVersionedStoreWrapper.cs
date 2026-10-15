using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.Patterns.Concurrency;

/// <summary>
/// Wraps an <see cref="IAsyncStore{T}"/> to enforce optimistic concurrency on entities
/// implementing <see cref="IVersioned"/>. Increments the version on create/update
/// and checks the version before updates to detect conflicts.
/// </summary>
/// <typeparam name="T">The type of entity, must implement <see cref="IVersioned"/>.</typeparam>
public class AsyncVersionedStoreWrapper<T> : IAsyncStore<T>, IStoreWrapper
    where T : AbstractModel, IVersioned
{
    private readonly IAsyncStore<T> _inner;

    public AsyncVersionedStoreWrapper(IAsyncStore<T> inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public Task InitAsync(CancellationToken ct = default) => _inner.InitAsync(ct);
    public Task DestroyAsync(CancellationToken ct = default) => _inner.DestroyAsync(ct);
    public T CreateInstance() => _inner.CreateInstance();
    public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => _inner.CountAsync(filter, ct);
    public Task<T?> ReadAsync(Guid guid, CancellationToken ct = default) => _inner.ReadAsync(guid, ct);
    public Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => _inner.ReadAsync(filter, ct);
    public Task DeleteAsync(T data, CancellationToken ct = default) => _inner.DeleteAsync(data, ct);

    public Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        data.Version = 1;
        return _inner.CreateAsync(data, processDelegate, ct);
    }

    // NOTE: best-effort optimistic concurrency. This is a read-check-write at the wrapper level and is
    // NOT atomic — it narrows, but does not close, the race. True optimistic locking requires the inner
    // store to include the expected version in its update predicate (WHERE Version = @v / $match).
    public async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        var existing = await _inner.ReadAsync(data.Guid ?? Guid.Empty, ct);
        // CR-M125: a missing row is a conflict (not a silent pass) — previously a null read skipped the
        // version check entirely and the write proceeded with data.Version++, a lost-update window.
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
            await _inner.UpdateAsync(data, processDelegate, ct);
        }
        catch
        {
            data.Version = version;
            throw;
        }
    }

    public async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        // TASK-555: the create-or-update decision has one producer; an unstored Guid is created, not lost.
        return await StoreSave.SaveAsync(data, processDelegate, CreateAsync, UpdateAsync, ct);
    }

    public object? GetInnerStore() => _inner;
}
