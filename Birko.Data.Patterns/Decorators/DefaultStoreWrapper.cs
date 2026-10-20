using Birko.Data.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Store wrapper that enforces only one entity can have IsDefault set to true.
/// When an entity is created or updated with IsDefault=true, all other entities
/// with IsDefault=true are automatically set to false.
/// Requires a bulk store because enforcing the constraint needs bulk read and update.
/// </summary>
public class DefaultStoreWrapper<TStore, T> : IBulkStore<T>, IStoreWrapper<T>
    where TStore : IBulkStore<T>
    where T : AbstractModel, IDefault
{
    protected readonly TStore _innerStore;

    public DefaultStoreWrapper(TStore innerStore)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
    }

    public T? Read(Guid guid) => ((IReadStore<T>)_innerStore).Read(guid);
    public T? Read(Expression<Func<T, bool>>? filter = null) => ((IReadStore<T>)_innerStore).Read(filter);
    public long Count(Expression<Func<T, bool>>? filter = null) => _innerStore.Count(filter);
    public IEnumerable<T> Read() => _innerStore.Read();

    public IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
    {
        return _innerStore.Read(filter, orderBy, limit, offset);
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

    public Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
        => Promote(data, () => _innerStore.Create(data, storeDelegate));

    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var items = data.ToList();
        var chosen = KeepLastDefault(items);
        for (var attempt = 1; ; attempt++)
        {
            if (chosen != null)
            {
                UnsetOtherDefaults(null);
            }
            items.ForEach(Mark);
            try
            {
                _innerStore.Create(items, storeDelegate);
                return;
            }
            // Retried only while nothing of the batch is stored: a non-atomic collection create may have written a prefix.
            catch (Birko.Data.Exceptions.StoreUniqueViolationException violation) when (attempt < MaxDefaultAttempts && chosen != null)
            {
                // TASK-685: probed in the body, not the filter, so a failed probe surfaces through ProbeAfterRefusal.
                if (Birko.Data.Exceptions.StoreException.ProbeAfterRefusal(() => AnyStored(items), violation) || !Birko.Data.Exceptions.StoreException.ProbeAfterRefusal(() => OtherDefaultStored(chosen.Guid), violation)) throw;
            }
        }
    }

    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
        => Promote(data, () => { _innerStore.Update(data, storeDelegate); return data.Guid ?? Guid.Empty; });

    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var items = data.ToList();
        var chosen = KeepLastDefault(items);
        var batch = items.Select(i => i.Guid).ToHashSet();
        for (var attempt = 1; ; attempt++)
        {
            if (chosen != null)
            {
                UnsetOtherDefaults(chosen.Guid, batch);
            }
            items.ForEach(Mark);
            try
            {
                _innerStore.Update(DefaultLast(items), storeDelegate);
                return;
            }
            // An update is idempotent, so a partly applied batch is safe to write again.
            catch (Birko.Data.Exceptions.StoreUniqueViolationException violation) when (attempt < MaxDefaultAttempts && chosen != null)
            {
                // TASK-685: probed in the body, not the filter, so a failed probe surfaces through ProbeAfterRefusal.
                if (!Birko.Data.Exceptions.StoreException.ProbeAfterRefusal(() => OtherDefaultStored(chosen.Guid), violation)) throw;
            }
        }
    }

    /// <summary>
    /// Unsets the other defaults when <paramref name="data"/> is one, writes it, and on a refused promotion does both
    /// again (TASK-632).
    /// </summary>
    private Guid Promote(T data, Func<Guid> write)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (data.IsDefault)
            {
                UnsetOtherDefaults(data.Guid);
            }
            Mark(data);
            try
            {
                return write();
            }
            catch (Birko.Data.Exceptions.StoreUniqueViolationException violation) when (attempt < MaxDefaultAttempts && data.IsDefault)
            {
                // TASK-685: probed in the body, not the filter, so a failed probe surfaces through ProbeAfterRefusal.
                if (!Birko.Data.Exceptions.StoreException.ProbeAfterRefusal(() => OtherDefaultStored(data.Guid), violation)) throw;
            }
        }
    }

    private bool OtherDefaultStored(Guid? guid)
        => guid == null
            ? _innerStore.Read(e => e.IsDefault, null, 1, null).Any()
            : _innerStore.Read(e => e.IsDefault && e.Guid != guid, null, 1, null).Any();

    private bool AnyStored(List<T> items)
    {
        var guids = items.Where(i => i.Guid != null).Select(i => i.Guid).ToList();
        return guids.Count > 0 && _innerStore.Count(x => guids.Contains(x.Guid)) > 0;
    }

    public void Update(Expression<Func<T, bool>> filter, Action<T> updateAction)
    {
        // SH-M329/M330 (TASK-159): read the matches, apply, then write once, as the async twin does. Unsetting the other
        // defaults from inside the inner store's update callback was a nested write in an in-flight operation, and
        // every matched row that the action made default stayed default (each unset excluded only itself).
        RequireFilter(filter, "update");
        var items = _innerStore.Read(filter, null, null, null).ToList();
        foreach (var item in items)
        {
            updateAction(item);
        }
        if (items.Count > 0)
        {
            // TASK-632: the collection update keeps one default, unsets the rest, keeps the marker and retries.
            Update(items);
        }
    }

    public void Update(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates)
    {
        // SH-M320 (TASK-159): an update that writes IsDefault is enforced like the Action overload; a native SET of
        // IsDefault = true over N rows left N defaults. Any other update stays native.
        if (WritesIsDefault(updates))
        {
            Update(filter, item => updates.ApplyTo(item));
            return;
        }
        _innerStore.Update(filter, updates);
    }

    public void Delete(T data) => _innerStore.Delete(data);
    public void Delete(IEnumerable<T> data) => _innerStore.Delete(data);
    public void Delete(Expression<Func<T, bool>> filter) => _innerStore.Delete(filter);

    public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        // TASK-555: the create-or-update decision has one producer; an unstored Guid is created, not lost.
        return Promote(data, () => StoreSave.Save(data, storeDelegate, _innerStore.Create, _innerStore.Update));
    }

    public void Init() => _innerStore.Init();
    public void Destroy() => _innerStore.Destroy();
    public T CreateInstance() => _innerStore.CreateInstance();

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;

    /// <summary>
    /// Of a batch, only the last item marked default stays default; every other stored default is unset.
    /// </summary>
    internal static bool WritesIsDefault(PropertyUpdate<T> updates) =>
        updates.Assignments.Any(a => a.MemberPath == nameof(IDefault.IsDefault));

    private static void RequireFilter(Expression<Func<T, bool>> filter, string operation)
    {
        if (filter is null)
            throw new ArgumentNullException(nameof(filter), $"A filtered {operation} on {typeof(T).Name} needs a filter.");
        Birko.Data.Expressions.BoundedFilterGuard.Require(filter, operation, typeof(T).Name, "UpdateAll(updateAction)");
    }

    /// <summary>
    /// Finds all entities with IsDefault=true (excluding the given entity) and sets them to false.
    /// </summary>
    protected void UnsetOtherDefaults(Guid? excludeGuid, HashSet<Guid?>? alsoExcludeGuids = null)
    {
        // SH-M324 (TASK-159): a null exclusion excludes nothing. Comparing Guid != null skipped any stored default whose
        // Guid was null, and a null in the batch's set did the same, leaving two defaults.
        var others = _innerStore.Read(e => e.IsDefault, orderBy: null, limit: null, offset: null)
            .Where(e => excludeGuid == null || e.Guid != excludeGuid);
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
        _innerStore.Update(toUpdate);
    }
}
