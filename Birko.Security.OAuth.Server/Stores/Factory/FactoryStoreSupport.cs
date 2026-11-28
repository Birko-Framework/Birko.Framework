using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.Models;
using Birko.Data.Stores;

namespace Birko.Security.OAuth.Server.Stores.Factory;

/// <summary>What every OAuth factory store needs from an <see cref="IStoreFactory"/> (TASK-1013).</summary>
public static class FactoryStoreSupport
{
    /// <summary>
    /// The factory's store for <typeparamref name="T"/>, which must be able to claim atomically. A backend whose stores
    /// cannot (JSON, XML) is refused here, by name, rather than given the interfaces' read-check-write claim, under which
    /// two concurrent redemptions of one code both succeed.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
    /// <exception cref="ArgumentException">The backend's store does not implement <see cref="IAsyncConditionalUpdateStore{T}"/>.</exception>
    public static IAsyncBulkStore<T> RequireClaimingStore<T>(IStoreFactory factory) where T : AbstractModel
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        var store = factory.GetAsyncStore<T>();
        if (store is IAsyncConditionalUpdateStore<T>) return store;
        throw new ArgumentException(
            $"The '{factory.Backend}' store backend cannot hold the OAuth server stores: its store for {typeof(T).Name} "
            + $"({store.GetType().Name}) does not implement IAsyncConditionalUpdateStore<{typeof(T).Name}>, so redeeming a code "
            + "or rotating a refresh token could not be claimed atomically (two concurrent requests could both succeed). "
            + "Use a backend that can (SQL, MongoDB, RavenDB, Cosmos DB, Elasticsearch, InMemory).", nameof(factory));
    }

    /// <summary>
    /// Makes a write just made visible to lookups by value, on a store that needs it (<see cref="IAsyncRefreshableStore"/>:
    /// Elasticsearch, whose searches see a write only after a refresh). OAuth finds codes and tokens by value straight
    /// after writing them (a code is redeemed moments after it is issued), so every factory store calls this after each
    /// write; the user chose this over refusing Elasticsearch (TASK-1013). Other backends read their own writes at once.
    /// </summary>
    public static Task MakeVisibleAsync(object store, CancellationToken ct)
        => store is IAsyncRefreshableStore refreshable ? refreshable.RefreshAsync(ct) : Task.CompletedTask;
}

/// <summary>
/// An OAuth store over the factory's store for a model persisted as it is (TASK-1013): every member forwards. The
/// interfaces' named lookups (their default methods) read through <see cref="ReadAsync(Expression{Func{T, bool}}?, CancellationToken)"/>.
/// </summary>
public abstract class FactoryModelStore<T> : IAsyncStore<T> where T : AbstractModel, new()
{
    protected FactoryModelStore(IStoreFactory factory)
    {
        Store = FactoryStoreSupport.RequireClaimingStore<T>(factory);
        Claims = (IAsyncConditionalUpdateStore<T>)Store;
    }

    /// <summary>The factory's store.</summary>
    protected IAsyncBulkStore<T> Store { get; }

    /// <summary>The same store, as its conditional update.</summary>
    protected IAsyncConditionalUpdateStore<T> Claims { get; }

    public Task InitAsync(CancellationToken ct = default) => Store.InitAsync(ct);

    public Task DestroyAsync(CancellationToken ct = default) => Store.DestroyAsync(ct);

    public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => Store.CountAsync(filter, ct);

    public Task<T?> ReadAsync(Guid guid, CancellationToken ct = default) => Store.ReadAsync(guid, ct);

    public Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        => ((IAsyncReadStore<T>)Store).ReadAsync(filter, ct);

    public async Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        var guid = await Store.CreateAsync(data, processDelegate, ct).ConfigureAwait(false);
        await MadeVisibleAsync(ct).ConfigureAwait(false);
        return guid;
    }

    public async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        await Store.UpdateAsync(data, processDelegate, ct).ConfigureAwait(false);
        await MadeVisibleAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(T data, CancellationToken ct = default)
    {
        await Store.DeleteAsync(data, ct).ConfigureAwait(false);
        await MadeVisibleAsync(ct).ConfigureAwait(false);
    }

    public async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        var guid = await Store.SaveAsync(data, processDelegate, ct).ConfigureAwait(false);
        await MadeVisibleAsync(ct).ConfigureAwait(false);
        return guid;
    }

    public T CreateInstance() => new();

    /// <summary>After a write: see <see cref="FactoryStoreSupport.MakeVisibleAsync"/>.</summary>
    protected Task MadeVisibleAsync(CancellationToken ct) => FactoryStoreSupport.MakeVisibleAsync(Store, ct);
}

