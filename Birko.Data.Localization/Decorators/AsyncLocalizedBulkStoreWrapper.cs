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
    /// <summary>How a write touching both stores is made one unit (TASK-683); best effort when none is given.</summary>
    protected readonly ILocalizedWriteBoundary _boundary;

    public AsyncLocalizedBulkStoreWrapper(
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

    public Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        // Materialized here: a retried boundary runs the write again.
        var items = data as IList<T> ?? data.ToList();
        return IsNonDefaultCulture()
            ? _boundary.RunAsync(c => CreateWithTranslationsAsync(items, storeDelegate, c), ct)
            : CreateWithTranslationsAsync(items, storeDelegate, ct);
    }

    private async Task CreateWithTranslationsAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate, CancellationToken ct)
    {
        // CR-M100: materialize once — a lazy/one-shot IEnumerable would otherwise be enumerated
        // twice (once by the inner store, once here), re-running or exhausting the source.
        var items = data as IList<T> ?? data.ToList();
        LocalizedEntityFields.RequireValidDeclarations(items);
        await _innerStore.CreateAsync(items, storeDelegate, ct);
        await SaveTranslationsAsync(items, ct);
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

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T,TTranslation}.Update(IEnumerable{T}, StoreDataDelegate{T}?)" />
    public Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
    {
        var items = data as IList<T> ?? data.ToList();
        return IsNonDefaultCulture()
            ? _boundary.RunAsync(c => UpdateWithTranslationsAsync(items, storeDelegate, c), ct)
            : UpdateWithTranslationsAsync(items, storeDelegate, ct);
    }

    private async Task UpdateWithTranslationsAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate, CancellationToken ct)
    {
        var items = data as IList<T> ?? data.ToList();
        LocalizedEntityFields.RequireValidDeclarations(items);

        if (!IsNonDefaultCulture())
        {
            await _innerStore.UpdateAsync(items, storeDelegate, ct);
            await SaveTranslationsAsync(items, ct);
            return;
        }

        var fields = GetLocalizableFieldsFromInstance();
        var baseValues = await ReadBaseValuesAsync(items, fields, ct);

        try
        {
            await _innerStore.UpdateAsync(WithBaseValues(items, baseValues), storeDelegate, ct);
        }
        catch (EntityNotFoundException missing)
        {
            // TASK-555: the inner store wrote every stored item and reports the rest; their translations are saved too.
            await LocalizedCleanup.SaveStoredAsync(_boundary, () => SaveTranslationsAsync(LocalizedCleanup.Stored(items, missing.Guids), ct));
            throw;
        }
        await SaveTranslationsAsync(items, ct);
    }

    public Task DeleteAsync(T data, CancellationToken ct = default)
        => _boundary.RunAsync(c => DeleteWithTranslationsAsync(data, c), ct);

    private async Task DeleteWithTranslationsAsync(T data, CancellationToken ct)
    {
        await _innerStore.DeleteAsync(data, ct);
        await DeleteTranslationsAsync(data, ct);
    }

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T,TTranslation}.Update(Expression{Func{T, bool}}, Action{T})" />
    public Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default)
        => IsNonDefaultCulture()
            ? _boundary.RunAsync(c => UpdateWithTranslationsAsync(filter, updateAction, c), ct)
            : UpdateWithTranslationsAsync(filter, updateAction, ct);

    private async Task UpdateWithTranslationsAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct)
    {
        var localized = IsNonDefaultCulture();
        var effective = localized ? await RewriteFilterAsync(filter, ct) : filter;
        var items = (await _innerStore.ReadAsync(effective, null, null, null, ct)).ToList();
        if (!localized)
        {
            foreach (var item in items)
            {
                updateAction(item);
                try
                {
                    await _innerStore.UpdateAsync(item, ct: ct);
                }
                catch (EntityNotFoundException)
                {
                    // Deleted since the read: skipped, as the sync wrapper's inner filter update does.
                }
            }
            return;
        }

        var fields = GetLocalizableFieldsFromInstance();
        // TASK-690: the matched entities' translations in one read, not one per entity.
        var rows = await LocalizedTranslationRows.ReadManyAsync(
            _translationStore, items.Where(i => i.Guid.HasValue).Select(i => i.Guid!.Value),
            LocalizedTranslationRows.EntityTypeOf<T>(), _context.CurrentCulture.Name, ct);
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
                try
                {
                    await _innerStore.UpdateAsync(working, ct: ct);
                }
                catch (EntityNotFoundException)
                {
                    // Deleted since the read: skipped, as the default-culture path and the SQL store's own filter update do.
                    continue;
                }
                if (working.Guid is Guid changedGuid && changed.Count > 0)
                {
                    pending.Add((changedGuid, changed));
                }
            }
        }
        catch (Exception) when (pending.Count > 0 && !_boundary.IsAtomic)
        {
            await SaveAfterFailureAsync(pending);
            throw;
        }

        // TASK-690: one batched write for every entity the update changed.
        await SaveTranslationsAsync(pending, ct);
    }

    /// <summary>
    /// TASK-690 (decided by the user): when a filter-based update fails part way, the entities already written keep
    /// their translation changes. Saved best effort, and never instead of the original error: a failure here is
    /// dropped so the caller sees what actually broke. The caller's token is not used, because the entity writes it
    /// would abandon have already happened.
    /// </summary>
    private async Task SaveAfterFailureAsync(IReadOnlyCollection<(Guid EntityGuid, IReadOnlyDictionary<string, string?> Values)> pending)
    {
        try
        {
            await SaveTranslationsAsync(pending, CancellationToken.None);
        }
        catch (Exception)
        {
            // The original exception is rethrown by the caller.
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

    public Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default)
    {
        var items = data as IList<T> ?? data.ToList();
        return _boundary.RunAsync(c => DeleteWithTranslationsAsync(items, c), ct);
    }

    private async Task DeleteWithTranslationsAsync(IEnumerable<T> data, CancellationToken ct)
    {
        var items = data as IList<T> ?? data.ToList();
        await _innerStore.DeleteAsync(items, ct);
        await DeleteTranslationsAsync(items, ct);
    }

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T,TTranslation}.Delete(Expression{Func{T, bool}})" />
    public Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default)
        => _boundary.RunAsync(c => DeleteWithTranslationsAsync(filter, c), ct);

    private async Task DeleteWithTranslationsAsync(Expression<Func<T, bool>> filter, CancellationToken ct)
    {
        var effective = IsNonDefaultCulture() ? await RewriteFilterAsync(filter, ct) : filter;
        var items = (await _innerStore.ReadAsync(effective, null, null, null, ct)).ToList();
        await _innerStore.DeleteAsync(items, ct);
        await DeleteTranslationsAsync(items, ct);
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

        // SH-H016: the result is a detached copy, never the instance the inner store returned.
        return (await LocalizedTranslationRows.LocalizeAllAsync(_translationStore, new List<T> { entity }, _context.CurrentCulture.Name, ct))[0];
    }

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T,TTranslation}.LocalizeAll(System.Collections.Generic.List{T})" />
    protected Task<List<T>> LocalizeAllAsync(List<T> entities, CancellationToken ct)
        => IsNonDefaultCulture()
            ? LocalizedTranslationRows.LocalizeAllAsync(_translationStore, entities, _context.CurrentCulture.Name, ct)
            : Task.FromResult(entities);

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

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T,TTranslation}.SaveTranslations(IEnumerable{T})" />
    protected Task SaveTranslationsAsync(IEnumerable<T> entities, CancellationToken ct)
        => IsNonDefaultCulture() ? SaveTranslationsAsync(LocalizedTranslationRows.ValuesOfAll(entities), ct) : Task.CompletedTask;

    /// <inheritdoc cref="LocalizedStoreWrapper{TStore,T,TTranslation}.SaveTranslations(Guid?, IReadOnlyDictionary{string, string?})" />
    protected Task SaveTranslationsAsync(Guid? guid, IReadOnlyDictionary<string, string?> values, CancellationToken ct)
        => guid is Guid g ? SaveTranslationsAsync(new[] { (g, values) }, ct) : Task.CompletedTask;

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T,TTranslation}.SaveTranslations(IReadOnlyCollection{ValueTuple{Guid, IReadOnlyDictionary{string, string?}}})" />
    protected Task SaveTranslationsAsync(
        IReadOnlyCollection<(Guid EntityGuid, IReadOnlyDictionary<string, string?> Values)> values, CancellationToken ct)
    {
        if (!IsNonDefaultCulture())
        {
            return Task.CompletedTask;
        }

        return LocalizedTranslationRows.SaveManyAsync(
            _translationStore, LocalizedTranslationRows.EntityTypeOf<T>(), _context.CurrentCulture.Name,
            values.Where(v => v.EntityGuid != Guid.Empty), ct);
    }

    protected Task DeleteTranslationsAsync(T entity, CancellationToken ct)
        => DeleteTranslationsAsync(new[] { entity }, ct);

    /// <inheritdoc cref="LocalizedBulkStoreWrapper{TStore,T,TTranslation}.DeleteTranslations(IEnumerable{T})" />
    protected Task DeleteTranslationsAsync(IEnumerable<T> entities, CancellationToken ct)
    {
        var guids = LocalizedTranslationRows.GuidsOf(entities).Distinct().ToList();
        return LocalizedCleanup.RunAsync(_boundary, typeof(T), guids, c => LocalizedTranslationRows.DeleteManyAsync(
            _translationStore, guids, LocalizedTranslationRows.EntityTypeOf<T>(), c), ct);
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
        IEntityLocalizationContext context,
        ILocalizedWriteBoundary? boundary = null)
        : base(innerStore, translationStore, context, boundary)
    {
    }
}
