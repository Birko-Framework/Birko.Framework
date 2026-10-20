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
public class LocalizedBulkStoreWrapper<TStore, T, TTranslation> : IBulkStore<T>, IStoreWrapper<T>
    where TStore : IBulkStore<T>
    where T : Data.Models.AbstractModel, ILocalizable
    where TTranslation : Data.Models.AbstractModel, IEntityTranslation
{
    protected readonly TStore _innerStore;
    protected readonly IBulkStore<TTranslation> _translationStore;
    protected readonly IEntityLocalizationContext _context;
    protected readonly IReadOnlyList<string> _localizableFields;

    public LocalizedBulkStoreWrapper(
        TStore innerStore,
        IBulkStore<TTranslation> translationStore,
        IEntityLocalizationContext context)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _translationStore = translationStore ?? throw new ArgumentNullException(nameof(translationStore));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        // SH-L079 / SH-L082 (TASK-194): the declaration is read and validated once, at wiring, on every culture.
        _localizableFields = LocalizedEntityFields.LocalizableFieldsOf(_innerStore.CreateInstance());
        LocalizedTranslationRows.RequireQueryableModel<TTranslation>();
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
        SaveTranslations(items);
    }

    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T,TTranslation}.Update(T, StoreDataDelegate{T}?)" />
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
            SaveTranslations(items);
            return;
        }

        var fields = GetLocalizableFieldsFromInstance();
        var baseValues = ReadBaseValues(items, fields);

        _innerStore.Update(WithBaseValues(items, baseValues), storeDelegate);
        SaveTranslations(items);
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
    /// SH-H017: the action used to mutate the entity the inner store loaded, and the inner store then
    /// persisted it, so the translated text landed in the base column. The action now works on a detached
    /// copy carrying the translations, and the copy gets the base values back before it is written.
    /// </para>
    /// <para>
    /// TASK-168: the action sees the entity as a read in this culture shows it, and only the localizable
    /// fields it <b>changed</b> are written as translations. Writing every field put the untouched base
    /// (default-culture) text over the existing translations. Those rows are written <b>after</b> the
    /// entity's inner write succeeds (SH-M075), in one batch for the whole update (TASK-690). The matched
    /// entities are read here and written one by one, as the async wrapper does, so the two take the same
    /// path; when a write fails part way, the entities already written keep their translation changes.
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
        var items = _innerStore.Read(RewriteFilter(filter)).ToList();
        // TASK-690: the matched entities' translations in one read, not one per entity.
        var rows = LocalizedTranslationRows.ReadMany(
            _translationStore, LocalizedTranslationRows.GuidsOf(items), LocalizedTranslationRows.EntityTypeOf<T>(), _context.CurrentCulture.Name);
        var pending = new List<(Guid EntityGuid, IReadOnlyDictionary<string, string?> Values)>();
        try
        {
            foreach (var item in items)
            {
                // The action works on a detached copy: translations are applied to it, and the inner store's
                // own instance must never carry them (SH-H016), even when the action throws.
                var working = LocalizedEntityFields.Detach(item);
                var baseValues = LocalizedEntityFields.Capture(working, fields);
                if (working.Guid is Guid guid && rows.TryGetValue(guid, out var translated))
                {
                    LocalizedEntityFields.Restore(working, LocalizedTranslationRows.OnlyFields(translated, fields));
                }

                var before = LocalizedEntityFields.Capture(working, fields);
                updateAction(working);
                var changed = LocalizedTranslationRows.Changed(before, LocalizedEntityFields.Capture(working, fields));

                // SH-H017: the base column keeps the default culture. SH-M075: an entity's translation rows are
                // written only once its inner update has succeeded, so a failed update leaves none behind.
                LocalizedEntityFields.Restore(working, baseValues);
                _innerStore.Update(working);
                if (working.Guid is Guid changedGuid && changed.Count > 0)
                {
                    pending.Add((changedGuid, changed));
                }
            }
        }
        catch (Exception) when (pending.Count > 0)
        {
            SaveAfterFailure(pending);
            throw;
        }

        // TASK-690: one batched write for every entity the update changed.
        SaveTranslations(pending);
    }

    /// <inheritdoc cref="AsyncLocalizedBulkStoreWrapper{TStore,T,TTranslation}.SaveAfterFailureAsync" />
    private void SaveAfterFailure(IReadOnlyCollection<(Guid EntityGuid, IReadOnlyDictionary<string, string?> Values)> pending)
    {
        try
        {
            SaveTranslations(pending);
        }
        catch (Exception)
        {
            // The original exception is rethrown by the caller.
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
        DeleteTranslations(items);
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
        DeleteTranslations(items);
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

    /// <summary>The filter rewritten for the current culture — see <see cref="LocalizedFilterResolution"/> (TASK-681).</summary>
    protected Expression<Func<T, bool>>? RewriteFilter(Expression<Func<T, bool>>? filter)
        => LocalizedFilterResolution.Rewrite(
            filter, GetLocalizableFieldsFromInstance(), _translationStore, _innerStore, _context.CurrentCulture.Name);

    #endregion

    #region Translation Application

    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T,TTranslation}.Localize(T)" />
    protected T Localize(T entity)
    {
        if (!IsNonDefaultCulture())
        {
            return entity;
        }

        // SH-H016: the result is a detached copy, never the instance the inner store returned.
        return LocalizedTranslationRows.LocalizeAll(_translationStore, new List<T> { entity }, _context.CurrentCulture.Name)[0];
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
        => IsNonDefaultCulture()
            ? LocalizedTranslationRows.LocalizeAll(_translationStore, entities, _context.CurrentCulture.Name)
            : entities;

    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T,TTranslation}.ReadBaseValues(Guid?, IReadOnlyList{string})" />
    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T,TTranslation}.WithBaseValues(T, IReadOnlyDictionary{string, string?}?)" />
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
    /// Persists every localizable field of each of <paramref name="entities"/> as this culture's translations, in one
    /// batched write (TASK-690).
    /// </summary>
    protected void SaveTranslations(IEnumerable<T> entities)
    {
        if (IsNonDefaultCulture())
        {
            SaveTranslations(LocalizedTranslationRows.ValuesOfAll(entities));
        }
    }

    /// <summary>
    /// Persists <paramref name="values"/> as this culture's translations of the entity keyed by
    /// <paramref name="guid"/>; a <c>null</c> value removes that field's row. See <see cref="LocalizedTranslationRows"/>.
    /// </summary>
    protected void SaveTranslations(Guid? guid, IReadOnlyDictionary<string, string?> values)
    {
        if (guid is Guid g)
        {
            SaveTranslations(new[] { (g, values) });
        }
    }

    /// <summary>
    /// Persists each entity's <c>Values</c> as this culture's translations, in one batched write: one read of the
    /// existing rows, then at most one bulk delete, update and create (<see cref="LocalizedTranslationRows.SaveMany"/>).
    /// Nothing is written on the default culture, and an empty GUID is skipped.
    /// </summary>
    protected void SaveTranslations(IReadOnlyCollection<(Guid EntityGuid, IReadOnlyDictionary<string, string?> Values)> values)
    {
        if (!IsNonDefaultCulture())
        {
            return;
        }

        LocalizedTranslationRows.SaveMany(
            _translationStore, LocalizedTranslationRows.EntityTypeOf<T>(), _context.CurrentCulture.Name,
            values.Where(v => v.EntityGuid != Guid.Empty));
    }

    protected void DeleteTranslations(T entity) => DeleteTranslations(new[] { entity });

    /// <summary>
    /// Deletes every culture's translations of <paramref name="entities"/>, as one filter delete per
    /// <see cref="LocalizedTranslationRows.ReadBatchSize"/> entities (TASK-690); no call when none has a GUID.
    /// </summary>
    protected void DeleteTranslations(IEnumerable<T> entities)
        => LocalizedTranslationRows.DeleteMany(
            _translationStore, LocalizedTranslationRows.GuidsOf(entities), LocalizedTranslationRows.EntityTypeOf<T>());

    #endregion

    #region Helpers

    protected IReadOnlyList<string> GetLocalizableFieldsFromInstance() => _localizableFields;

    #endregion
}

/// <summary>
/// <see cref="LocalizedBulkStoreWrapper{TStore,T,TTranslation}"/> with the default translation model, <see cref="EntityTranslationModel"/>.
/// </summary>
public class LocalizedBulkStoreWrapper<TStore, T> : LocalizedBulkStoreWrapper<TStore, T, EntityTranslationModel>
    where TStore : IBulkStore<T>
    where T : Data.Models.AbstractModel, ILocalizable
{
    public LocalizedBulkStoreWrapper(
        TStore innerStore,
        IBulkStore<EntityTranslationModel> translationStore,
        IEntityLocalizationContext context)
        : base(innerStore, translationStore, context)
    {
    }
}