/// <summary>
/// An OAuth store over the factory's store for an entity that stores a model (TASK-1013): the model is mapped to and from
/// the entity in one place each (<see cref="ToEntity"/>, <see cref="ToModel"/>), and a predicate over the model is
/// translated to one over the entity, refusing a member the entity keeps inside a JSON column.
/// </summary>
public abstract class FactoryMappedStore<TModel, TEntity> : IAsyncStore<TModel>
    where TModel : AbstractModel, new()
    where TEntity : AbstractModel, new()
{
    private readonly ModelPredicateTranslator<TModel, TEntity> _translator;

    private protected FactoryMappedStore(IStoreFactory factory, ModelPredicateTranslator<TModel, TEntity> translator)
    {
        Entities = FactoryStoreSupport.RequireClaimingStore<TEntity>(factory);
        Claims = (IAsyncConditionalUpdateStore<TEntity>)Entities;
        _translator = translator;
    }

    /// <summary>The factory's store for the entity.</summary>
    protected IAsyncBulkStore<TEntity> Entities { get; }

    /// <summary>The same store, as its conditional update.</summary>
    protected IAsyncConditionalUpdateStore<TEntity> Claims { get; }

    /// <summary>Writes <paramref name="model"/>'s members onto <paramref name="entity"/>, leaving its Guid.</summary>
    protected abstract void ToEntity(TModel model, TEntity entity);

    /// <summary>The model <paramref name="entity"/> stores.</summary>
    protected abstract TModel ToModel(TEntity entity);

    /// <summary>The predicate over the entity; see <see cref="ModelPredicateTranslator{TModel, TEntity}"/>.</summary>
    protected Expression<Func<TEntity, bool>>? Translate(Expression<Func<TModel, bool>>? filter) => _translator.Translate(filter);

    private TModel? ToModelOrNull(TEntity? entity) => entity == null ? null : ToModel(entity);

    public Task InitAsync(CancellationToken ct = default) => Entities.InitAsync(ct);

    public Task DestroyAsync(CancellationToken ct = default) => Entities.DestroyAsync(ct);

    public Task<long> CountAsync(Expression<Func<TModel, bool>>? filter = null, CancellationToken ct = default)
        => Entities.CountAsync(Translate(filter), ct);

    public async Task<TModel?> ReadAsync(Guid guid, CancellationToken ct = default)
        => ToModelOrNull(await Entities.ReadAsync(guid, ct).ConfigureAwait(false));

    public async Task<TModel?> ReadAsync(Expression<Func<TModel, bool>>? filter = null, CancellationToken ct = default)
        => ToModelOrNull(await ((IAsyncReadStore<TEntity>)Entities).ReadAsync(Translate(filter), ct).ConfigureAwait(false));

    /// <summary>Creates the entity under <paramref name="data"/>'s Guid (a new one when it has none) and sets it on <paramref name="data"/>.</summary>
    public async Task<Guid> CreateAsync(TModel data, StoreDataDelegate<TModel>? processDelegate = null, CancellationToken ct = default)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        data = StoreDelegates.Apply(processDelegate, data);
        var entity = new TEntity { Guid = data.Guid is { } g && g != Guid.Empty ? g : Guid.NewGuid() };
        ToEntity(data, entity);
        var guid = await Entities.CreateAsync(entity, ct: ct).ConfigureAwait(false);
        await MadeVisibleAsync(ct).ConfigureAwait(false);
        data.Guid = guid;
        return guid;
    }

    /// <summary>Updates the stored entity with <paramref name="data"/>'s Guid; <see cref="EntityNotFoundException"/> when none is stored.</summary>
    public async Task UpdateAsync(TModel data, StoreDataDelegate<TModel>? processDelegate = null, CancellationToken ct = default)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        data = StoreDelegates.Apply(processDelegate, data);
        var existing = data.Guid is { } guid ? await Entities.ReadAsync(guid, ct).ConfigureAwait(false) : null;
        if (existing == null) throw new EntityNotFoundException(typeof(TModel), data.Guid);
        ToEntity(data, existing);
        await Entities.UpdateAsync(existing, ct: ct).ConfigureAwait(false);
        await MadeVisibleAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(TModel data, CancellationToken ct = default)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        var existing = data.Guid is { } guid ? await Entities.ReadAsync(guid, ct).ConfigureAwait(false) : null;
        if (existing == null) return;
        await Entities.DeleteAsync(existing, ct).ConfigureAwait(false);
        await MadeVisibleAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Creates the model, or updates the stored one with the same Guid.</summary>
    public async Task<Guid> SaveAsync(TModel data, StoreDataDelegate<TModel>? processDelegate = null, CancellationToken ct = default)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        data = StoreDelegates.Apply(processDelegate, data);
        if (data.Guid is { } guid && guid != Guid.Empty && await Entities.ReadAsync(guid, ct).ConfigureAwait(false) != null)
        {
            await UpdateAsync(data, ct: ct).ConfigureAwait(false);
            return guid;
        }
        return await CreateAsync(data, ct: ct).ConfigureAwait(false);
    }

    public TModel CreateInstance() => new();

    /// <summary>After a write: see <see cref="FactoryStoreSupport.MakeVisibleAsync"/>.</summary>
    protected Task MadeVisibleAsync(CancellationToken ct) => FactoryStoreSupport.MakeVisibleAsync(Entities, ct);
}
