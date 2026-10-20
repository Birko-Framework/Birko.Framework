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
public class LocalizedStoreWrapper<TStore, T, TTranslation> : IStore<T>, IStoreWrapper<T>
    where TStore : IStore<T>
    where T : Data.Models.AbstractModel, ILocalizable
    where TTranslation : Data.Models.AbstractModel, IEntityTranslation
{
    protected readonly TStore _innerStore;
    protected readonly IBulkStore<TTranslation> _translationStore;
    protected readonly IEntityLocalizationContext _context;
    protected readonly IReadOnlyList<string> _localizableFields;

    public LocalizedStoreWrapper(
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
        LocalizedEntityFields.RequireValidDeclarations(new[] { data });
        var guid = _innerStore.Create(data, storeDelegate);
        // SH-M076: keyed by the GUID the store returned — it need not have written it onto the entity.
        SaveTranslations(guid, LocalizedTranslationRows.ValuesOf(data));
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
            filter, GetLocalizableFieldsFromInstance(), _translationStore, _innerStore as IBulkStore<T>, _context.CurrentCulture.Name);

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

        // SH-H016: the result is a detached copy, never the instance the inner store returned.
        return LocalizedTranslationRows.LocalizeAll(_translationStore, new List<T> { entity }, _context.CurrentCulture.Name)[0];
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
        => LocalizedTranslationRows.DeleteMany(
            _translationStore, LocalizedTranslationRows.GuidsOf(new[] { entity }), LocalizedTranslationRows.EntityTypeOf<T>());

    #endregion

    #region Helpers

    protected IReadOnlyList<string> GetLocalizableFieldsFromInstance() => _localizableFields;

    #endregion
}

/// <summary>
/// <see cref="LocalizedStoreWrapper{TStore,T,TTranslation}"/> with the default translation model, <see cref="EntityTranslationModel"/>.
/// </summary>
public class LocalizedStoreWrapper<TStore, T> : LocalizedStoreWrapper<TStore, T, EntityTranslationModel>
    where TStore : IStore<T>
    where T : Data.Models.AbstractModel, ILocalizable
{
    public LocalizedStoreWrapper(
        TStore innerStore,
        IBulkStore<EntityTranslationModel> translationStore,
        IEntityLocalizationContext context)
        : base(innerStore, translationStore, context)
    {
    }
}
