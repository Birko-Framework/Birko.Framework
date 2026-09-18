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
/// Async store wrapper that intercepts reads to apply localized field values,
/// rewrites filter expressions to query the translation store for localized field conditions,
/// and intercepts creates/updates to persist translations for localizable fields.
/// </summary>
public class AsyncLocalizedStoreWrapper<TStore, T> : IAsyncStore<T>, IStoreWrapper<T>
    where TStore : IAsyncStore<T>
    where T : Data.Models.AbstractModel, ILocalizable
{
    protected readonly TStore _innerStore;
    protected readonly IAsyncBulkStore<EntityTranslationModel> _translationStore;
    protected readonly IEntityLocalizationContext _context;

    public AsyncLocalizedStoreWrapper(
        TStore innerStore,
        IAsyncBulkStore<EntityTranslationModel> translationStore,
        IEntityLocalizationContext context)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _translationStore = translationStore ?? throw new ArgumentNullException(nameof(translationStore));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

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
        var entity = await _innerStore.ReadAsync(rewritten, ct);
        return entity == null ? null : await LocalizeAsync(entity, ct);
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

    public async Task DeleteAsync(T data, CancellationToken ct = default)
    {
        await _innerStore.DeleteAsync(data, ct);
        await DeleteTranslationsAsync(data, ct);
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
