using Birko.Data.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Async store wrapper that enforces only one entity can have IsDefault set to true.
/// When an entity is created or updated with IsDefault=true, all other entities
/// with IsDefault=true are automatically set to false.
/// Requires an async bulk store because enforcing the constraint needs bulk read and update.
/// </summary>
public class AsyncDefaultStoreWrapper<TStore, T> : IAsyncBulkStore<T>, IStoreWrapper<T>, IAsyncAggregatableStore<T>
    where TStore : IAsyncBulkStore<T>
    where T : AbstractModel, IDefault
{
    /// <summary>Forwards aggregation: this decorator does not scope reads (TASK-631).</summary>
    public Task<IReadOnlyList<AggregateResult>> AggregateAsync(AggregateQuery<T> query, CancellationToken ct = default)
        => StoreChain.AggregateThrough(_innerStore, query, ct);

    protected readonly TStore _innerStore;

    public AsyncDefaultStoreWrapper(TStore innerStore)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
    }

    public Task<T?> ReadAsync(Guid guid, CancellationToken ct = default) => ((IAsyncReadStore<T>)_innerStore).ReadAsync(guid, ct);
    public Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => ((IAsyncReadStore<T>)_innerStore).ReadAsync(filter, ct);
    public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => _innerStore.CountAsync(filter, ct);
    public Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default) => _innerStore.ReadAsync(ct);

    public Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        return _innerStore.ReadAsync(filter, orderBy, limit, offset, ct);
    }

    /// <summary>
    /// Writes tried before a refused promotion surfaces (TASK-632).
    /// </summary>
    /// <remarks>
    /// Unsetting the other defaults and then writing is a read-then-write, so two callers promoting two rows at once could
    /// both store a default (SH-M328). A model implementing <see cref="Birko.Data.Patterns.Models.IDefaultMarker"/> under a
    /// unique index makes the store refuse the second (<see cref="Birko.Data.Exceptions.StoreUniqueViolationException"/>);
    /// the wrapper then unsets the winner and writes again, so the last writer wins. A refusal while no other default is
    /// stored was for another key and surfaces at once. Without the marker and its index, or on a store that enforces no
    /// unique index (ElasticSearch, RavenDB, Cosmos DB, JSON, XML, InMemory), the window is narrowed, not closed.
    /// </remarks>
    protected const int MaxDefaultAttempts = 5;

    /// <summary>Keeps the marker in step: <c>true</c> on the default, <c>null</c> otherwise, never <c>false</c>.</summary>
    private static void Mark(T item)
    {
        if (item is Birko.Data.Patterns.Models.IDefaultMarker marked)
        {
            marked.DefaultMarker = item.IsDefault ? true : null;
        }
    }

    /// <summary>Non-defaults first: a unique index checks each statement, so a demotion must land before the promotion.</summary>
    private static List<T> DefaultLast(List<T> items) => items.OrderBy(i => i.IsDefault).ToList();

    /// <summary>The batch's single default after trimming: the last item marked default, or null.</summary>
    private static T? KeepLastDefault(List<T> items)
    {
        var defaultItems = items.Where(i => i.IsDefault).ToList();
        foreach (var item in defaultItems.SkipLast(1))
        {
            item.IsDefault = false;
        }
        return defaultItems.LastOrDefault();
    }

    public Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        => PromoteAsync(data, () => _innerStore.CreateAsync(data, processDelegate, ct), ct);

    public async Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        var items = data.ToList();
        var chosen = KeepLastDefault(items);
        for (var attempt = 1; ; attempt++)
        {
            if (chosen != null)
            {
                await UnsetOtherDefaultsAsync(null, ct: ct);
            }
            items.ForEach(Mark);
            try
            {
                await _innerStore.CreateAsync(items, storeDelegate, ct);
                return;
            }
            catch (Birko.Data.Exceptions.StoreUniqueViolationException) when (attempt < MaxDefaultAttempts && chosen != null)
            {
                // Retried only while nothing of the batch is stored: a non-atomic collection create may have written a prefix.
                if (await AnyStoredAsync(items, ct) || !await OtherDefaultStoredAsync(chosen.Guid, ct)) throw;
            }
        }
    }

    public Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        => PromoteAsync(data, async () => { await _innerStore.UpdateAsync(data, processDelegate, ct); return data.Guid ?? Guid.Empty; }, ct);

    public async Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        var items = data.ToList();
        var chosen = KeepLastDefault(items);
        var batch = items.Select(i => i.Guid).ToHashSet();
        for (var attempt = 1; ; attempt++)
        {
            if (chosen != null)
            {
                await UnsetOtherDefaultsAsync(chosen.Guid, batch, ct);
            }
            items.ForEach(Mark);
            try
            {
                await _innerStore.UpdateAsync(DefaultLast(items), storeDelegate, ct);
                return;
            }
            catch (Birko.Data.Exceptions.StoreUniqueViolationException) when (attempt < MaxDefaultAttempts && chosen != null)
            {
                // An update is idempotent, so a partly applied batch is safe to write again.
                if (!await OtherDefaultStoredAsync(chosen.Guid, ct)) throw;
            }
        }
    }

    /// <summary>
    /// Unsets the other defaults when <paramref name="data"/> is one, writes it, and on a refused promotion does both
    /// again (TASK-632).
    /// </summary>
    private async Task<Guid> PromoteAsync(T data, Func<Task<Guid>> write, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (data.IsDefault)
            {
                await UnsetOtherDefaultsAsync(data.Guid, ct: ct);
            }
            Mark(data);
            try
            {
                return await write();
            }
            catch (Birko.Data.Exceptions.StoreUniqueViolationException) when (attempt < MaxDefaultAttempts && data.IsDefault)
            {
                if (!await OtherDefaultStoredAsync(data.Guid, ct)) throw;
            }
        }
    }

    private async Task<bool> OtherDefaultStoredAsync(Guid? guid, CancellationToken ct)
        => (guid == null
            ? await _innerStore.ReadAsync(e => e.IsDefault, null, 1, null, ct)
            : await _innerStore.ReadAsync(e => e.IsDefault && e.Guid != guid, null, 1, null, ct)).Any();

    private async Task<bool> AnyStoredAsync(List<T> items, CancellationToken ct)
    {
        var guids = items.Where(i => i.Guid != null).Select(i => i.Guid).ToList();
        return guids.Count > 0 && await _innerStore.CountAsync(x => guids.Contains(x.Guid), ct) > 0;
    }

    public async Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default)
    {
        // SH-M329/M330 (TASK-159): one collection write, as the sync twin does (atomic on SQL, TASK-629). One write per row
        // left an arbitrary prefix updated on a failure, and every matched row the action made default stayed default.
        RequireFilter(filter, "update");
        var items = (await _innerStore.ReadAsync(filter, null, null, null, ct)).ToList();
        foreach (var item in items)
        {
            updateAction(item);
        }
        if (items.Count > 0)
        {
            // TASK-632: the collection update keeps one default, unsets the rest, keeps the marker and retries.
            await UpdateAsync(items, storeDelegate: null, ct);
        }
    }

    public Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default)
    {
        // SH-M320 (TASK-159): an update that writes IsDefault is enforced like the Action overload; any other is native.
        if (DefaultStoreWrapper<IBulkStore<T>, T>.WritesIsDefault(updates))
        {
            return UpdateAsync(filter, item => updates.ApplyTo(item), ct);
        }
        return _innerStore.UpdateAsync(filter, updates, ct);
    }

    public Task DeleteAsync(T data, CancellationToken ct = default) => _innerStore.DeleteAsync(data, ct);
    public Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default) => _innerStore.DeleteAsync(data, ct);
    public Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default) => _innerStore.DeleteAsync(filter, ct);

    public async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        // TASK-555: the create-or-update decision has one producer; an unstored Guid is created, not lost.
        return await PromoteAsync(data, () => StoreSave.SaveAsync(data, processDelegate, _innerStore.CreateAsync, _innerStore.UpdateAsync, ct), ct);
    }

    public Task InitAsync(CancellationToken ct = default) => _innerStore.InitAsync(ct);
    public Task DestroyAsync(CancellationToken ct = default) => _innerStore.DestroyAsync(ct);
    public T CreateInstance() => _innerStore.CreateInstance();

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;

    /// <summary>
    /// Of a batch, only the last item marked default stays default; every other stored default is unset.
    /// </summary>
    private static void RequireFilter(Expression<Func<T, bool>> filter, string operation)
    {
        if (filter is null)
            throw new ArgumentNullException(nameof(filter), $"A filtered {operation} on {typeof(T).Name} needs a filter.");
        Birko.Data.Expressions.BoundedFilterGuard.Require(filter, operation, typeof(T).Name, "UpdateAllAsync(updateAction)");
    }

    /// <summary>
    /// Finds all entities with IsDefault=true (excluding the given entity) and sets them to false.
    /// </summary>
    protected async Task UnsetOtherDefaultsAsync(Guid? excludeGuid, HashSet<Guid?>? alsoExcludeGuids = null, CancellationToken ct = default)
    {
        // SH-M324 (TASK-159): a null exclusion excludes nothing; see the sync twin.
        var allDefaults = await _innerStore.ReadAsync(e => e.IsDefault, orderBy: null, limit: null, offset: null, ct);
        var others = allDefaults.Where(e => excludeGuid == null || e.Guid != excludeGuid);
        if (alsoExcludeGuids != null)
        {
            others = others.Where(e => e.Guid == null || !alsoExcludeGuids.Contains(e.Guid));
        }

        var toUpdate = others.ToList();
        if (toUpdate.Count == 0)
        {
            return;
        }

        foreach (var item in toUpdate)
        {
            item.IsDefault = false;
            Mark(item);
        }
        await _innerStore.UpdateAsync(toUpdate, storeDelegate: null, ct);
    }
}
