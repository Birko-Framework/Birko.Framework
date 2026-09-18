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
        return entity == null ? null : Localize(entity);
    }

    public T? Read(Expression<Func<T, bool>>? filter = null)
    {
        if (!IsNonDefaultCulture())
        {
            return _innerStore.Read(filter);
        }

        var rewritten = RewriteFilter(filter);
        var entity = _innerStore.Read(rewritten);
        return entity == null ? null : Localize(entity);
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

    /// <summary>
    /// Persists <paramref name="data"/>, keeping the base column on the default culture.
    /// </summary>
    /// <remarks>
    /// SH-H015: this used to hand the caller's entity straight to the inner store and only then call
    /// <see cref="SaveTranslations"/>. Under a non-default culture the entity's localizable properties
    /// hold the <i>translated</i> text, so the base column was overwritten with it and the stored
    /// default-culture value destroyed — silently, and permanently once the translation row was written
    /// beside it. The base values are now read back from the store and put on the entity for the
    /// duration of the inner write, so the base column keeps the default culture and the caller's text
    /// reaches the translation row instead. Costs one extra read per update on a non-default culture.
    /// </remarks>
    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        if (!IsNonDefaultCulture())
        {
            _innerStore.Update(data, storeDelegate);
            SaveTranslations(data);
            return;
        }

        var fields = data.GetLocalizableFields();
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
    /// Reads the stored (untranslated) values of <paramref name="fields"/> for an entity, or
    /// <c>null</c> when there is no stored row to preserve.
    /// </summary>
    /// <remarks>
    /// This deliberately goes to <c>_innerStore</c> rather than through this wrapper: the wrapper's own
    /// read applies translations, and the value needed here is the base one.
    /// </remarks>
    /// <summary>
    /// Returns the entity the inner store should persist: a detached copy whose localizable fields
    /// carry <paramref name="baseValues"/>, or the caller's own entity when there is nothing stored to
    /// preserve (a first write has no default-culture value to keep).
    /// </summary>
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

    protected IReadOnlyDictionary<string, string?>? ReadBaseValues(Guid? guid, IReadOnlyList<string> fields)
    {
        if (guid == null || guid == Guid.Empty)
        {
            return null;
        }

        var stored = _innerStore.Read(guid.Value);
        return stored == null ? null : LocalizedEntityFields.Capture(stored, fields);
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

    /// <summary>
    /// Returns the entity as the current culture sees it: the same instance on the default culture,
    /// and a <b>detached</b> translated copy otherwise.
    /// </summary>
    /// <remarks>
    /// SH-H016: this used to write the translations into the instance the inner store returned. An
    /// <c>InMemory</c> store hands back the object held in its dictionary and a caching decorator hands
    /// back the cached reference, so a single read under a non-default culture replaced the
    /// <i>store's</i> default-culture values — after which a default-culture read came back translated
    /// and any later update persisted it. Nothing in <c>IStore&lt;T&gt;</c> promises a detached read, so
    /// the copy is made here rather than assumed.
    ///
    /// The copy is taken whenever the culture is non-default and the entity is translatable, not only
    /// when a translation row happens to exist. The rule a caller can hold is then "a non-default-culture
    /// read returns a detached entity" — where copying only on a hit would silently vary per entity
    /// inside one result set, which is exactly the kind of difference a test passes by luck.
    /// </remarks>
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

        var localized = LocalizedEntityFields.Detach(entity);

        var filter = EntityTranslationFilter.ByEntityAndCulture(entity.Guid.Value, _context.CurrentCulture.Name);
        var translations = _translationStore.Read(filter.ToExpression());
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
