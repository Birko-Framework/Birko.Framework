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
/// Sync store wrapper that intercepts reads to apply localized field values,
/// rewrites filter expressions to query the translation store for localized field conditions,
/// and intercepts creates/updates to persist translations for localizable fields.
/// </summary>
public class LocalizedStoreWrapper<TStore, T> : IStore<T>, IStoreWrapper<T>
    where TStore : IStore<T>
    where T : Data.Models.AbstractModel, ILocalizable
{
    protected readonly TStore _innerStore;
    protected readonly IBulkStore<EntityTranslationModel> _translationStore;
    protected readonly IEntityLocalizationContext _context;

    public LocalizedStoreWrapper(
        TStore innerStore,
        IBulkStore<EntityTranslationModel> translationStore,
        IEntityLocalizationContext context)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _translationStore = translationStore ?? throw new ArgumentNullException(nameof(translationStore));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

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
            return _innerStore.Read(filter);
        }

        var rewritten = RewriteFilter(filter);
        var entity = _innerStore.Read(rewritten);
        if (entity != null)
        {
            ApplyTranslations(entity);
        }
        return entity;
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

    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        _innerStore.Update(data, storeDelegate);
        SaveTranslations(data);
    }

    public void Delete(T data)
    {
        _innerStore.Delete(data);
        DeleteTranslations(data);
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

    /// <summary>
    /// Rewrites a filter expression: extracts localized field conditions,
    /// queries the translation store for matching entity GUIDs,
    /// and replaces localized conditions with a GUID membership test.
    /// </summary>
    protected Expression<Func<T, bool>>? RewriteFilter(Expression<Func<T, bool>>? filter)
    {
        var localizableFields = GetLocalizableFieldsFromInstance();
        var split = LocalizedExpressionAnalyzer.Split(filter, localizableFields);

        if (!split.HasLocalizedConditions)
        {
            return filter;
        }

        var matchingGuids = ResolveMatchingGuids(split.LocalizedConditions);

        // Build: x => matchingGuids.Contains(x.Guid)
        var guidFilter = LocalizedFilterHelper.BuildGuidFilter<T>(matchingGuids);

        if (split.RemainingFilter == null)
        {
            return guidFilter;
        }

        return LocalizedFilterHelper.CombineFilters(split.RemainingFilter, guidFilter);
    }

    /// <summary>
    /// Queries the translation store to find entity GUIDs that match all localized conditions.
    /// </summary>
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
        if (!IsNonDefaultCulture())
        {
            return;
        }

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
