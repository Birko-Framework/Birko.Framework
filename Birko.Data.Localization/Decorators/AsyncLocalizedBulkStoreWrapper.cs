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
public class AsyncLocalizedBulkStoreWrapper<TStore, T> : IAsyncBulkStore<T>, IStoreWrapper<T>
    where TStore : IAsyncBulkStore<T>
    where T : Data.Models.AbstractModel, ILocalizable
{
    protected readonly TStore _innerStore;
    protected readonly IAsyncBulkStore<EntityTranslationModel> _translationStore;
    protected readonly IEntityLocalizationContext _context;

    public AsyncLocalizedBulkStoreWrapper(
        TStore innerStore,
        IAsyncBulkStore<EntityTranslationModel> translationStore,
        IEntityLocalizationContext context)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _translationStore = translationStore ?? throw new ArgumentNullException(nameof(translationStore));
        _context = context ?? throw new ArgumentNullException(nameof(context));
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
            allEntities = LocalizedOrderByHelper.ApplyInMemoryOrderBy(allEntities, orderBy);
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
        var guid = await _innerStore.CreateAsync(data, processDelegate, ct);
        await SaveTranslationsAsync(data, ct);
        return guid;
    }

    public async Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        // CR-M100: materialize once — a lazy/one-shot IEnumerable would otherwise be enumerated
        // twice (once by the inner store, once here), re-running or exhausting the source.
        var items = data as IList<T> ?? data.ToList();
        await _innerStore.CreateAsync(items, storeDelegate, ct);
        foreach (var entity in items)
        {
            await SaveTranslationsAsync(entity, ct);
        }
    }

    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T}.Update(T, StoreDataDelegate{T}?)" />
    public async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (!IsNonDefaultCulture())
        {
            await _innerStore.UpdateAsync(data, processDelegate, ct);
            await SaveTranslationsAsync(data, ct);
            return;
        }

        var fields = data.GetLocalizableFields();
        var baseValues = await ReadBaseValuesAsync(data.Guid, fields, ct);

        // The entity handed to the inner store is a DETACHED copy carrying the base values, not the
        // caller's object with its values swapped and swapped back. A store may keep the reference it
        // is given, so restoring the caller's text afterwards would write it straight through into the
        // store, reinstating the very defect this is fixing.
        await _innerStore.UpdateAsync(WithBaseValues(data, baseValues), processDelegate, ct);
        await SaveTranslationsAsync(data, ct);
    }

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T}.Update(IEnumerable{T}, StoreDataDelegate{T}?)" />
    public async Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        var items = data as IList<T> ?? data.ToList();

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

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T}.Update(Expression{Func{T, bool}}, Action{T})" />
    public async Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default)
    {
        var localized = IsNonDefaultCulture();
        var effective = localized ? await RewriteFilterAsync(filter, ct) : filter;
        var items = (await _innerStore.ReadAsync(effective, null, null, null, ct)).ToList();
        var fields = localized ? GetLocalizableFieldsFromInstance() : null;

        foreach (var item in items)
        {
            var baseValues = fields == null ? null : LocalizedEntityFields.Capture(item, fields);
            updateAction(item);

            if (baseValues == null)
            {
                await _innerStore.UpdateAsync(item, ct: ct);
                await SaveTranslationsAsync(item, ct);
                continue;
            }

            // SH-H017: the caller text belongs in the translation row and the base column keeps the
            // default culture, so the translation is saved first and the base values put back before
            // the inner store sees the entity. Saving first is also what the sync twin does (its
            // callback runs ahead of the inner store write), so the two agree; the consequence either
            // way is that a translation row can outlive a failed inner update.
            await SaveTranslationsAsync(item, ct);
            LocalizedEntityFields.Restore(item, baseValues);
            await _innerStore.UpdateAsync(item, ct: ct);
        }
    }

    public Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default)
    {
        // TASK-498: refused on every culture, before the culture decides the path.
        LocalizedPropertyUpdateHelper.RefuseIncrementOnLocalizableField(updates, GetLocalizableFieldsFromInstance());

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

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T}.Delete(Expression{Func{T, bool}})" />
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
        if (data.Guid == null || data.Guid == Guid.Empty)
        {
            await CreateAsync(data, processDelegate, ct);
        }
        else
        {
            await UpdateAsync(data, processDelegate, ct);
        }
        return data.Guid ?? Guid.Empty;
    }

    public Task InitAsync(CancellationToken ct = default) => _innerStore.InitAsync(ct);
    public Task DestroyAsync(CancellationToken ct = default) => _innerStore.DestroyAsync(ct);
    public T CreateInstance() => _innerStore.CreateInstance();

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;

    #region Filter Rewriting

    protected bool IsNonDefaultCulture()
        => _context.CurrentCulture.Name != _context.DefaultCulture.Name;

    protected async Task<Expression<Func<T, bool>>?> RewriteFilterAsync(
        Expression<Func<T, bool>>? filter, CancellationToken ct)
    {
        var localizableFields = GetLocalizableFieldsFromInstance();
        var split = LocalizedExpressionAnalyzer.Split(filter, localizableFields);

        if (!split.HasLocalizedConditions)
        {
            return filter;
        }

        var matchingGuids = await ResolveMatchingGuidsAsync(split.LocalizedConditions, ct);
        var guidFilter = LocalizedFilterHelper.BuildGuidFilter<T>(matchingGuids);

        if (split.RemainingFilter == null)
        {
            return guidFilter;
        }

        return LocalizedFilterHelper.CombineFilters(split.RemainingFilter, guidFilter);
    }

    protected async Task<HashSet<Guid>> ResolveMatchingGuidsAsync(
        IReadOnlyList<LocalizedFieldCondition> conditions, CancellationToken ct)
    {
        HashSet<Guid>? result = null;
        var culture = _context.CurrentCulture.Name;
        var entityType = typeof(T).Name;

        foreach (var condition in conditions)
        {
            var tFilter = new EntityTranslationFilter
            {
                EntityType = entityType,
                FieldName = condition.FieldName,
                Culture = culture
            };
            var translations = await _translationStore.ReadAsync(tFilter.ToExpression(), ct: ct);
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
    protected async Task<T> LocalizeAsync(T entity, CancellationToken ct)
    {
        // No translations exist for the default culture (IEntityLocalizationContext contract), so
        // skip the per-entity query and avoid overwriting base fields with a stray default-culture
        // translation row — matching the non-bulk wrapper (CR-H053).
        if (!IsNonDefaultCulture())
        {
            return entity;
        }

        if (entity.Guid == null)
        {
            return entity;
        }

        // SH-H016: never write to the instance the inner store returned.
        var localized = LocalizedEntityFields.Detach(entity);

        var filter = EntityTranslationFilter.ByEntityAndCulture(entity.Guid.Value, _context.CurrentCulture.Name);
        var translations = await _translationStore.ReadAsync(filter.ToExpression(), ct: ct);
        var translationDict = new Dictionary<string, string>();
        foreach (var t in translations)
        {
            translationDict[t.FieldName] = t.Value;
        }

        if (translationDict.Count == 0)
        {
            return localized;
        }

        var fields = localized.GetLocalizableFields();
        var type = localized.GetType();
        foreach (var fieldName in fields)
        {
            if (translationDict.TryGetValue(fieldName, out var value))
            {
                var prop = type.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.PropertyType == typeof(string) && prop.CanWrite)
                {
                    prop.SetValue(localized, value);
                }
            }
        }

        return localized;
    }

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T}.LocalizeAll(System.Collections.Generic.List{T})" />
    protected async Task<List<T>> LocalizeAllAsync(List<T> entities, CancellationToken ct)
    {
        for (var i = 0; i < entities.Count; i++)
        {
            entities[i] = await LocalizeAsync(entities[i], ct);
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

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T}.WithBaseValues(System.Collections.Generic.IList{T}, IReadOnlyDictionary{Guid, IReadOnlyDictionary{string, string?}})" />
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

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T}.ReadBaseValues(System.Collections.Generic.IList{T}, IReadOnlyList{string})" />
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

    protected async Task SaveTranslationsAsync(T entity, CancellationToken ct)
    {
        if (!IsNonDefaultCulture())
        {
            return;
        }

        if (entity.Guid == null)
        {
            return;
        }

        var fields = entity.GetLocalizableFields();
        var type = entity.GetType();
        var entityType = type.Name;
        var now = DateTime.UtcNow;

        foreach (var fieldName in fields)
        {
            var prop = type.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || prop.PropertyType != typeof(string))
            {
                continue;
            }

            var value = prop.GetValue(entity) as string;
            if (value == null)
            {
                continue;
            }

            var tFilter = EntityTranslationFilter.ByEntityFieldAndCulture(
                entity.Guid.Value, fieldName, _context.CurrentCulture.Name);
            var existing = await _translationStore.ReadAsync(tFilter.ToExpression(), ct: ct);

            var translation = existing.FirstOrDefault();
            if (translation != null)
            {
                translation.Value = value;
                translation.UpdatedAt = now;
                await _translationStore.UpdateAsync(translation, ct: ct);
            }
            else
            {
                translation = new EntityTranslationModel
                {
                    EntityGuid = entity.Guid.Value,
                    EntityType = entityType,
                    FieldName = fieldName,
                    Culture = _context.CurrentCulture.Name,
                    Value = value,
                    UpdatedAt = now
                };
                await _translationStore.CreateAsync(translation, ct: ct);
            }
        }
    }

    protected async Task DeleteTranslationsAsync(T entity, CancellationToken ct)
    {
        if (entity.Guid == null)
        {
            return;
        }

        var filter = EntityTranslationFilter.ByEntity(entity.Guid.Value);
        var translations = await _translationStore.ReadAsync(filter.ToExpression(), ct: ct);
        await _translationStore.DeleteAsync(translations, ct);
    }

    #endregion

    #region Helpers

    protected IReadOnlyList<string> GetLocalizableFieldsFromInstance()
    {
        var instance = _innerStore.CreateInstance();
        return instance.GetLocalizableFields();
    }

    #endregion
}
