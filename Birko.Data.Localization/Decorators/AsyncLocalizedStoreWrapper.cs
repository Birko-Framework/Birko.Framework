using Birko.Data.Exceptions;
using Birko.Data.Localization.Boundaries;
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
public class AsyncLocalizedStoreWrapper<TStore, T, TTranslation> : IAsyncStore<T>, IStoreWrapper<T>
    where TStore : IAsyncStore<T>
    where T : Data.Models.AbstractModel, ILocalizable
    where TTranslation : Data.Models.AbstractModel, IEntityTranslation
{
    protected readonly TStore _innerStore;
    protected readonly IAsyncBulkStore<TTranslation> _translationStore;
    protected readonly IEntityLocalizationContext _context;
    protected readonly IReadOnlyList<string> _localizableFields;
    /// <summary>How a write touching both stores is made one unit (TASK-683); best effort when none is given.</summary>
    protected readonly ILocalizedWriteBoundary _boundary;

    public AsyncLocalizedStoreWrapper(
        TStore innerStore,
        IAsyncBulkStore<TTranslation> translationStore,
        IEntityLocalizationContext context,
        ILocalizedWriteBoundary? boundary = null)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _translationStore = translationStore ?? throw new ArgumentNullException(nameof(translationStore));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _boundary = boundary ?? BestEffortWriteBoundary.Instance;
        // SH-L079 / SH-L082 (TASK-194): the declaration is read and validated once, at wiring, on every culture.
        _localizableFields = LocalizedEntityFields.LocalizableFieldsOf(_innerStore.CreateInstance());
        LocalizedTranslationRows.RequireQueryableModel<TTranslation>();
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
        if (!IsNonDefaultCulture())
        {
            return await CreateWithTranslationsAsync(data, processDelegate, ct);
        }
        var guid = Guid.Empty;
        await _boundary.RunAsync(async c => guid = await CreateWithTranslationsAsync(data, processDelegate, c), ct);
        return guid;
    }

    private async Task<Guid> CreateWithTranslationsAsync(T data, StoreDataDelegate<T>? processDelegate, CancellationToken ct)
    {
        LocalizedEntityFields.RequireValidDeclarations(new[] { data });
        var guid = await _innerStore.CreateAsync(data, processDelegate, ct);
        // SH-M076: keyed by the GUID the store returned — it need not have written it onto the entity.
        await SaveTranslationsAsync(guid, LocalizedTranslationRows.ValuesOf(data), ct);
        return guid;
    }

    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T,TTranslation}.Update(T, StoreDataDelegate{T}?)" />
    public Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        => IsNonDefaultCulture()
            ? _boundary.RunAsync(c => UpdateWithTranslationsAsync(data, processDelegate, c), ct)
            : UpdateWithTranslationsAsync(data, processDelegate, ct);

    private async Task UpdateWithTranslationsAsync(T data, StoreDataDelegate<T>? processDelegate, CancellationToken ct)
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

    public Task DeleteAsync(T data, CancellationToken ct = default)
        => _boundary.RunAsync(c => DeleteWithTranslationsAsync(data, c), ct);

    private async Task DeleteWithTranslationsAsync(T data, CancellationToken ct)
    {
        await _innerStore.DeleteAsync(data, ct);
        await DeleteTranslationsAsync(data, ct);
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
            filter, GetLocalizableFieldsFromInstance(), _translationStore, _innerStore as IAsyncBulkStore<T>, _context.CurrentCulture.Name, ct);

    #endregion

    #region Translation Application

    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T,TTranslation}.Localize(T)" />
    protected async Task<T> LocalizeAsync(T entity, CancellationToken ct)
    {
        if (!IsNonDefaultCulture())
        {
            return entity;
        }

        // SH-H016: the result is a detached copy, never the instance the inner store returned.
        return (await LocalizedTranslationRows.LocalizeAllAsync(_translationStore, new List<T> { entity }, _context.CurrentCulture.Name, ct))[0];
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

    protected Task DeleteTranslationsAsync(T entity, CancellationToken ct)
    {
        var guids = LocalizedTranslationRows.GuidsOf(new[] { entity }).ToList();
        return LocalizedCleanup.RunAsync(_boundary, typeof(T), guids, c => LocalizedTranslationRows.DeleteManyAsync(
            _translationStore, guids, LocalizedTranslationRows.EntityTypeOf<T>(), c), ct);
    }

    #endregion

    #region Helpers

    protected IReadOnlyList<string> GetLocalizableFieldsFromInstance() => _localizableFields;

    #endregion
}

/// <summary>
/// <see cref="AsyncLocalizedStoreWrapper{TStore,T,TTranslation}"/> with the default translation model, <see cref="EntityTranslationModel"/>.
/// </summary>
public class AsyncLocalizedStoreWrapper<TStore, T> : AsyncLocalizedStoreWrapper<TStore, T, EntityTranslationModel>
    where TStore : IAsyncStore<T>
    where T : Data.Models.AbstractModel, ILocalizable
{
    public AsyncLocalizedStoreWrapper(
        TStore innerStore,
        IAsyncBulkStore<EntityTranslationModel> translationStore,
        IEntityLocalizationContext context,
        ILocalizedWriteBoundary? boundary = null)
        : base(innerStore, translationStore, context, boundary)
    {
    }
}
