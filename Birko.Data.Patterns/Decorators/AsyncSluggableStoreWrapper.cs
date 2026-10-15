using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Async store wrapper that normalizes and ensures slug uniqueness on Create/Update.
/// </summary>
public class AsyncSluggableStoreWrapper<TStore, T> : IAsyncStore<T>, IStoreWrapper<T>
    where TStore : IAsyncStore<T>
    where T : Data.Models.AbstractModel, ISluggable
{
    protected readonly TStore _innerStore;

    public AsyncSluggableStoreWrapper(TStore innerStore)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
    }

    public Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
        => _innerStore.ReadAsync(guid, ct);

    public Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        => _innerStore.ReadAsync(filter, ct);

    public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        => _innerStore.CountAsync(filter, ct);

    public async Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        await ResolveSlugAsync(data, excludeId: null, ct: ct);
        return await _innerStore.CreateAsync(data, processDelegate, ct);
    }

    public async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        await ResolveSlugAsync(data, data.Guid, ct: ct);
        await _innerStore.UpdateAsync(data, processDelegate, ct);
    }

    public Task DeleteAsync(T data, CancellationToken ct = default)
        => _innerStore.DeleteAsync(data, ct);

    public async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        // TASK-555: the create-or-update decision has one producer; an unstored Guid is created, not lost.
        return await StoreSave.SaveAsync(data, processDelegate, CreateAsync, UpdateAsync, ct);
    }

    public Task InitAsync(CancellationToken ct = default) => _innerStore.InitAsync(ct);
    public Task DestroyAsync(CancellationToken ct = default) => _innerStore.DestroyAsync(ct);
    public T CreateInstance() => _innerStore.CreateInstance();

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;

    protected async Task ResolveSlugAsync(T data, Guid? excludeId, HashSet<string>? batchSlugs = null, CancellationToken ct = default)
    {
        var source = !string.IsNullOrWhiteSpace(data.Slug) ? data.Slug : data.GetSlugSource();
        var baseSlug = SlugGenerator.Normalize(source);

        data.Slug = await SlugGenerator.EnsureUniqueAsync(
            baseSlug,
            async slug =>
            {
                if (batchSlugs?.Contains(slug) == true)
                    return true;
                if (data.IsReservedSlug(slug))
                    return true;
                var existing = await _innerStore.ReadAsync(BuildSlugFilter(slug, excludeId), ct);
                // SH-M326 (TASK-159): ANY other row holding the slug, asked in the filter. Reading one row and comparing its
                // Guid answered "free" whenever the store happened to return this row, so an existing duplicate survived.
                return existing is not null;
            },
            fallback: "item");
    }

    /// <summary>
    /// Builds an expression filter for matching a specific slug value.
    /// </summary>
    protected static Expression<Func<T, bool>> BuildSlugFilter(string slug, Guid? excludeId = null)
    {
        if (excludeId == null)
            return entity => entity.Slug == slug;
        return entity => entity.Slug == slug && entity.Guid != excludeId;
    }
}
