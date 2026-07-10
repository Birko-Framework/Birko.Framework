using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Bulk store wrapper that normalizes and ensures slug uniqueness on Create/Update.
/// </summary>
public class SluggableBulkStoreWrapper<TStore, T> : SluggableStoreWrapper<TStore, T>, IBulkStore<T>
    where TStore : IBulkStore<T>
    where T : Data.Models.AbstractModel, ISluggable
{
    public SluggableBulkStoreWrapper(TStore innerStore) : base(innerStore) { }

    public IEnumerable<T> Read() => Read(null, null, null, null);

    public IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
        => _innerStore.Read(filter, orderBy, limit, offset);

    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        // CR-M124: materialize once so the slug-mutated items are the ones persisted (a lazy source
        // enumerated twice would resolve slugs on discarded objects and persist unmutated ones).
        var items = data as IReadOnlyList<T> ?? data.ToList();
        var batchSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            ResolveSlug(item, excludeId: null, batchSlugs);
            batchSlugs.Add(item.Slug!);
        }
        _innerStore.Create(items, storeDelegate);
    }

    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var items = data as IReadOnlyList<T> ?? data.ToList();
        foreach (var item in items)
            ResolveSlug(item, item.Guid);
        _innerStore.Update(items, storeDelegate);
    }

    public void Update(Expression<Func<T, bool>> filter, Action<T> updateAction)
        => _innerStore.Update(filter, updateAction);

    public void Update(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates)
        => _innerStore.Update(filter, updates);

    public void Delete(IEnumerable<T> data) => _innerStore.Delete(data);

    public void Delete(Expression<Func<T, bool>> filter) => _innerStore.Delete(filter);
}
