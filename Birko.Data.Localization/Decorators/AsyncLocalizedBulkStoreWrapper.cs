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
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Localization.Decorators;

/// <summary>
/// Async bulk store wrapper that applies entity localization to bulk read/write operations.
/// Rewrites filter expressions for localized field conditions and handles
/// in-memory ordering/pagination when OrderBy references localized fields.
/// </summary>
public class AsyncLocalizedBulkStoreWrapper<TStore, T, TTranslation> : IAsyncBulkStore<T>, IStoreWrapper<T>
    where TStore : IAsyncBulkStore<T>
    where T : Data.Models.AbstractModel, ILocalizable
    where TTranslation : Data.Models.AbstractModel, IEntityTranslation
{
    protected readonly TStore _innerStore;
    protected readonly IAsyncBulkStore<TTranslation> _translationStore;
    protected readonly IEntityLocalizationContext _context;
    protected readonly IReadOnlyList<string> _localizableFields;

    public AsyncLocalizedBulkStoreWrapper(
        TStore innerStore,
        IAsyncBulkStore<TTranslation> translationStore,
        IEntityLocalizationContext context)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _translationStore = translationStore ?? throw new ArgumentNullException(nameof(translationStore));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        // SH-L079 / SH-L082 (TASK-194): the declaration is read and validated once, at wiring, on every culture.
        _localizableFields = LocalizedEntityFields.LocalizableFieldsOf(_innerStore.CreateInstance());
        LocalizedTranslationRows.RequireQueryableModel<TTranslation>();
    }

    // Singular reads with localization
    public async Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
    {
        var entity = await _innerStore.ReadAsync(guid, ct);
        return entity == null ? null : await LocalizeAsync(entity, ct);
    }

    public async Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        if (!IsNonDefaultCulture())
        {
            return await _innerStore.ReadAsync(filter, ct);
        }

        var rewritten = await RewriteFilterAsync(filter, ct);
        var result = await _innerStore.ReadAsync(rewritten, ct);
        return result == null ? null : await LocalizeAsync(result, ct);
    }

    // Bulk reads with localization
    public async Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default)
    {
        var entities = (await _innerStore.ReadAsync(ct)).ToList();
        return IsNonDefaultCulture() ? await LocalizeAllAsync(entities, ct) : entities;
    }

    public async Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        if (!IsNonDefaultCulture())
        {
            return (await _innerStore.ReadAsync(filter, orderBy, limit, offset, ct)).ToList();
        }

        var rewritten = await RewriteFilterAsync(filter, ct);
        var localizableFields = GetLocalizableFieldsFromInstance();
        var needsInMemorySort = LocalizedOrderByHelper.ReferencesLocalizedField(orderBy, localizableFields);

        if (!needsInMemorySort)
        {
            // OrderBy is on non-localized fields — safe to pass to inner store
            var entities = (await _innerStore.ReadAsync(rewritten, orderBy, limit, offset, ct)).ToList();
            return await LocalizeAllAsync(entities, ct);
        }

        // OrderBy references localized fields — fetch all matching, translate, sort, paginate in memory
        var allEntities = await LocalizeAllAsync(
            (await _innerStore.ReadAsync(rewritten, ct: ct)).ToList(), ct);

        if (orderBy != null)
        {
            allEntities = LocalizedOrderByHelper.ApplyInMemoryOrderBy(allEntities, orderBy, _context.CurrentCulture);
        }

        return LocalizedOrderByHelper.ApplyInMemoryPaging(allEntities, offset, limit);
    }

    public async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        if (!IsNonDefaultCulture())
        {
            return await _innerStore.CountAsync(filter, ct);
        }

        var rewritten = await RewriteFilterAsync(filter, ct);
        return await _innerStore.CountAsync(rewritten, ct);
    }

    public async Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        LocalizedEntityFields.RequireValidDeclarations(new[] { data });
        var guid = await _innerStore.CreateAsync(data, processDelegate, ct);
        // SH-M076: keyed by the GUID the store returned — it need not have written it onto the entity.
        await SaveTranslationsAsync(guid, LocalizedTranslationRows.ValuesOf(data), ct);
        return guid;
    }

    public async Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        // CR-M100: materialize once — a lazy/one-shot IEnumerable would otherwise be enumerated
        // twice (once by the inner store, once here), re-running or exhausting the source.
        var items = data as IList<T> ?? data.ToList();
        LocalizedEntityFields.RequireValidDeclarations(items);
        await _innerStore.CreateAsync(items, storeDelegate, ct);
        foreach (var entity in items)
        {
            await SaveTranslationsAsync(entity, ct);
        }
    }

    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T,TTranslation}.Update(T, StoreDataDelegate{T}?)" />
    public async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        LocalizedEntityFields.RequireValidDeclarations(new[] { data });
        if (!IsNonDefaultCulture())
        {
            await _innerStore.UpdateAsync(data, processDelegate, ct);
            await SaveTranslationsAsync(data, ct);
            return;
        }

        var fields = LocalizedEntityFields.LocalizableFieldsOf(data);
        var baseValues = await ReadBaseValuesAsync(data.Guid, fields, ct);

        // The entity handed to the inner store is a DETACHED copy carrying the base values, not the
        // caller's object with its values swapped and swapped back. A store may keep the reference it
        // is given, so restoring the caller's text afterwards would write it straight through into the
        // store, reinstating the very defect this is fixing.
        await _innerStore.UpdateAsync(WithBaseValues(data, baseValues), processDelegate, ct);
        await SaveTranslationsAsync(data, ct);
    }

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T,TTranslation}.Update(IEnumerable{T}, StoreDataDelegate{T}?)" />
    public async Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        var items = data as IList<T> ?? data.ToList();
        LocalizedEntityFields.RequireValidDeclarations(items);

        if (!IsNonDefaultCulture())
        {
            await _innerStore.UpdateAsync(items, storeDelegate, ct);
            foreach (var entity in items)
            {
                await SaveTranslationsAsync(entity, ct);
            }
            return;
        }

        var fields = GetLocalizableFieldsFromInstance();
        var baseValues = await ReadBaseValuesAsync(items, fields, ct);

        await _innerStore.UpdateAsync(WithBaseValues(items, baseValues), storeDelegate, ct);

        foreach (var entity in items)
        {
            await SaveTranslationsAsync(entity, ct);
        }
    }

    public async Task DeleteAsync(T data, CancellationToken ct = default)
    {
        await _innerStore.DeleteAsync(data, ct);
        await DeleteTranslationsAsync(data, ct);
    }

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T,TTranslation}.Update(Expression{Func{T, bool}}, Action{T})" />
    public async Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default)
    {
        var localized = IsNonDefaultCulture();
        var effective = localized ? await RewriteFilterAsync(filter, ct) : filter;
        var items = (await _innerStore.ReadAsync(effective, null, null, null, ct)).ToList();
        var fields = localized ? GetLocalizableFieldsFromInstance() : null;

        foreach (var item in items)
        {
            if (fields == null)
            {
                updateAction(item);
                await _innerStore.UpdateAsync(item, ct: ct);
                continue;
            }

            // The action works on a detached copy: translations are applied to it, and the inner store's
            // own instance must never carry them (SH-H016), even when the action throws.
            var working = LocalizedEntityFields.Detach(item);
            var baseValues = LocalizedEntityFields.Capture(working, fields);
            if (working.Guid is Guid guid)
            {
                LocalizedEntityFields.Restore(working, await LocalizedTranslationRows.ReadAsync(
                    _translationStore, guid, LocalizedTranslationRows.EntityTypeOf<T>(), _context.CurrentCulture.Name, fields, ct));
            }

            var before = LocalizedEntityFields.Capture(working, fields);
            updateAction(working);
            var changed = LocalizedTranslationRows.Changed(before, LocalizedEntityFields.Capture(working, fields));

            // SH-H017: the base column keeps the default culture. SH-M075: the translation rows are written
            // only once the inner update has succeeded, so a failed update leaves none behind.
            LocalizedEntityFields.Restore(working, baseValues);
            await _innerStore.UpdateAsync(working, ct: ct);
            if (changed.Count > 0)
            {
                await SaveTranslationsAsync(working.Guid, changed, ct);
            }
        }
    }

    public Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default)
    {
        // CR-L135: a native PropertyUpdate mutates the base column directly. When it targets a localizable
        // field on a non-default culture, that diverges from the value the Action<T> overload would persist
        // (a translation row). Detect that case and fall back to the read-modify-write path so the
        // translation is written; otherwise use the fast native PropertyUpdate.
        if (IsNonDefaultCulture() &&
            LocalizedPropertyUpdateHelper.TouchesLocalizableField(updates, GetLocalizableFieldsFromInstance()))
        {
            LocalizedPropertyUpdateHelper.RefuseIncrementOnReadModifyWriteFallback(updates);
            // The raw filter is handed on: the Action<T> overload rewrites it itself.
            return UpdateAsync(filter, LocalizedPropertyUpdateHelper.ToAction(updates), ct);
        }

        // SH-H018: a native PropertyUpdate that touches no localizable field still needs its *filter*
        // resolved, because the predicate may name one.
        return IsNonDefaultCulture()
            ? UpdateNativeWithRewrittenFilterAsync(filter, updates, ct)
            : _innerStore.UpdateAsync(filter, updates, ct);
    }

    private async Task UpdateNativeWithRewrittenFilterAsync(
        Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct)
    {
        var rewritten = await RewriteFilterAsync(filter, ct);
        await _innerStore.UpdateAsync(rewritten!, updates, ct);
    }

    public async Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default)
    {
        var items = data as IList<T> ?? data.ToList();
        await _innerStore.DeleteAsync(items, ct);
        foreach (var entity in items)
        {
            await DeleteTranslationsAsync(entity, ct);
        }
    }

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T,TTranslation}.Delete(Expression{Func{T, bool}})" />
    public async Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default)
    {
        var effective = IsNonDefaultCulture() ? await RewriteFilterAsync(filter, ct) : filter;
        var items = (await _innerStore.ReadAsync(effective, null, null, null, ct)).ToList();
        await _innerStore.DeleteAsync(items, ct);
        foreach (var entity in items)
        {
            await DeleteTranslationsAsync(entity, ct);
        }
    }

    public async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        // TASK-555: the create-or-update decision has one producer; an unstored Guid is created, not lost.
        return await StoreSave.SaveAsync(data, processDelegate, CreateAsync, UpdateAsync, ct);
    }

    public async Task InitAsync(CancellationToken ct = default)
    {
        await _innerStore.InitAsync(ct);
        await _translationStore.InitAsync(ct);
    }

    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T,TTranslation}.Destroy" />
    public async Task DestroyAsync(CancellationToken ct = default)
    {
        await _innerStore.DestroyAsync(ct);
        await LocalizedTranslationRows.DeleteTypeAsync(_translationStore, LocalizedTranslationRows.EntityTypeOf<T>(), ct);
    }
    public T CreateInstance() => _innerStore.CreateInstance();

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;

    #region Filter Rewriting

    protected bool IsNonDefaultCulture()
        => _context.CurrentCulture.Name != _context.DefaultCulture.Name;

    /// <summary>The filter rewritten for the current culture — see <see cref="LocalizedFilterResolution"/> (TASK-681).</summary>
    protected Task<Expression<Func<T, bool>>?> RewriteFilterAsync(
        Expression<Func<T, bool>>? filter, CancellationToken ct)
        => LocalizedFilterResolution.RewriteAsync(
            filter, GetLocalizableFieldsFromInstance(), _translationStore, _innerStore, _context.CurrentCulture.Name, ct);

    #endregion

    #region Translation Application

    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T,TTranslation}.Localize(T)" />
    protected async Task<T> LocalizeAsync(T entity, CancellationToken ct)
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
        return LocalizedTranslationRows.Apply(entity, await LocalizedTranslationRows.ReadManyAsync(
            _translationStore, new[] { entity.Guid.Value }, LocalizedTranslationRows.EntityTypeOf<T>(), _context.CurrentCulture.Name, ct));
    }

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T,TTranslation}.LocalizeAll(System.Collections.Generic.List{T})" />
    protected async Task<List<T>> LocalizeAllAsync(List<T> entities, CancellationToken ct)
    {
        if (!IsNonDefaultCulture() || entities.Count == 0)
        {
            return entities;
        }

        var rows = await LocalizedTranslationRows.ReadManyAsync(
            _translationStore, entities.Where(e => e.Guid.HasValue).Select(e => e.Guid!.Value),
            LocalizedTranslationRows.EntityTypeOf<T>(), _context.CurrentCulture.Name, ct);
        for (var i = 0; i < entities.Count; i++)
        {
            entities[i] = LocalizedTranslationRows.Apply(entities[i], rows);
        }
        return entities;
    }

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

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T,TTranslation}.WithBaseValues(System.Collections.Generic.IList{T}, IReadOnlyDictionary{Guid, IReadOnlyDictionary{string, string?}})" />
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

    protected async Task<IReadOnlyDictionary<string, string?>?> ReadBaseValuesAsync(
        Guid? guid, IReadOnlyList<string> fields, CancellationToken ct)
    {
        if (guid == null || guid == Guid.Empty)
        {
            return null;
        }

        var stored = await _innerStore.ReadAsync(guid.Value, ct);
        return stored == null ? null : LocalizedEntityFields.Capture(stored, fields);
    }

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T,TTranslation}.ReadBaseValues(System.Collections.Generic.IList{T}, IReadOnlyList{string})" />
    protected async Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string?>>> ReadBaseValuesAsync(
        IList<T> items, IReadOnlyList<string> fields, CancellationToken ct)
    {
        var result = new Dictionary<Guid, IReadOnlyDictionary<string, string?>>();
        var guids = new HashSet<Guid>(
            items.Where(i => i.Guid.HasValue && i.Guid.Value != Guid.Empty).Select(i => i.Guid!.Value));
        if (guids.Count == 0)
        {
            return result;
        }

        // Spelled out in full: ReadAsync(filter, ct) and ReadAsync(filter, orderBy, limit, offset, ct)
        // are both applicable here, and only the second returns the collection this needs.
        var stored = await _innerStore.ReadAsync(
            LocalizedFilterHelper.BuildGuidFilter<T>(guids), null, null, null, ct);
        foreach (var entity in stored)
        {
            if (entity.Guid is Guid g)
            {
                result[g] = LocalizedEntityFields.Capture(entity, fields);
            }
        }

        return result;
    }

    #endregion

    #region Translation Persistence

    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T,TTranslation}.SaveTranslations(T)" />
    protected Task SaveTranslationsAsync(T entity, CancellationToken ct)
        => SaveTranslationsAsync(entity.Guid, LocalizedTranslationRows.ValuesOf(entity), ct);

    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T,TTranslation}.SaveTranslations(Guid?, IReadOnlyDictionary{string, string?})" />
    protected Task SaveTranslationsAsync(Guid? guid, IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        if (!IsNonDefaultCulture() || guid == null || guid == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        return LocalizedTranslationRows.SaveAsync(
            _translationStore, guid.Value, LocalizedTranslationRows.EntityTypeOf<T>(), _context.CurrentCulture.Name, values, ct);
    }

    protected async Task DeleteTranslationsAsync(T entity, CancellationToken ct)
    {
        if (entity.Guid == null)
        {
            return;
        }

        await LocalizedTranslationRows.DeleteAsync(_translationStore, entity.Guid.Value, LocalizedTranslationRows.EntityTypeOf<T>(), ct);
    }

    #endregion

    #region Helpers

    protected IReadOnlyList<string> GetLocalizableFieldsFromInstance() => _localizableFields;

    #endregion
}

/// <summary>
/// <see cref="AsyncLocalizedBulkStoreWrapper{TStore,T,TTranslation}"/> with the default translation model, <see cref="EntityTranslationModel"/>.
/// </summary>
public class AsyncLocalizedBulkStoreWrapper<TStore, T> : AsyncLocalizedBulkStoreWrapper<TStore, T, EntityTranslationModel>
    where TStore : IAsyncBulkStore<T>
    where T : Data.Models.AbstractModel, ILocalizable
{
    public AsyncLocalizedBulkStoreWrapper(
        TStore innerStore,
        IAsyncBulkStore<EntityTranslationModel> translationStore,
        IEntityLocalizationContext context)
        : base(innerStore, translationStore, context)
    {
    }
}
