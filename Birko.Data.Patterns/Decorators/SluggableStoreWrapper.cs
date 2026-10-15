using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Store wrapper that normalizes and ensures slug uniqueness on Create/Update.
/// </summary>
public class SluggableStoreWrapper<TStore, T> : IStore<T>, IStoreWrapper<T>
    where TStore : IStore<T>
    where T : Data.Models.AbstractModel, ISluggable
{
    protected readonly TStore _innerStore;

    public SluggableStoreWrapper(TStore innerStore)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
    }

    public T? Read(Guid guid) => _innerStore.Read(guid);

    public T? Read(Expression<Func<T, bool>>? filter = null) => _innerStore.Read(filter);

    public long Count(Expression<Func<T, bool>>? filter = null) => _innerStore.Count(filter);

    public Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        ResolveSlug(data, excludeId: null);
        return _innerStore.Create(data, storeDelegate);
    }

    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        ResolveSlug(data, data.Guid);
        _innerStore.Update(data, storeDelegate);
    }

    public void Delete(T data) => _innerStore.Delete(data);

    public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null)
    {
        // TASK-555: the create-or-update decision has one producer; an unstored Guid is created, not lost.
        return StoreSave.Save(data, storeDelegate, Create, Update);
    }

    public void Init() => _innerStore.Init();
    public void Destroy() => _innerStore.Destroy();
    public T CreateInstance() => _innerStore.CreateInstance();

    object? IStoreWrapper.GetInnerStore() => _innerStore;
    public TInner? GetInnerStoreAs<TInner>() where TInner : class => _innerStore as TInner;

    protected void ResolveSlug(T data, Guid? excludeId, HashSet<string>? batchSlugs = null)
    {
        var source = !string.IsNullOrWhiteSpace(data.Slug) ? data.Slug : data.GetSlugSource();
        var baseSlug = SlugGenerator.Normalize(source);

        data.Slug = SlugGenerator.EnsureUnique(
            baseSlug,
            slug =>
            {
                if (batchSlugs?.Contains(slug) == true)
                    return true;
                if (data.IsReservedSlug(slug))
                    return true;
                var existing = _innerStore.Read(BuildSlugFilter(slug, excludeId));
                // SH-M326 (TASK-159): ANY other row holding the slug, asked in the filter. Reading one row and comparing its
                // Guid answered "free" whenever the store happened to return this row, so an existing duplicate survived.
                return existing is not null;
            },
            fallback: "item");
    }

    protected static Expression<Func<T, bool>> BuildSlugFilter(string slug, Guid? excludeId = null)
    {
        if (excludeId == null)
            return entity => entity.Slug == slug;
        return entity => entity.Slug == slug && entity.Guid != excludeId;
    }
}
