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

    public Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (data.IsDefault)
        {
            UnsetOtherDefaults(data.Guid);
        }
        return _innerStore.Create(data, storeDelegate);
    }

    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var items = data.ToList();
        if (items.Any(i => i.IsDefault))
        {
            UnsetOtherDefaults(null);
            // Ensure only the last item marked as default stays default
            var defaultItems = items.Where(i => i.IsDefault).ToList();
            foreach (var item in defaultItems.SkipLast(1))
            {
                item.IsDefault = false;
            }
        }
        _innerStore.Create(items, storeDelegate);
    }

    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (data.IsDefault)
        {
            UnsetOtherDefaults(data.Guid);
        }
        _innerStore.Update(data, storeDelegate);
    }

    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var items = data.ToList();
        if (items.Any(i => i.IsDefault))
        {
            var defaultItems = items.Where(i => i.IsDefault).ToList();
            // Keep only the last one as default
            foreach (var item in defaultItems.SkipLast(1))
            {
                item.IsDefault = false;
            }
            var lastDefault = defaultItems.Last();
            UnsetOtherDefaults(lastDefault.Guid, items.Select(i => i.Guid).ToHashSet());
        }
        _innerStore.Update(items, storeDelegate);
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
        KeepLastDefault(items);
        if (items.Count > 0)
        {
            _innerStore.Update(items);
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
        if (data.IsDefault)
        {
            UnsetOtherDefaults(data.Guid);
        }
        // TASK-555: the create-or-update decision has one producer; an unstored Guid is created, not lost.
        return StoreSave.Save(data, storeDelegate, _innerStore.Create, _innerStore.Update);
    }

    public void Init() => _innerStore.Init();
    public void Destroy() => _innerStore.Destroy();
    public T CreateInstance() => _innerStore.CreateInstance();

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;

    /// <summary>
    /// Of a batch, only the last item marked default stays default; every other stored default is unset.
    /// </summary>
    private void KeepLastDefault(List<T> items)
    {
        var defaultItems = items.Where(i => i.IsDefault).ToList();
        if (defaultItems.Count == 0)
        {
            return;
        }
        foreach (var item in defaultItems.SkipLast(1))
        {
            item.IsDefault = false;
        }
        UnsetOtherDefaults(defaultItems.Last().Guid, items.Select(i => i.Guid).ToHashSet());
    }

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
        }
        _innerStore.Update(toUpdate);
    }
}
