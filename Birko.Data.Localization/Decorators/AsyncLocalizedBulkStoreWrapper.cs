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
        if (entity != null)
        {
            await ApplyTranslationsAsync(entity, ct);
        }
        return entity;
    }

    public async Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        if (!IsNonDefaultCulture())
        {
            var entity = await _innerStore.ReadAsync(filter, ct);
            if (entity != null)
            {
                await ApplyTranslationsAsync(entity, ct);
            }
            return entity;
        }

        var rewritten = await RewriteFilterAsync(filter, ct);
        var result = await _innerStore.ReadAsync(rewritten, ct);
        if (result != null)
        {
            await ApplyTranslationsAsync(result, ct);
        }
        return result;
    }

    // Bulk reads with localization
    public async Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default)
    {
        var entities = (await _innerStore.ReadAsync(ct)).ToList();
        if (IsNonDefaultCulture())
        {
            foreach (var entity in entities)
            {
                await ApplyTranslationsAsync(entity, ct);
            }
        }
        return entities;
    }

    public async Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        if (!IsNonDefaultCulture())
        {
            var entities = (await _innerStore.ReadAsync(filter, orderBy, limit, offset, ct)).ToList();
            foreach (var entity in entities)
            {
                await ApplyTranslationsAsync(entity, ct);
            }
            return entities;
        }

        var rewritten = await RewriteFilterAsync(filter, ct);
        var localizableFields = GetLocalizableFieldsFromInstance();
        var needsInMemorySort = LocalizedOrderByHelper.ReferencesLocalizedField(orderBy, localizableFields);

        if (!needsInMemorySort)
        {
            // OrderBy is on non-localized fields — safe to pass to inner store
            var entities = (await _innerStore.ReadAsync(rewritten, orderBy, limit, offset, ct)).ToList();
            foreach (var entity in entities)
            {
                await ApplyTranslationsAsync(entity, ct);
            }
            return entities;
        }

        // OrderBy references localized fields — fetch all matching, translate, sort, paginate in memory
        var allEntities = (await _innerStore.ReadAsync(rewritten, ct: ct)).ToList();
        foreach (var entity in allEntities)
        {
            await ApplyTranslationsAsync(entity, ct);
        }

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
        await _innerStore.CreateAsync(data, storeDelegate, ct);
        foreach (var entity in data)
        {
            await SaveTranslationsAsync(entity, ct);
        }
    }

    public async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        await _innerStore.UpdateAsync(data, processDelegate, ct);
        await SaveTranslationsAsync(data, ct);
    }

    public async Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        await _innerStore.UpdateAsync(data, storeDelegate, ct);
        foreach (var entity in data)
        {
            await SaveTranslationsAsync(entity, ct);
        }
    }

    public async Task DeleteAsync(T data, CancellationToken ct = default)
    {
        await _innerStore.DeleteAsync(data, ct);
        await DeleteTranslationsAsync(data, ct);
    }

    public async Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default)
    {
        await _innerStore.DeleteAsync(data, ct);
        foreach (var entity in data)
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

    protected async Task ApplyTranslationsAsync(T entity, CancellationToken ct)
    {
        if (entity.Guid == null)
        {
            return;
        }

        var filter = EntityTranslationFilter.ByEntityAndCulture(entity.Guid.Value, _context.CurrentCulture.Name);
        var translations = await _translationStore.ReadAsync(filter.ToExpression(), ct: ct);
        var translationDict = new Dictionary<string, string>();
        foreach (var t in translations)
        {
            translationDict[t.FieldName] = t.Value;
        }

        if (translationDict.Count == 0)
        {
            return;
        }

        var fields = entity.GetLocalizableFields();
        var type = entity.GetType();
        foreach (var fieldName in fields)
        {
            if (translationDict.TryGetValue(fieldName, out var value))
            {
                var prop = type.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.PropertyType == typeof(string) && prop.CanWrite)
                {
                    prop.SetValue(entity, value);
                }
            }
        }
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
