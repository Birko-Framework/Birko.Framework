using Birko.Data.Localization.Expressions;
using Birko.Data.Localization.Filters;
using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Birko.Data.Localization.Decorators;

/// <summary>
/// Sync bulk store wrapper that applies entity localization to bulk read/write operations.
/// Rewrites filter expressions for localized field conditions and handles
/// in-memory ordering/pagination when OrderBy references localized fields.
/// </summary>
public class LocalizedBulkStoreWrapper<TStore, T> : IBulkStore<T>, IStoreWrapper<T>
    where TStore : IBulkStore<T>
    where T : Data.Models.AbstractModel, ILocalizable
{
    protected readonly TStore _innerStore;
    protected readonly IBulkStore<EntityTranslationModel> _translationStore;
    protected readonly IEntityLocalizationContext _context;
    protected readonly IReadOnlyList<string> _localizableFields;

    public LocalizedBulkStoreWrapper(
        TStore innerStore,
        IBulkStore<EntityTranslationModel> translationStore,
        IEntityLocalizationContext context)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _translationStore = translationStore ?? throw new ArgumentNullException(nameof(translationStore));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        // SH-L079 / SH-L082 (TASK-194): the declaration is read and validated once, at wiring, on every culture.
        _localizableFields = LocalizedEntityFields.LocalizableFieldsOf(_innerStore.CreateInstance());
    }

    // Singular read with localization
    public T? Read(Guid guid)
    {
        var entity = _innerStore.Read(guid);
        return entity == null ? null : Localize(entity);
    }

    public T? Read(Expression<Func<T, bool>>? filter = null)
    {
        if (!IsNonDefaultCulture())
        {
            return ((IReadStore<T>)_innerStore).Read(filter);
        }

        var rewritten = RewriteFilter(filter);
        var result = ((IReadStore<T>)_innerStore).Read(rewritten);
        return result == null ? null : Localize(result);
    }

    // Bulk read with localization
    public IEnumerable<T> Read()
    {
        var entities = _innerStore.Read().ToList();
        if (!IsNonDefaultCulture())
        {
            return entities;
        }
        return LocalizeAll(entities);
    }

    public IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
    {
        if (!IsNonDefaultCulture())
        {
            return _innerStore.Read(filter, orderBy, limit, offset).ToList();
        }

        var rewritten = RewriteFilter(filter);
        var localizableFields = GetLocalizableFieldsFromInstance();
        var needsInMemorySort = LocalizedOrderByHelper.ReferencesLocalizedField(orderBy, localizableFields);

        if (!needsInMemorySort)
        {
            // OrderBy is on non-localized fields — safe to pass to inner store
            var entities = _innerStore.Read(rewritten, orderBy, limit, offset).ToList();
            return LocalizeAll(entities);
        }

        // OrderBy references localized fields — fetch all matching, translate, sort, paginate in memory
        var allEntities = LocalizeAll(_innerStore.Read(rewritten).ToList());

        if (orderBy != null)
        {
            allEntities = LocalizedOrderByHelper.ApplyInMemoryOrderBy(allEntities, orderBy, _context.CurrentCulture);
        }

        return LocalizedOrderByHelper.ApplyInMemoryPaging(allEntities, offset, limit);
    }

    public long Count(Expression<Func<T, bool>>? filter = null)
    {
        if (!IsNonDefaultCulture())
        {
            return _innerStore.Count(filter);
        }

        var rewritten = RewriteFilter(filter);
        return _innerStore.Count(rewritten);
    }

    public Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        LocalizedEntityFields.RequireValidDeclarations(new[] { data });
        var guid = _innerStore.Create(data, storeDelegate);
        // SH-M076: keyed by the GUID the store returned — it need not have written it onto the entity.
        SaveTranslations(guid, LocalizedTranslationRows.ValuesOf(data));
        return guid;
    }

    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        // CR-M100: materialize once — a lazy/one-shot IEnumerable would otherwise be enumerated
        // twice (once by the inner store, once here), re-running or exhausting the source.
        var items = data as IList<T> ?? data.ToList();
        LocalizedEntityFields.RequireValidDeclarations(items);
        _innerStore.Create(items, storeDelegate);
        foreach (var entity in items)
        {
            SaveTranslations(entity);
        }
    }

    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T}.Update(T, StoreDataDelegate{T}?)" />
    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        LocalizedEntityFields.RequireValidDeclarations(new[] { data });
        if (!IsNonDefaultCulture())
        {
            _innerStore.Update(data, storeDelegate);
            SaveTranslations(data);
            return;
        }

        var fields = LocalizedEntityFields.LocalizableFieldsOf(data);
        var baseValues = ReadBaseValues(data.Guid, fields);

        // The entity handed to the inner store is a DETACHED copy carrying the base values, not the
        // caller's object with its values swapped and swapped back. A store may keep the reference it
        // is given — the test double and Birko.Data.InMemory both do — so restoring the caller's text
        // afterwards would write it straight through into the store, reinstating the very defect this
        // is fixing. Measured: the restore-in-place version failed 4 of these tests.
        _innerStore.Update(WithBaseValues(data, baseValues), storeDelegate);
        SaveTranslations(data);
    }

    /// <summary>
    /// SH-H015, the collection shape. Base values for the whole batch are fetched in <b>one</b> read
    /// rather than one per entity, so preserving the base column does not turn a bulk update into N
    /// round-trips.
    /// </summary>
    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        var items = data as IList<T> ?? data.ToList();
        LocalizedEntityFields.RequireValidDeclarations(items);

        if (!IsNonDefaultCulture())
        {
            _innerStore.Update(items, storeDelegate);
            foreach (var entity in items)
            {
                SaveTranslations(entity);
            }
            return;
        }

        var fields = GetLocalizableFieldsFromInstance();
        var baseValues = ReadBaseValues(items, fields);

        _innerStore.Update(WithBaseValues(items, baseValues), storeDelegate);

        foreach (var entity in items)
        {
            SaveTranslations(entity);
        }
    }

    public void Delete(T data)
    {
        _innerStore.Delete(data);
        DeleteTranslations(data);
    }

    /// <summary>
    /// Filter-based update. The filter is resolved against the translation store exactly as a read
    /// resolves it, and the base column keeps the default-culture value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SH-H018: every read path called <see cref="RewriteFilter"/> and none of the write paths did, so
    /// under a non-default culture <c>Update(x =&gt; x.Name == "Stolicka", …)</c> was matched against the
    /// untranslated base column — a write that silently disagreed with the equivalent <c>Read</c> about
    /// which rows it covered.
    /// </para>
    /// <para>
    /// SH-H017: the action mutates the entity the inner store loaded and the inner store then persists
    /// it, so the translated text landed in the base column. The base values are captured before the
    /// action runs and put back before the callback returns, whether or not the action threw.
    /// </para>
    /// <para>
    /// TASK-168: the action sees the entity as a read in this culture shows it, and only the localizable
    /// fields it <b>changed</b> are written as translations. Writing every field put the untouched base
    /// (default-culture) text over the existing translations. Those rows are written <b>after</b> the
    /// inner store's update returns (SH-M075): the callback runs ahead of the inner write, so a row written
    /// there outlived an update that then failed.
    /// </para>
    /// </remarks>
    public void Update(Expression<Func<T, bool>> filter, Action<T> updateAction)
    {
        if (!IsNonDefaultCulture())
        {
            _innerStore.Update(filter, updateAction);
            return;
        }

        var fields = GetLocalizableFieldsFromInstance();
        var culture = _context.CurrentCulture.Name;
        var pending = new List<(Guid Guid, Dictionary<string, string?> Values)>();
        _innerStore.Update(RewriteFilter(filter)!, item =>
        {
            var baseValues = LocalizedEntityFields.Capture(item, fields);
            try
            {
                if (item.Guid is Guid guid)
                {
                    LocalizedEntityFields.Restore(item, LocalizedTranslationRows.Read(_translationStore, guid, LocalizedTranslationRows.EntityTypeOf<T>(), culture, fields));
                }

                var before = LocalizedEntityFields.Capture(item, fields);
                updateAction(item);
                var changed = LocalizedTranslationRows.Changed(before, LocalizedEntityFields.Capture(item, fields));
                if (item.Guid is Guid changedGuid && changed.Count > 0)
                {
                    pending.Add((changedGuid, changed));
                }
            }
            finally
            {
                LocalizedEntityFields.Restore(item, baseValues);
            }
        });

        foreach (var (guid, values) in pending)
        {
            SaveTranslations(guid, values);
        }
    }

    public void Update(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates)
    {
        // CR-L135: a native PropertyUpdate mutates the base column directly. When it targets a localizable
        // field on a non-default culture, that diverges from the value the Action<T> overload would persist
        // (a translation row). Detect that case and fall back to the read-modify-write path so the
        // translation is written; otherwise use the fast native PropertyUpdate.
        if (IsNonDefaultCulture() &&
            LocalizedPropertyUpdateHelper.TouchesLocalizableField(updates, GetLocalizableFieldsFromInstance()))
        {
            LocalizedPropertyUpdateHelper.RefuseIncrementOnReadModifyWriteFallback(updates);
            // The raw filter is handed on: the Action<T> overload rewrites it itself, and rewriting here
            // as well would resolve an already-resolved GUID membership test a second time.
            Update(filter, LocalizedPropertyUpdateHelper.ToAction(updates));
            return;
        }

        // SH-H018: a native PropertyUpdate that touches no localizable field still needs its *filter*
        // resolved, because the predicate may name one.
        _innerStore.Update(IsNonDefaultCulture() ? RewriteFilter(filter)! : filter, updates);
    }

    public void Delete(IEnumerable<T> data)
    {
        var items = data as IList<T> ?? data.ToList();
        _innerStore.Delete(items);
        foreach (var entity in items)
        {
            DeleteTranslations(entity);
        }
    }

    /// <summary>
    /// Filter-based delete. SH-H018: this is the destructive member of the family — under a non-default
    /// culture the unrewritten filter matched the base column, so a delete removed a different set of
    /// rows than the identical <c>Read(filter)</c> returned.
    /// </summary>
    public void Delete(Expression<Func<T, bool>> filter)
    {
        var items = _innerStore.Read(IsNonDefaultCulture() ? RewriteFilter(filter) : filter).ToList();
        _innerStore.Delete(items);
        foreach (var entity in items)
        {
            DeleteTranslations(entity);
        }
    }

    public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        // TASK-555: the create-or-update decision has one producer; an unstored Guid is created, not lost.
        return StoreSave.Save(data, storeDelegate, Create, Update);
    }

    public void Init()
    {
        _innerStore.Init();
        _translationStore.Init();
    }

    /// <summary>
    /// Destroys the inner store, then deletes this entity type's translation rows (SH-L078) — only this type's,
    /// because the translation store is shared by every localized store.
    /// </summary>
    public void Destroy()
    {
        _innerStore.Destroy();
        LocalizedTranslationRows.DeleteType(_translationStore, LocalizedTranslationRows.EntityTypeOf<T>());
    }
    public T CreateInstance() => _innerStore.CreateInstance();

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;

    #region Filter Rewriting

    protected bool IsNonDefaultCulture()
        => _context.CurrentCulture.Name != _context.DefaultCulture.Name;

    protected Expression<Func<T, bool>>? RewriteFilter(Expression<Func<T, bool>>? filter)
    {
        var localizableFields = GetLocalizableFieldsFromInstance();
        var split = LocalizedExpressionAnalyzer.Split(filter, localizableFields);

        if (!split.HasLocalizedConditions)
        {
            return filter;
        }

        var matchingGuids = ResolveMatchingGuids(split.LocalizedConditions);
        var guidFilter = LocalizedFilterHelper.BuildGuidFilter<T>(matchingGuids);

        if (split.RemainingFilter == null)
        {
            return guidFilter;
        }

        return LocalizedFilterHelper.CombineFilters(split.RemainingFilter, guidFilter);
    }

    protected HashSet<Guid> ResolveMatchingGuids(IReadOnlyList<LocalizedFieldCondition> conditions)
    {
        HashSet<Guid>? result = null;
        var culture = _context.CurrentCulture.Name;
        var entityType = LocalizedTranslationRows.EntityTypeOf<T>();

        foreach (var condition in conditions)
        {
            var tFilter = new EntityTranslationFilter
            {
                EntityType = entityType,
                FieldName = condition.FieldName,
                Culture = culture
            };
            var translations = _translationStore.Read(tFilter.ToExpression());
            var guids = new HashSet<Guid>(
                translations.Where(t => condition.ValuePredicate(t.Value)).Select(t => t.EntityGuid));

            if (result == null)
            {
                result = guids;
            }
            else
            {
                result.IntersectWith(guids);
            }
        }

        return result ?? new HashSet<Guid>();
    }

    #endregion

    #region Translation Application

    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T}.Localize(T)" />
    protected T Localize(T entity)
    {
        if (!IsNonDefaultCulture())
        {
            return entity;
        }

        if (entity.Guid == null)
        {
            return entity;
        }

        // SH-H016: Apply returns a detached copy, never the instance the inner store returned.
        return LocalizedTranslationRows.Apply(entity, LocalizedTranslationRows.ReadMany(
            _translationStore, new[] { entity.Guid.Value }, LocalizedTranslationRows.EntityTypeOf<T>(), _context.CurrentCulture.Name));
    }

    /// <summary>
    /// Replaces each entity of <paramref name="entities"/>, in place, by its translated detached copy, so the caller
    /// never holds the inner store's own instances.
    /// </summary>
    /// <remarks>
    /// TASK-679: one translation read per <see cref="LocalizedTranslationRows.ReadBatchSize"/> entities, not one per
    /// entity — a page of 48 products used to cost 48 extra queries.
    /// </remarks>
    protected List<T> LocalizeAll(List<T> entities)
    {
        if (!IsNonDefaultCulture() || entities.Count == 0)
        {
            return entities;
        }

        var rows = LocalizedTranslationRows.ReadMany(
            _translationStore, entities.Where(e => e.Guid.HasValue).Select(e => e.Guid!.Value),
            LocalizedTranslationRows.EntityTypeOf<T>(), _context.CurrentCulture.Name);
        for (var i = 0; i < entities.Count; i++)
        {
            entities[i] = LocalizedTranslationRows.Apply(entities[i], rows);
        }
        return entities;
    }

    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T}.ReadBaseValues(Guid?, IReadOnlyList{string})" />
    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T}.WithBaseValues(T, IReadOnlyDictionary{string, string?}?)" />
    protected T WithBaseValues(T data, IReadOnlyDictionary<string, string?>? baseValues)
    {
        if (baseValues == null)
        {
            return data;
        }

        var toStore = LocalizedEntityFields.Detach(data);
        LocalizedEntityFields.Restore(toStore, baseValues);
        return toStore;
    }

    /// <summary>The collection form of <see cref="WithBaseValues(T, IReadOnlyDictionary{string, string?}?)"/>.</summary>
    protected List<T> WithBaseValues(
        IList<T> items, IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string?>> baseValues)
    {
        var result = new List<T>(items.Count);
        foreach (var item in items)
        {
            result.Add(item.Guid is Guid guid && baseValues.TryGetValue(guid, out var stored)
                ? WithBaseValues(item, stored)
                : item);
        }
        return result;
    }

    protected IReadOnlyDictionary<string, string?>? ReadBaseValues(Guid? guid, IReadOnlyList<string> fields)
    {
        if (guid == null || guid == Guid.Empty)
        {
            return null;
        }

        var stored = _innerStore.Read(guid.Value);
        return stored == null ? null : LocalizedEntityFields.Capture(stored, fields);
    }

    /// <summary>
    /// The batch form of <see cref="ReadBaseValues(Guid?, IReadOnlyList{string})"/> — one read for the
    /// whole collection, keyed by GUID. An entity with no stored row simply has no entry.
    /// </summary>
    protected IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string?>> ReadBaseValues(
        IList<T> items, IReadOnlyList<string> fields)
    {
        var result = new Dictionary<Guid, IReadOnlyDictionary<string, string?>>();
        var guids = new HashSet<Guid>(
            items.Where(i => i.Guid.HasValue && i.Guid.Value != Guid.Empty).Select(i => i.Guid!.Value));
        if (guids.Count == 0)
        {
            return result;
        }

        foreach (var stored in _innerStore.Read(LocalizedFilterHelper.BuildGuidFilter<T>(guids)))
        {
            if (stored.Guid is Guid g)
            {
                result[g] = LocalizedEntityFields.Capture(stored, fields);
            }
        }

        return result;
    }

    #endregion

    #region Translation Persistence

    /// <summary>Persists every localizable field of <paramref name="entity"/> as this culture's translation.</summary>
    protected void SaveTranslations(T entity) => SaveTranslations(entity.Guid, LocalizedTranslationRows.ValuesOf(entity));

    /// <summary>
    /// Persists <paramref name="values"/> as this culture's translations of the entity keyed by
    /// <paramref name="guid"/>; a <c>null</c> value removes that field's row. See <see cref="LocalizedTranslationRows"/>.
    /// </summary>
    protected void SaveTranslations(Guid? guid, IReadOnlyDictionary<string, string?> values)
    {
        if (!IsNonDefaultCulture() || guid == null || guid == Guid.Empty)
        {
            return;
        }

        LocalizedTranslationRows.Save(_translationStore, guid.Value, LocalizedTranslationRows.EntityTypeOf<T>(), _context.CurrentCulture.Name, values);
    }

    protected void DeleteTranslations(T entity)
    {
        if (entity.Guid == null)
        {
            return;
        }

        LocalizedTranslationRows.Delete(_translationStore, entity.Guid.Value, LocalizedTranslationRows.EntityTypeOf<T>());
    }

    #endregion

    #region Helpers

    protected IReadOnlyList<string> GetLocalizableFieldsFromInstance() => _localizableFields;

    #endregion
}
