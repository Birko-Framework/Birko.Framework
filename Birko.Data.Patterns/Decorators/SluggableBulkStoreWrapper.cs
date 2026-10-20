using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
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
        for (var attempt = 1; ; attempt++)
        {
            var batchSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                ResolveSlug(item, excludeId: null, batchSlugs);
                batchSlugs.Add(item.Slug!);
            }
            try
            {
                // TASK-694: a savepoint only while this attempt can still be retried; a null store runs the write as is.
                Birko.Data.Stores.RetryableWrite.Run(attempt < MaxSlugAttempts ? _innerStore : null, () => _innerStore.Create(items, storeDelegate));
                return;
            }
            // TASK-632: retried only while nothing of the batch is stored and a slug is what collided (see the async twin).
            catch (Birko.Data.Exceptions.StoreUniqueViolationException violation) when (attempt < MaxSlugAttempts)
            {
                // TASK-685: probed in the body, not the filter, so a failed probe surfaces through ProbeAfterRefusal.
                if (Birko.Data.Exceptions.StoreException.ProbeAfterRefusal(() => AnyStored(items), violation) || !Birko.Data.Exceptions.StoreException.ProbeAfterRefusal(() => AnySlugHeld(items), violation)) throw;
            }
        }
    }

    private bool AnyStored(IReadOnlyList<T> items)
    {
        var guids = items.Where(i => i.Guid != null).Select(i => i.Guid).ToList();
        return guids.Count > 0 && _innerStore.Count(x => guids.Contains(x.Guid)) > 0;
    }

    private bool AnySlugHeld(IReadOnlyList<T> items) => items.Any(i => SlugHeld(i.Slug, i.Guid));

    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var items = data as IReadOnlyList<T> ?? data.ToList();
        // SH-M319 (TASK-159): the batch's own slugs count, as in Create and in the async twin; two rows of one batch
        // re-resolving to "widget" both kept it.
        for (var attempt = 1; ; attempt++)
        {
            var batchSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                ResolveSlug(item, item.Guid, batchSlugs);
                batchSlugs.Add(item.Slug!);
            }
            try
            {
                // TASK-694: a savepoint only while this attempt can still be retried; a null store runs the write as is.
                Birko.Data.Stores.RetryableWrite.Run(attempt < MaxSlugAttempts ? _innerStore : null, () => _innerStore.Update(items, storeDelegate));
                return;
            }
            // An update is idempotent, so a partly applied batch is safe to write again (TASK-632).
            catch (Birko.Data.Exceptions.StoreUniqueViolationException violation) when (attempt < MaxSlugAttempts)
            {
                // TASK-685: probed in the body, not the filter, so a failed probe surfaces through ProbeAfterRefusal.
                if (!Birko.Data.Exceptions.StoreException.ProbeAfterRefusal(() => AnySlugHeld(items), violation)) throw;
            }
        }
    }

    public void Update(Expression<Func<T, bool>> filter, Action<T> updateAction)
        => _innerStore.Update(filter, updateAction);

    public void Update(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates)
        => _innerStore.Update(filter, updates);

    public void Delete(IEnumerable<T> data) => _innerStore.Delete(data);

    public void Delete(Expression<Func<T, bool>> filter) => _innerStore.Delete(filter);
}
