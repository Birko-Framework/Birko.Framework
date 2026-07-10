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
        if (existing == null || existing.Version != data.Version)
        {
            throw new ConcurrentUpdateException(typeof(T), data.Guid ?? Guid.Empty, data.Version);
        }

        data.Version++;
        await _inner.UpdateAsync(data, processDelegate, ct);
    }

    public async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (data.Guid == null || data.Guid == Guid.Empty)
        {
            return await CreateAsync(data, processDelegate, ct);
        }

        await UpdateAsync(data, processDelegate, ct);
        return data.Guid.Value;
    }

    public object? GetInnerStore() => _inner;
}
