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
        // CR-M124: materialize once — the slug is resolved/mutated in the foreach and the same items
        // must be what's persisted; a lazy source enumerated twice would persist unmutated objects.
        var items = data as IReadOnlyList<T> ?? data.ToList();
        var batchSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            await ResolveSlugAsync(item, excludeId: null, batchSlugs, ct);
            batchSlugs.Add(item.Slug!);
        }
        await _innerStore.CreateAsync(items, storeDelegate, ct);
    }

    public async Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        // Resolve slugs sequentially against the shared inner store. Firing ResolveSlugAsync
        // concurrently (Task.WhenAll) issued overlapping ReadAsync calls on one store instance,
        // which connection-stateful backends (SQL connectors, file-based JSON/XML) cannot serve
        // safely; it also skipped per-batch uniqueness tracking so two updated rows could receive
        // the same slug, and the ContinueWith/Unwrap ran the inner update even if resolution
        // faulted (CR-H075). Materialize once so a lazy source isn't enumerated twice.
        var items = data as IReadOnlyList<T> ?? data.ToList();
        var batchSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            await ResolveSlugAsync(item, item.Guid, batchSlugs, ct);
            batchSlugs.Add(item.Slug!);
        }
        await _innerStore.UpdateAsync(items, storeDelegate, ct);
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
