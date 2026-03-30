using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Async bulk store wrapper that normalizes and ensures slug uniqueness on Create/Update.
/// </summary>
public class AsyncSluggableBulkStoreWrapper<TStore, T> : AsyncSluggableStoreWrapper<TStore, T>, IAsyncBulkStore<T>
    where TStore : IAsyncBulkStore<T>
    where T : Data.Models.AbstractModel, ISluggable
{
    public AsyncSluggableBulkStoreWrapper(TStore innerStore) : base(innerStore) { }

    public Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default)
        => _innerStore.ReadAsync(ct);

    public Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
        => _innerStore.ReadAsync(filter, orderBy, limit, offset, ct);

    public async Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        var batchSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in data)
        {
            await ResolveSlugAsync(item, excludeId: null, batchSlugs, ct);
            batchSlugs.Add(item.Slug!);
        }
        await _innerStore.CreateAsync(data, storeDelegate, ct);
    }

    public Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        // Resolve each item's slug individually — batch tracking not needed for updates
        // since each item already has a Guid that excludes itself from uniqueness check
        return Task.WhenAll(data.Select(async item =>
        {
            await ResolveSlugAsync(item, item.Guid, ct: ct);
        })).ContinueWith(_ => _innerStore.UpdateAsync(data, storeDelegate, ct), ct).Unwrap();
    }

    public Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default)
        => _innerStore.UpdateAsync(filter, updateAction, ct);

    public Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default)
        => _innerStore.UpdateAsync(filter, updates, ct);

    public Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default)
        => _innerStore.DeleteAsync(data, ct);

    public Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default)
        => _innerStore.DeleteAsync(filter, ct);
}
