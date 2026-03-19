using Birko.Data.Localization.Expressions;
using Birko.Data.Localization.Filters;
using Birko.Data.Localization.Models;
using Birko.Data.Stores;
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

    public LocalizedBulkStoreWrapper(
        TStore innerStore,
        IBulkStore<EntityTranslationModel> translationStore,
        IEntityLocalizationContext context)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _translationStore = translationStore ?? throw new ArgumentNullException(nameof(translationStore));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    // Singular read with localization
    public T? Read(Guid guid)
    {
        var entity = _innerStore.Read(guid);
        if (entity != null)
        {
            ApplyTranslations(entity);
        }
        return entity;
    }

    public T? Read(Expression<Func<T, bool>>? filter = null)
    {
        if (!IsNonDefaultCulture())
        {
            var entity = ((IReadStore<T>)_innerStore).Read(filter);
            if (entity != null)
            {
                ApplyTranslations(entity);
            }
            return entity;
        }

        var rewritten = RewriteFilter(filter);
        var result = ((IReadStore<T>)_innerStore).Read(rewritten);
        if (result != null)
        {
            ApplyTranslations(result);
        }
        return result;
    }

    // Bulk read with localization
    public IEnumerable<T> Read()
    {
        var entities = _innerStore.Read().ToList();
        if (IsNonDefaultCulture())
        {
            foreach (var entity in entities)
            {
                ApplyTranslations(entity);
            }
        }
        return entities;
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
            foreach (var entity in entities)
            {
                ApplyTranslations(entity);
            }
            return entities;
        }

        // OrderBy references localized fields — fetch all matching, translate, sort, paginate in memory
        var allEntities = _innerStore.Read(rewritten).ToList();
        foreach (var entity in allEntities)
        {
            ApplyTranslations(entity);
        }

        if (orderBy != null)
        {
            allEntities = LocalizedOrderByHelper.ApplyInMemoryOrderBy(allEntities, orderBy);
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
        var guid = _innerStore.Create(data, storeDelegate);
        SaveTranslations(data);
        return guid;
    }

    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        _innerStore.Create(data, storeDelegate);
        foreach (var entity in data)
        {
            SaveTranslations(entity);
        }
    }

    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        _innerStore.Update(data, storeDelegate);
        SaveTranslations(data);
    }

    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
    {
        _innerStore.Update(data, storeDelegate);
        foreach (var entity in data)
        {
            SaveTranslations(entity);
        }
    }

    public void Delete(T data)
    {
        _innerStore.Delete(data);
        DeleteTranslations(data);
    }

    public void Delete(IEnumerable<T> data)
    {
        _innerStore.Delete(data);
        foreach (var entity in data)
        {
            DeleteTranslations(entity);
        }
    }

    public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (data.Guid == null || data.Guid == Guid.Empty)
        {
            return Create(data, storeDelegate);
        }
        else
        {
            Update(data, storeDelegate);
            return data.Guid ?? Guid.Empty;
        }
    }

    public void Init() => _innerStore.Init();
    public void Destroy() => _innerStore.Destroy();
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
        var entityType = typeof(T).Name;

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

    protected void ApplyTranslations(T entity)
    {
        if (entity.Guid == null)
        {
            return;
        }

        var filter = EntityTranslationFilter.ByEntityAndCulture(entity.Guid.Value, _context.CurrentCulture.Name);
        var translations = _translationStore.Read(filter.ToExpression());
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

    protected void SaveTranslations(T entity)
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
            var existing = _translationStore.Read(tFilter.ToExpression());

            var translation = existing.FirstOrDefault();
            if (translation != null)
            {
                translation.Value = value;
                translation.UpdatedAt = now;
                _translationStore.Update(translation);
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
                _translationStore.Create(translation);
            }
        }
    }

    protected void DeleteTranslations(T entity)
    {
        if (entity.Guid == null)
        {
            return;
        }

        var filter = EntityTranslationFilter.ByEntity(entity.Guid.Value);
        var translations = _translationStore.Read(filter.ToExpression());
        _translationStore.Delete(translations);
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
